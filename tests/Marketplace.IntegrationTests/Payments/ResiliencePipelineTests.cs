using Marketplace.Application.Payments;
using Marketplace.Domain.Common;
using Marketplace.Infrastructure.Payments;
using Polly.CircuitBreaker;
using Shouldly;

namespace Marketplace.IntegrationTests.Payments;

// Section 10.2 policies on ResilientPaymentProvider, with a stub provider and no host.
public sealed class ResiliencePipelineTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CircuitBreaker_OpensAfterHalfTheCallsFail_AndStopsCallingTheProvider()
    {
        var stub = new FailingProvider(failures: int.MaxValue);
        var settings = Settings(maxRetries: 0, minimumThroughput: 4);
        var provider = new ResilientPaymentProvider(stub, settings, settings);

        for (var i = 0; i < 4; i++)
        {
            await Should.ThrowAsync<PaymentProviderUnavailableException>(() => provider.GetPaymentAsync("pi_1", CancellationToken));
        }

        var open = await Should.ThrowAsync<PaymentProviderUnavailableException>(() => provider.GetPaymentAsync("pi_1", CancellationToken));
        open.InnerException.ShouldBeAssignableTo<BrokenCircuitException>();
        stub.Calls.ShouldBe(4);
    }

    [Fact]
    public async Task WorkerProfile_RetriesThreeTimes_ApiProfileTwice()
    {
        var stub = new FailingProvider(failures: 3);
        var provider = new ResilientPaymentProvider(stub, Settings(maxRetries: 2), Settings(maxRetries: 3));

        await Should.ThrowAsync<PaymentProviderUnavailableException>(() => provider.GetPaymentAsync("pi_1", CancellationToken));
        stub.Calls.ShouldBe(3);

        stub.Reset(failures: 3);
        await provider.CancelPaymentAsync("pi_1", CancellationToken);
        stub.Calls.ShouldBe(4);
    }

    [Fact]
    public async Task RetryAfter_IsHonoredUpToTheCap()
    {
        var stub = new FailingProvider(failures: 1, retryAfter: TimeSpan.FromMinutes(1));
        var settings = Settings(maxRetries: 2);
        settings.MaxRetryAfter = TimeSpan.FromMilliseconds(50);
        settings.TotalTimeout = TimeSpan.FromSeconds(2);
        var provider = new ResilientPaymentProvider(stub, settings, settings);

        (await provider.GetPaymentAsync("pi_1", CancellationToken)).Status.ShouldBe(ProviderPaymentStatus.RequiresPayment);
        stub.Calls.ShouldBe(2);
    }

    private static ResilienceSettings Settings(int maxRetries, int minimumThroughput = 100) => new()
    {
        AttemptTimeout = TimeSpan.FromSeconds(1),
        TotalTimeout = TimeSpan.FromSeconds(5),
        MaxRetries = maxRetries,
        BaseDelay = TimeSpan.FromMilliseconds(1),
        MinimumThroughput = minimumThroughput,
    };

    private sealed class FailingProvider(int failures, TimeSpan? retryAfter = null) : IPaymentProvider
    {
        private int _failures = failures;

        public int Calls { get; private set; }

        public string Name => "stub";

        public void Reset(int failures)
        {
            _failures = failures;
            Calls = 0;
        }

        public Task<ProviderPayment> CreatePaymentAsync(CreateProviderPayment request, CancellationToken cancellationToken) => Call();

        public Task<ProviderPayment> GetPaymentAsync(string providerPaymentId, CancellationToken cancellationToken) => Call();

        public Task CancelPaymentAsync(string providerPaymentId, CancellationToken cancellationToken) => Call();

        public Task<ProviderRefund> RefundAsync(string providerPaymentId, Money amount, string idempotencyKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public PaymentWebhookResult ParseWebhook(IReadOnlyDictionary<string, string> headers, byte[] rawBody) => throw new NotSupportedException();

        public PaymentWebhookResult ReadStoredEvent(string rawPayload) => throw new NotSupportedException();

        private Task<ProviderPayment> Call()
        {
            Calls++;
            if (_failures-- > 0)
            {
                throw new PaymentProviderUnavailableException("Simulated.") { RetryAfter = retryAfter };
            }

            return Task.FromResult(new ProviderPayment("pi_1", null, ProviderPaymentStatus.RequiresPayment, Money.Eur(1m)));
        }
    }
}
