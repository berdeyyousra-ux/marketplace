using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;

namespace Marketplace.Infrastructure.Payments;

// Section 10.2, shared by the payment provider and the LLM client. Order, outermost first: total timeout, retry,
// circuit breaker, per-attempt timeout. isTransient says which exceptions are retried and counted by the breaker;
// a timed-out attempt always is.
public static class ResiliencePipelines
{
    public static ResiliencePipeline Build(
        ResilienceSettings settings, TimeProvider pipelineTime, Func<Exception, bool> isTransient, Func<Exception, TimeSpan?> retryAfter)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var transient = new PredicateBuilder().Handle<Exception>(exception => exception is TimeoutRejectedException || isTransient(exception));
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
                    arguments.Outcome.Exception is { } exception && retryAfter(exception) is { } wait
                        ? (TimeSpan?)(wait < settings.MaxRetryAfter ? wait : settings.MaxRetryAfter)
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
}
