using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Marketplace.Application.Payments;
using Marketplace.Domain.Common;
using Marketplace.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Marketplace.IntegrationTests.Payments;

// T-031 and section 9.11. These need no containers; they live here because only this project references Infrastructure.
public sealed class PaymentProviderTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly IOptions<PaymentOptions> _options = Options.Create(new PaymentOptions());

    private FakePaymentProvider NewFake() => new(_options, _time);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Fake_SameIdempotencyKey_ReturnsSamePayment()
    {
        var fake = NewFake();
        var request = new CreateProviderPayment(Guid.CreateVersion7(), Money.Eur(19.99m), "order-1-attempt-1");

        var first = await fake.CreatePaymentAsync(request, Token);
        var second = await fake.CreatePaymentAsync(request, Token);
        var other = await fake.CreatePaymentAsync(request with { IdempotencyKey = "order-1-attempt-2" }, Token);

        second.ProviderPaymentId.ShouldBe(first.ProviderPaymentId);
        other.ProviderPaymentId.ShouldNotBe(first.ProviderPaymentId);
        first.Status.ShouldBe(ProviderPaymentStatus.RequiresPayment);
        first.ClientSecret.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Fake_CompleteAndCancel_ChangeStatus()
    {
        var fake = NewFake();
        var completed = await fake.CreatePaymentAsync(new CreateProviderPayment(Guid.CreateVersion7(), Money.Eur(5m), "a"), Token);
        var canceled = await fake.CreatePaymentAsync(new CreateProviderPayment(Guid.CreateVersion7(), Money.Eur(5m), "b"), Token);

        fake.CompletePayment(completed.ProviderPaymentId);
        await fake.CancelPaymentAsync(canceled.ProviderPaymentId, Token);

        (await fake.GetPaymentAsync(completed.ProviderPaymentId, Token)).Status.ShouldBe(ProviderPaymentStatus.Succeeded);
        (await fake.GetPaymentAsync(canceled.ProviderPaymentId, Token)).Status.ShouldBe(ProviderPaymentStatus.Canceled);
    }

    [Fact]
    public async Task Fake_Refund_IsIdempotentAndNeedsASucceededPayment()
    {
        var fake = NewFake();
        var payment = await fake.CreatePaymentAsync(new CreateProviderPayment(Guid.CreateVersion7(), Money.Eur(5m), "c"), Token);

        await Should.ThrowAsync<InvalidOperationException>(() => fake.RefundAsync(payment.ProviderPaymentId, Money.Eur(5m), "refund-1", Token));
        fake.CompletePayment(payment.ProviderPaymentId);
        var first = await fake.RefundAsync(payment.ProviderPaymentId, Money.Eur(5m), "refund-2", Token);
        var repeat = await fake.RefundAsync(payment.ProviderPaymentId, Money.Eur(5m), "refund-2", Token);

        repeat.ProviderRefundId.ShouldBe(first.ProviderRefundId);
    }

    [Fact]
    public async Task Fake_AnotherProcessesPayment_IsAdoptedForRefundAndCancel()
    {
        // The Api and Worker each hold their own fake provider; the Worker refunds and cancels payments it never saw.
        var worker = NewFake();

        var refund = await worker.RefundAsync("pi_fake_0123abcd", Money.Eur(19.99m), "refund-1", TestContext.Current.CancellationToken);
        await worker.CancelPaymentAsync("pi_fake_4567ef01", TestContext.Current.CancellationToken);

        refund.WasAlreadyRefunded.ShouldBeFalse();
        (await worker.GetPaymentAsync("pi_fake_4567ef01", TestContext.Current.CancellationToken)).Status.ShouldBe(ProviderPaymentStatus.Canceled);
        await Should.ThrowAsync<InvalidOperationException>(() => worker.RefundAsync("pi_other_1", Money.Eur(1m), "refund-2", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Fake_FailNext_ThrowsUnavailableThenRecovers()
    {
        var fake = NewFake();
        fake.FailNext(2);

        await Should.ThrowAsync<PaymentProviderUnavailableException>(() => fake.CreatePaymentAsync(new CreateProviderPayment(Guid.CreateVersion7(), Money.Eur(5m), "d"), Token));
        await Should.ThrowAsync<PaymentProviderUnavailableException>(() => fake.CreatePaymentAsync(new CreateProviderPayment(Guid.CreateVersion7(), Money.Eur(5m), "d"), Token));
        (await fake.CreatePaymentAsync(new CreateProviderPayment(Guid.CreateVersion7(), Money.Eur(5m), "d"), Token)).ShouldNotBeNull();
        fake.CallCount.ShouldBe(3);
    }

    [Fact]
    public async Task Fake_TimeoutNext_DelaysUntilCancelled()
    {
        var fake = NewFake();
        fake.TimeoutNext(1);
        fake.TimeoutDelay = TimeSpan.FromSeconds(10);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Should.ThrowAsync<OperationCanceledException>(() =>
            fake.CreatePaymentAsync(new CreateProviderPayment(Guid.CreateVersion7(), Money.Eur(5m), "e"), timeout.Token));
    }

    [Fact]
    public async Task Fake_SignedWebhook_ParsesToResult()
    {
        var fake = NewFake();
        var payment = await fake.CreatePaymentAsync(new CreateProviderPayment(Guid.CreateVersion7(), Money.Eur(19.99m), "f"), Token);
        var (headers, body) = fake.CompletePayment(payment.ProviderPaymentId);

        var result = fake.ParseWebhook(headers, body);

        result.Type.ShouldBe(PaymentEventType.Succeeded);
        result.ProviderPaymentId.ShouldBe(payment.ProviderPaymentId);
        result.Amount.ShouldBe(Money.Eur(19.99m));
        result.ProviderEventId.ShouldStartWith("evt_fake_");
    }

    [Fact]
    public void Fake_TamperedOrStaleOrForeignWebhook_IsRejected()
    {
        var fake = NewFake();
        var (headers, body) = fake.CreateWebhook(PaymentEventType.Succeeded, "pi_x", Money.Eur(1m));
        var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(body).Replace("1.00", "0.01", StringComparison.Ordinal));
        var foreign = new FakePaymentProvider(Options.Create(new PaymentOptions { Fake = new() { WebhookSecret = "another-secret" } }), _time);

        Should.Throw<WebhookSignatureException>(() => fake.ParseWebhook(headers, tampered));
        Should.Throw<WebhookSignatureException>(() => foreign.ParseWebhook(headers, body));
        Should.Throw<WebhookSignatureException>(() => fake.ParseWebhook(new Dictionary<string, string>(), body));
        _time.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        Should.Throw<WebhookSignatureException>(() => fake.ParseWebhook(headers, body));
    }

    [Fact]
    public void Stripe_SignedPaymentIntentSucceeded_MapsToSucceeded()
    {
        const string secret = "whsec_test_only";
        var stripe = new StripePaymentProvider(
            Options.Create(new PaymentOptions { Provider = PaymentOptions.StripeName, Stripe = new() { WebhookSecret = secret } }),
            new Stripe.StripeClient("sk_test_unused"),
            _time);
        var created = _time.GetUtcNow().ToUnixTimeSeconds();
        var json = """
            {"id":"evt_123","object":"event","api_version":"2025-01-01","created":CREATED,"type":"payment_intent.succeeded",
             "data":{"object":{"id":"pi_123","object":"payment_intent","amount":1999,"amount_received":1999,"currency":"eur","status":"succeeded"}}}
            """.Replace("CREATED", created.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{created}.{json}")));
        var headers = new Dictionary<string, string> { [StripePaymentProvider.SignatureHeader] = string.Create(CultureInfo.InvariantCulture, $"t={created},v1={signature}") };

        var result = stripe.ParseWebhook(headers, Encoding.UTF8.GetBytes(json));

        result.ProviderEventId.ShouldBe("evt_123");
        result.Type.ShouldBe(PaymentEventType.Succeeded);
        result.ProviderPaymentId.ShouldBe("pi_123");
        result.Amount.ShouldBe(Money.Eur(19.99m));
        Should.Throw<WebhookSignatureException>(() => stripe.ParseWebhook(headers, Encoding.UTF8.GetBytes(json + " ")));
    }
}
