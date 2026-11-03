using Marketplace.Application.Abstractions;
using Marketplace.Infrastructure.Outbox;
using Marketplace.Infrastructure.Persistence;
using Marketplace.IntegrationTests.Fixtures;
using Marketplace.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Marketplace.IntegrationTests.Workers;

// The Worker's polling loop around the outbox processor (section 10.3).
public sealed class OutboxWorkerTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private static readonly TimeSpan _wait = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Worker_Started_ProcessesQueuedMessage()
    {
        var key = Guid.CreateVersion7().ToString();
        await EnqueueAsync(key);
        using var worker = NewWorker();

        await worker.StartAsync(CancellationToken);
        await WaitUntilAsync(() => ProcessedCountAsync(), 1);
        await worker.StopAsync(CancellationToken);

        Factory.Services.GetRequiredService<TestOutboxHandler>().Calls[key].ShouldBe(1);
    }

    [Fact]
    public async Task Worker_Idle_PicksUpNewMessageAfterIdleDelay()
    {
        using var worker = NewWorker();
        await worker.StartAsync(CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken);
        await EnqueueAsync(Guid.CreateVersion7().ToString());

        var processedBeforeDelay = await ProcessedCountAsync();
        Time.Advance(OutboxWorker.IdleDelay + TimeSpan.FromSeconds(1));
        await WaitUntilAsync(() => ProcessedCountAsync(), 1);
        await worker.StopAsync(CancellationToken);

        processedBeforeDelay.ShouldBe(0);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 10)]
    [InlineData(50, 10)]
    public void DelayAfter_EmptyPolls_DoublesUpToTenSeconds(int emptyPolls, int seconds)
    {
        OutboxWorker.DelayAfter(emptyPolls).ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public async Task Worker_LongIdle_BacksOff_ThenResetsAfterWork()
    {
        using var worker = NewWorker();
        await worker.StartAsync(CancellationToken);

        // Four empty polls: the waits after them are 2, 4, 8 and then 10 seconds (each plus up to 0.5 s of jitter).
        await SettleAsync();
        foreach (var seconds in new[] { 2.5, 4.5, 8.5 })
        {
            Time.Advance(TimeSpan.FromSeconds(seconds));
            await SettleAsync();
        }

        await EnqueueAsync(Guid.CreateVersion7().ToString());
        Time.Advance(TimeSpan.FromSeconds(5));
        await SettleAsync();
        var processedMidWait = await ProcessedCountAsync();

        Time.Advance(TimeSpan.FromSeconds(6));
        await WaitUntilAsync(() => ProcessedCountAsync(), 1);

        // The poll claimed a message, so the next wait is back to 2 seconds.
        await SettleAsync();
        await EnqueueAsync(Guid.CreateVersion7().ToString());
        Time.Advance(TimeSpan.FromSeconds(2.5));
        await WaitUntilAsync(() => ProcessedCountAsync(), 2);
        await worker.StopAsync(CancellationToken);

        processedMidWait.ShouldBe(0);
    }

    // Lets the loop finish its poll and reach its next wait before fake time moves on.
    private static Task SettleAsync() => Task.Delay(TimeSpan.FromMilliseconds(300), CancellationToken);

    [Fact]
    public async Task Worker_Stopped_ReturnsPromptly()
    {
        using var worker = NewWorker();
        await worker.StartAsync(CancellationToken);

        var stop = worker.StopAsync(CancellationToken);

        (await Task.WhenAny(stop, Task.Delay(_wait, CancellationToken))).ShouldBe(stop);
    }

    private OutboxWorker NewWorker() =>
        new(Factory.Services.GetRequiredService<OutboxProcessor>(), Time, NullLogger<OutboxWorker>.Instance);

    private async Task EnqueueAsync(string key)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IOutbox>().Enqueue(TestOutboxHandler.Type, new TestOutboxPayload(key, "succeed"));
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync(CancellationToken);
    }

    private Task<int> ProcessedCountAsync() =>
        WithDbAsync(db => db.OutboxMessages.CountAsync(message => message.ProcessedAt != null, CancellationToken));

    // Waits on real time for the background loop; the test fails if the count is not reached in 15 seconds.
    private static async Task WaitUntilAsync(Func<Task<int>> count, int expected)
    {
        var deadline = DateTime.UtcNow + _wait;
        while (await count() < expected)
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the worker did not process the message in time");
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }
    }
}
