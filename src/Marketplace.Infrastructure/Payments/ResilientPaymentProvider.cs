using Marketplace.Application.Payments;
using Marketplace.Domain.Common;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Marketplace.Infrastructure.Payments;

// Section 10.2, bound from Resilience:PaymentsApi and Resilience:PaymentsWorker. Defaults are the spec's values.
public sealed class ResilienceSettings
{
    public const string SectionName = "Resilience";
    public const string PaymentsApi = "PaymentsApi";
    public const string PaymentsWorker = "PaymentsWorker";

    public TimeSpan AttemptTimeout { get; set; }

    public int MaxRetries { get; set; }

    public TimeSpan TotalTimeout { get; set; }

    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    public TimeSpan MaxRetryAfter { get; set; } = TimeSpan.FromSeconds(2);

    public double FailureRatio { get; set; } = 0.5;

    public int MinimumThroughput { get; set; } = 10;

    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    public static ResilienceSettings ApiDefaults() =>
        new() { AttemptTimeout = TimeSpan.FromSeconds(5), MaxRetries = 2, TotalTimeout = TimeSpan.FromSeconds(12) };

    public static ResilienceSettings WorkerDefaults() =>
        new() { AttemptTimeout = TimeSpan.FromSeconds(10), MaxRetries = 3, TotalTimeout = TimeSpan.FromSeconds(40) };
}

// Wraps whichever provider is configured, so the fake provider exercises the same policies as Stripe (FM-09,
// FM-10). Create and get use the Api profile; cancel and refund, which run in the Worker, use the Worker profile.
// Every call is safe to repeat: create and refund carry idempotency keys, get and cancel are idempotent.
// Webhook parsing is local and is not wrapped. The pipelines run on the system clock: retry delays and
// timeouts are real time, even when tests replace the application's TimeProvider.
public sealed class ResilientPaymentProvider : IPaymentProvider
{
    private readonly IPaymentProvider _inner;
    private readonly ResiliencePipeline _api;
    private readonly ResiliencePipeline _worker;

    public ResilientPaymentProvider(IPaymentProvider inner, ResilienceSettings api, ResilienceSettings worker, TimeProvider? pipelineTime = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
        _api = Build(api, pipelineTime ?? TimeProvider.System);
        _worker = Build(worker, pipelineTime ?? TimeProvider.System);
    }

    public string Name => _inner.Name;

    public IPaymentProvider Inner => _inner;

    public Task<ProviderPayment> CreatePaymentAsync(CreateProviderPayment request, CancellationToken cancellationToken) =>
        RunAsync(_api, token => _inner.CreatePaymentAsync(request, token), cancellationToken);

    public Task<ProviderPayment> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken) =>
        RunAsync(_api, token => _inner.GetPaymentAsync(providerPaymentId, token), cancellationToken);

    public Task CancelPaymentAsync(string providerPaymentId, CancellationToken cancellationToken) =>
        RunAsync(_worker, async token =>
        {
            await _inner.CancelPaymentAsync(providerPaymentId, token);
            return true;
        }, cancellationToken);

    public Task<ProviderRefund> RefundAsync(string providerPaymentId, Money amount, string idempotencyKey, CancellationToken cancellationToken) =>
        RunAsync(_worker, token => _inner.RefundAsync(providerPaymentId, amount, idempotencyKey, token), cancellationToken);

    public PaymentWebhookResult ParseWebhook(IReadOnlyDictionary<string, string> headers, byte[] rawBody) => _inner.ParseWebhook(headers, rawBody);

    public PaymentWebhookResult ReadStoredEvent(string rawPayload) => _inner.ReadStoredEvent(rawPayload);

    // Order, outermost first: total timeout, retry, circuit breaker, per-attempt timeout.
    public static ResiliencePipeline Build(ResilienceSettings settings, TimeProvider pipelineTime)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var transient = new PredicateBuilder()
            .Handle<PaymentProviderUnavailableException>()
            .Handle<TimeoutRejectedException>();

        var builder = new ResiliencePipelineBuilder { TimeProvider = pipelineTime }.AddTimeout(settings.TotalTimeout);
        if (settings.MaxRetries > 0)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = transient,
                MaxRetryAttempts = settings.MaxRetries,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = settings.BaseDelay,

                // A 429 asks for a wait; honor it up to MaxRetryAfter.
                DelayGenerator = arguments => ValueTask.FromResult(
                    arguments.Outcome.Exception is PaymentProviderUnavailableException { RetryAfter: { } retryAfter }
                        ? (TimeSpan?)(retryAfter < settings.MaxRetryAfter ? retryAfter : settings.MaxRetryAfter)
                        : null),
            });
        }

        return builder
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = transient,
                FailureRatio = settings.FailureRatio,
                MinimumThroughput = settings.MinimumThroughput,
                SamplingDuration = settings.SamplingDuration,
                BreakDuration = settings.BreakDuration,
            })
            .AddTimeout(settings.AttemptTimeout)
            .Build();
    }

    private static async Task<T> RunAsync<T>(ResiliencePipeline pipeline, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await pipeline.ExecuteAsync(async token => await call(token), cancellationToken);
        }
        catch (TimeoutRejectedException exception)
        {
            throw new PaymentProviderUnavailableException("The payment provider timed out.", exception);
        }
        catch (BrokenCircuitException exception)
        {
            throw new PaymentProviderUnavailableException("The payment provider circuit is open.", exception);
        }
    }
}
