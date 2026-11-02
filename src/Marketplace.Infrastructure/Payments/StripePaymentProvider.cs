using System.Text;
using Marketplace.Application.Payments;
using Marketplace.Domain.Common;
using Microsoft.Extensions.Options;
using Stripe;

namespace Marketplace.Infrastructure.Payments;

// D-25: Stripe PaymentIntents. Every call carries an idempotency key, so retries are safe (section 10.2).
// Exercised against Stripe's test mode in staging (T-064); automated tests use FakePaymentProvider.
public sealed class StripePaymentProvider(IOptions<PaymentOptions> options, IStripeClient client, TimeProvider timeProvider) : IPaymentProvider
{
    public const string SignatureHeader = "Stripe-Signature";
    private const long _toleranceSeconds = 300;

    public string Name => PaymentOptions.StripeName;

    public async Task<ProviderPayment> CreatePaymentAsync(CreateProviderPayment request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var intent = await CallAsync(() => new PaymentIntentService(client).CreateAsync(
            new PaymentIntentCreateOptions
            {
                Amount = ToMinorUnits(request.Amount),
                Currency = request.Amount.Currency.ToLowerInvariant(),
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
                Metadata = new Dictionary<string, string> { ["orderId"] = request.OrderId.ToString() },
            },
            new RequestOptions { IdempotencyKey = request.IdempotencyKey },
            cancellationToken));
        return ToProviderPayment(intent);
    }

    public async Task<ProviderPayment> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken) =>
        ToProviderPayment(await CallAsync(() => new PaymentIntentService(client).GetAsync(providerPaymentId, cancellationToken: cancellationToken)));

    public async Task CancelPaymentAsync(string providerPaymentId, CancellationToken cancellationToken)
    {
        try
        {
            await CallAsync(() => new PaymentIntentService(client).CancelAsync(
                providerPaymentId, requestOptions: new RequestOptions { IdempotencyKey = $"cancel-{providerPaymentId}" }, cancellationToken: cancellationToken));
        }
        catch (StripeException exception) when (exception.StripeError?.Code == "payment_intent_unexpected_state")
        {
            // Already succeeded or canceled: nothing to cancel (section 9.10, CancelPaymentIntent).
        }
    }

    public async Task<ProviderRefund> RefundAsync(string providerPaymentId, Money amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            var refund = await CallAsync(() => new RefundService(client).CreateAsync(
                new RefundCreateOptions { PaymentIntent = providerPaymentId, Amount = ToMinorUnits(amount) },
                new RequestOptions { IdempotencyKey = idempotencyKey },
                cancellationToken));
            return new ProviderRefund(refund.Id, WasAlreadyRefunded: false);
        }
        catch (StripeException exception) when (exception.StripeError?.Code == "charge_already_refunded")
        {
            // Section 9.8: an already-refunded answer counts as success.
            return new ProviderRefund(string.Empty, WasAlreadyRefunded: true);
        }
    }

    public PaymentWebhookResult ParseWebhook(IReadOnlyDictionary<string, string> headers, byte[] rawBody)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(rawBody);

        var secret = options.Value.Stripe.WebhookSecret
            ?? throw new InvalidOperationException("Payments:Stripe:WebhookSecret is not configured.");
        if (!headers.TryGetValue(SignatureHeader, out var signature))
        {
            throw new WebhookSignatureException("The webhook has no Stripe-Signature header.");
        }

        var json = Encoding.UTF8.GetString(rawBody);
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json, signature, secret, _toleranceSeconds, timeProvider.GetUtcNow().ToUnixTimeSeconds(), throwOnApiVersionMismatch: false);
        }
        catch (StripeException exception)
        {
            throw new WebhookSignatureException("The Stripe webhook signature did not verify.", exception);
        }

        var (type, paymentId, amount) = stripeEvent.Data.Object switch
        {
            PaymentIntent intent when stripeEvent.Type == EventTypes.PaymentIntentSucceeded =>
                (PaymentEventType.Succeeded, intent.Id, FromMinorUnits(intent.AmountReceived, intent.Currency)),
            PaymentIntent intent when stripeEvent.Type == EventTypes.PaymentIntentPaymentFailed =>
                (PaymentEventType.Failed, intent.Id, FromMinorUnits(intent.Amount, intent.Currency)),
            Charge charge when stripeEvent.Type == EventTypes.ChargeRefunded =>
                (PaymentEventType.Refunded, charge.PaymentIntentId, FromMinorUnits(charge.AmountRefunded, charge.Currency)),
            Dispute dispute when stripeEvent.Type == EventTypes.ChargeDisputeCreated =>
                (PaymentEventType.Disputed, dispute.PaymentIntentId, (Money?)FromMinorUnits(dispute.Amount, dispute.Currency)),
            _ => (PaymentEventType.Other, (string?)null, (Money?)null),
        };

        return new PaymentWebhookResult(stripeEvent.Id, type, paymentId, amount, stripeEvent.Created, json);
    }

    private static long ToMinorUnits(Money money) => (long)(money.Amount * 100);

    private static Money FromMinorUnits(long amount, string currency) => new(amount / 100m, currency.ToUpperInvariant());

    private static ProviderPayment ToProviderPayment(PaymentIntent intent) => new(
        intent.Id,
        intent.ClientSecret,
        intent.Status switch
        {
            "succeeded" => ProviderPaymentStatus.Succeeded,
            "canceled" => ProviderPaymentStatus.Canceled,
            "requires_payment_method" when intent.LastPaymentError is not null => ProviderPaymentStatus.Failed,
            _ => ProviderPaymentStatus.RequiresPayment,
        },
        FromMinorUnits(intent.Amount, intent.Currency));

    // Network errors, timeouts, 5xx and 429 are transient (section 10.2); everything else is a real error.
    private static async Task<T> CallAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (StripeException exception) when ((int)exception.HttpStatusCode >= 500 || (int)exception.HttpStatusCode == 429)
        {
            throw new PaymentProviderUnavailableException("Stripe is unavailable.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new PaymentProviderUnavailableException("Stripe could not be reached.", exception);
        }
        catch (TaskCanceledException exception) when (!exception.CancellationToken.IsCancellationRequested)
        {
            throw new PaymentProviderUnavailableException("Stripe timed out.", exception);
        }
    }
}
