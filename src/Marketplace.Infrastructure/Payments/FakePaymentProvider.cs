using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marketplace.Application.Payments;
using Marketplace.Domain.Common;
using Microsoft.Extensions.Options;

namespace Marketplace.Infrastructure.Payments;

// Section 16 "Payments" configuration.
public sealed class PaymentOptions
{
    public const string SectionName = "Payments";
    public const string FakeName = "Fake";
    public const string StripeName = "Stripe";

    public string Provider { get; set; } = FakeName;

    // Payments:Fake:* and Payments:Stripe:* (section 16).
    public FakeOptions Fake { get; set; } = new();

    public StripeOptions Stripe { get; set; } = new();

    public sealed class FakeOptions
    {
        public string WebhookSecret { get; set; } = "dev-fake-secret";
    }

    public sealed class StripeOptions
    {
        public string? SecretKey { get; set; }

        public string? WebhookSecret { get; set; }

        public string? PublishableKey { get; set; }
    }
}

// D-25 and section 9.11: an in-memory provider for development and tests. It behaves like Stripe where it
// matters here: a repeated idempotency key returns the same payment, and webhooks are signed and time-checked.
public sealed class FakePaymentProvider(IOptions<PaymentOptions> options, TimeProvider timeProvider) : IPaymentProvider
{
    public const string SignatureHeader = "X-Fake-Signature";
    public static readonly TimeSpan SignatureTolerance = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, FakePayment> _payments = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _paymentsByIdempotencyKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProviderRefund> _refundsByIdempotencyKey = new(StringComparer.Ordinal);
    private int _failNext;
    private int _timeoutNext;

    public string Name => PaymentOptions.FakeName;

    // Test controls (section 9.11).
    public TimeSpan Latency { get; set; } = TimeSpan.Zero;

    public TimeSpan TimeoutDelay { get; set; } = TimeSpan.FromSeconds(30);

    public int CallCount => _callCount;

    private int _callCount;

    public void FailNext(int count) => Interlocked.Exchange(ref _failNext, count);

    public void TimeoutNext(int count) => Interlocked.Exchange(ref _timeoutNext, count);

    public async Task<ProviderPayment> CreatePaymentAsync(CreateProviderPayment request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await SimulateAsync(cancellationToken);

        var id = _paymentsByIdempotencyKey.GetOrAdd(request.IdempotencyKey, _ =>
        {
            var payment = new FakePayment("pi_fake_" + Guid.CreateVersion7().ToString("N"), request.OrderId, request.Amount);
            _payments[payment.Id] = payment;
            return payment.Id;
        });
        return _payments[id].ToProviderPayment();
    }

    public async Task<ProviderPayment> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken)
    {
        await SimulateAsync(cancellationToken);
        return Find(providerPaymentId).ToProviderPayment();
    }

    public async Task CancelPaymentAsync(string providerPaymentId, CancellationToken cancellationToken)
    {
        await SimulateAsync(cancellationToken);
        var payment = Find(providerPaymentId);
        if (payment.Status == ProviderPaymentStatus.RequiresPayment)
        {
            payment.Status = ProviderPaymentStatus.Canceled;
        }
    }

    public async Task<ProviderRefund> RefundAsync(string providerPaymentId, Money amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        await SimulateAsync(cancellationToken);
        var payment = Find(providerPaymentId);
        return _refundsByIdempotencyKey.GetOrAdd(idempotencyKey, _ =>
        {
            if (payment.Status != ProviderPaymentStatus.Succeeded)
            {
                throw new InvalidOperationException($"Payment {providerPaymentId} has not succeeded and cannot be refunded.");
            }

            payment.RefundedAmount += amount.Amount;
            return new ProviderRefund("re_fake_" + Guid.CreateVersion7().ToString("N"), WasAlreadyRefunded: false);
        });
    }

    public PaymentWebhookResult ParseWebhook(IReadOnlyDictionary<string, string> headers, byte[] rawBody)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rawBody);

        if (!headers.TryGetValue(SignatureHeader, out var header) || !TryParseSignature(header, out var timestamp, out var signature))
        {
            throw new WebhookSignatureException("The webhook has no valid signature header.");
        }

        var expected = Sign(timestamp, rawBody);
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature)))
        {
            throw new WebhookSignatureException("The webhook signature does not match.");
        }

        var sentAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        if ((timeProvider.GetUtcNow() - sentAt).Duration() > SignatureTolerance)
        {
            throw new WebhookSignatureException("The webhook timestamp is outside the tolerance.");
        }

        return ReadStoredEvent(Encoding.UTF8.GetString(rawBody));
    }

    public PaymentWebhookResult ReadStoredEvent(string rawPayload)
    {
        var payload = rawPayload;
        var webhook = JsonSerializer.Deserialize<FakeWebhook>(payload, _json) ?? throw new WebhookSignatureException("The webhook body is empty.");
        return new PaymentWebhookResult(
            webhook.EventId,
            webhook.Type,
            webhook.PaymentId,
            webhook.Amount is null ? null : new Money(decimal.Parse(webhook.Amount, CultureInfo.InvariantCulture), webhook.Currency ?? Money.EuroCurrency),
            DateTimeOffset.FromUnixTimeSeconds(webhook.Created),
            payload);
    }

    // Marks the payment succeeded and returns the signed webhook a real provider would send (used by EP-99 and tests).
    public (IReadOnlyDictionary<string, string> Headers, byte[] Body) CompletePayment(string providerPaymentId, Money? reportedAmount = null)
    {
        var payment = Find(providerPaymentId);
        payment.Status = ProviderPaymentStatus.Succeeded;
        return CreateWebhook(PaymentEventType.Succeeded, providerPaymentId, reportedAmount ?? payment.Amount);
    }

    public (IReadOnlyDictionary<string, string> Headers, byte[] Body) FailPayment(string providerPaymentId)
    {
        var payment = Find(providerPaymentId);
        payment.Status = ProviderPaymentStatus.Failed;
        return CreateWebhook(PaymentEventType.Failed, providerPaymentId, payment.Amount);
    }

    public (IReadOnlyDictionary<string, string> Headers, byte[] Body) CreateWebhook(
        PaymentEventType type, string providerPaymentId, Money? amount, string? eventId = null)
    {
        var now = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var webhook = new FakeWebhook(
            eventId ?? "evt_fake_" + Guid.CreateVersion7().ToString("N"),
            type,
            providerPaymentId,
            amount?.Amount.ToString("0.00", CultureInfo.InvariantCulture),
            amount?.Currency,
            now);
        var body = JsonSerializer.SerializeToUtf8Bytes(webhook, _json);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SignatureHeader] = $"t={now},v1={Sign(now, body)}",
        };
        return (headers, body);
    }

    private string Sign(long timestamp, byte[] body)
    {
        var secret = Encoding.UTF8.GetBytes(options.Value.Fake.WebhookSecret);
        var signed = Encoding.UTF8.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + ".").Concat(body).ToArray();
        return Convert.ToHexStringLower(HMACSHA256.HashData(secret, signed));
    }

    private static bool TryParseSignature(string header, out long timestamp, out string signature)
    {
        timestamp = 0;
        signature = string.Empty;
        foreach (var part in header.Split(','))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2)
            {
                return false;
            }

            if (pair[0] == "t" && !long.TryParse(pair[1], NumberStyles.None, CultureInfo.InvariantCulture, out timestamp))
            {
                return false;
            }

            if (pair[0] == "v1")
            {
                signature = pair[1];
            }
        }

        return timestamp > 0 && signature.Length > 0;
    }

    private async Task SimulateAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        if (Latency > TimeSpan.Zero)
        {
            await Task.Delay(Latency, timeProvider, cancellationToken);
        }

        if (Interlocked.Decrement(ref _timeoutNext) >= 0)
        {
            await Task.Delay(TimeoutDelay, cancellationToken);
        }

        if (Interlocked.Decrement(ref _failNext) >= 0)
        {
            throw new PaymentProviderUnavailableException("Simulated provider failure.");
        }
    }

    private FakePayment Find(string providerPaymentId) =>
        _payments.TryGetValue(providerPaymentId, out var payment)
            ? payment
            : throw new InvalidOperationException($"Unknown fake payment {providerPaymentId}.");

    private sealed class FakePayment(string id, Guid orderId, Money amount)
    {
        public string Id { get; } = id;

        public Guid OrderId { get; } = orderId;

        public Money Amount { get; } = amount;

        public ProviderPaymentStatus Status { get; set; } = ProviderPaymentStatus.RequiresPayment;

        public decimal RefundedAmount { get; set; }

        public ProviderPayment ToProviderPayment() => new(Id, $"{Id}_secret_fake", Status, Amount);
    }

    private sealed record FakeWebhook(string EventId, PaymentEventType Type, string? PaymentId, string? Amount, string? Currency, long Created);
}
