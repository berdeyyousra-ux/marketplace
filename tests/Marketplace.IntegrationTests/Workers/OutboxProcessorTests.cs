using Marketplace.Application.Abstractions;
using Marketplace.Application.Ops;
using Marketplace.Infrastructure.Outbox;
using Marketplace.Infrastructure.Persistence;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Workers;

// T-030 and section 10.3, driven directly rather than by the worker loop (section 14.2).
public sealed class OutboxProcessorTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private OutboxProcessor Processor => Factory.Services.GetRequiredService<OutboxProcessor>();

    private TestOutboxHandler Handler => Factory.Services.GetRequiredService<TestOutboxHandler>();

    [Fact]
    public async Task Enqueue_InUnitOfWork_WritesRowWithCorrelationAndCurrentTime()
    {
        var key = NewKey();

        await EnqueueAsync(key, "succeed", correlationId: "corr-t030");

        var row = await SingleRowAsync();
        row.Type.ShouldBe(TestOutboxHandler.Type);
        row.Payload.ShouldContain(key);
        row.CorrelationId.ShouldBe("corr-t030");
        row.AvailableAt.ShouldBe(Time.GetUtcNow());
        row.Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task Enqueue_WithoutSaveChanges_WritesNothing()
    {
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IOutbox>().Enqueue(TestOutboxHandler.Type, new TestOutboxPayload(NewKey(), "succeed"));
        }

        (await WithDbAsync(db => db.OutboxMessages.CountAsync(CancellationToken))).ShouldBe(0);
    }

    [Fact]
    public async Task ProcessBatch_HandlerSucceeds_MarksProcessed()
    {
        var key = NewKey();
        await EnqueueAsync(key, "succeed");

        var claimed = await Processor.ProcessBatchAsync(CancellationToken);

        claimed.ShouldBe(1);
        var row = await SingleRowAsync();
        row.ProcessedAt.ShouldBe(Time.GetUtcNow());
        row.Attempts.ShouldBe(1);
        Handler.Calls[key].ShouldBe(1);
    }

    [Fact]
    public async Task ProcessBatch_HandlerFails_SchedulesRetryWithBackoff()
    {
        var key = NewKey();
        await EnqueueAsync(key, "fail");
        var start = Time.GetUtcNow();

        await Processor.ProcessBatchAsync(CancellationToken);
        var afterFirst = await SingleRowAsync();
        var claimedTooEarly = await Processor.ProcessBatchAsync(CancellationToken);
        Time.Advance(TimeSpan.FromSeconds(13));
        await Processor.ProcessBatchAsync(CancellationToken);

        afterFirst.ProcessedAt.ShouldBeNull();
        afterFirst.AvailableAt.ShouldBeInRange(start.AddSeconds(8), start.AddSeconds(12));
        afterFirst.LastError!.ShouldContain("Simulated failure");
        claimedTooEarly.ShouldBe(0);
        (await SingleRowAsync()).Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task ProcessBatch_EighthFailure_DeadLettersAndCallsHook()
    {
        var key = NewKey();
        await EnqueueAsync(key, "fail");

        for (var attempt = 1; attempt <= 8; attempt++)
        {
            (await Processor.ProcessBatchAsync(CancellationToken)).ShouldBe(1);
            Time.Advance(TimeSpan.FromHours(2));
        }

        var row = await SingleRowAsync();
        row.Attempts.ShouldBe(8);
        row.DeadLetteredAt.ShouldNotBeNull();
        Handler.DeadLettered.ShouldContainKey(key);
        (await Processor.ProcessBatchAsync(CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task ProcessBatch_NonRetryableException_DeadLettersImmediately()
    {
        var key = NewKey();
        await EnqueueAsync(key, "nonretryable");

        await Processor.ProcessBatchAsync(CancellationToken);

        var row = await SingleRowAsync();
        row.Attempts.ShouldBe(1);
        row.DeadLetteredAt.ShouldNotBeNull();
        Handler.DeadLettered[key].ShouldContain("Simulated permanent failure");
    }

    [Fact]
    public async Task ProcessBatch_UnknownMessageType_DeadLettersImmediately()
    {
        await WithDbAsync(async db =>
        {
            var now = Time.GetUtcNow();
            db.OutboxMessages.Add(new OutboxMessage { Type = "Test.Unknown", Payload = "{}", CreatedAt = now, AvailableAt = now });
            return await db.SaveChangesAsync(CancellationToken);
        });

        await Processor.ProcessBatchAsync(CancellationToken);

        var row = await SingleRowAsync();
        row.DeadLetteredAt.ShouldNotBeNull();
        row.LastError!.ShouldContain("No handler");
    }

    [Fact]
    public async Task FM11_WorkerCrashesAfterClaim_RetriedAfterLeaseAndAppliedOnce()
    {
        var key = NewKey();
        await EnqueueAsync(key, "succeed");

        (await Processor.ClaimAsync(CancellationToken)).Count.ShouldBe(1);
        var duringLease = await Processor.ProcessBatchAsync(CancellationToken);
        Time.Advance(OutboxProcessor.Lease);
        var afterLease = await Processor.ProcessBatchAsync(CancellationToken);

        duringLease.ShouldBe(0);
        afterLease.ShouldBe(1);
        Handler.Calls[key].ShouldBe(1);
        (await SingleRowAsync()).ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task ProcessBatch_LongError_IsTruncatedAndExcludesPayload()
    {
        var key = NewKey();
        await EnqueueAsync(key, "longerror");

        await Processor.ProcessBatchAsync(CancellationToken);

        var row = await SingleRowAsync();
        row.LastError!.Length.ShouldBe(OutboxProcessor.LastErrorMaxLength);
        row.LastError.ShouldNotContain(key);
    }

    [Fact]
    public async Task ProcessBatch_TwoProcessorsConcurrently_ProcessEachMessageOnce()
    {
        var keys = Enumerable.Range(0, 30).Select(_ => NewKey()).ToList();
        foreach (var key in keys)
        {
            await EnqueueAsync(key, "succeed");
        }

        int claimed;
        do
        {
            var counts = await Task.WhenAll(Processor.ProcessBatchAsync(CancellationToken), Processor.ProcessBatchAsync(CancellationToken));
            claimed = counts.Sum();
        }
        while (claimed > 0);

        keys.ShouldAllBe(key => Handler.Calls[key] == 1);
        (await WithDbAsync(db => db.OutboxMessages.CountAsync(message => message.ProcessedAt != null, CancellationToken))).ShouldBe(30);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(4, 80)]
    [InlineData(9, 2560)]
    [InlineData(20, 3600)]
    public void Backoff_WithoutJitter_DoublesFromTenSecondsUpToOneHour(int attempts, int expectedSeconds)
    {
        OutboxProcessor.Backoff(attempts, 1.0).ShouldBe(TimeSpan.FromSeconds(expectedSeconds));
    }

    private static string NewKey() => Guid.CreateVersion7().ToString();

    private async Task EnqueueAsync(string key, string mode, string? correlationId = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        using var correlation = AmbientCorrelationContext.Begin(correlationId);
        scope.ServiceProvider.GetRequiredService<IOutbox>().Enqueue(TestOutboxHandler.Type, new TestOutboxPayload(key, mode));
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(CancellationToken);
    }

    private Task<OutboxMessage> SingleRowAsync() =>
        WithDbAsync(db => db.OutboxMessages.AsNoTracking().SingleAsync(CancellationToken));
}
