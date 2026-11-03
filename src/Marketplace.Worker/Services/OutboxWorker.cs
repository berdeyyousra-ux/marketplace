using Marketplace.Infrastructure.Outbox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marketplace.Worker.Services;

// Section 10.3 polling: poll again at once after a full batch, and wait 2 seconds (plus jitter) after a partial one.
// Consecutive empty polls back off, 2, 4, 8, then 10 seconds at most, so an idle Worker makes so little database
// traffic that Container Apps bills it at the idle rate (changelog). Any claimed message resets the wait to 2
// seconds. A failed poll counts as empty, so a stopped database is retried every 10 seconds rather than every 2.
// On shutdown it stops claiming and gives in-flight handlers up to 30 seconds to finish.
public sealed partial class OutboxWorker(OutboxProcessor processor, TimeProvider timeProvider, ILogger<OutboxWorker> logger) : BackgroundService
{
    public static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaxIdleDelay = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var handlerStop = new CancellationTokenSource();
        await using var registration = stoppingToken.Register(() => handlerStop.CancelAfter(ShutdownGrace));

        var emptyPolls = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            int claimed;
            try
            {
                claimed = await processor.ProcessBatchAsync(handlerStop.Token);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogPollFailed(logger, exception);
                claimed = 0;
            }

            if (claimed >= OutboxProcessor.BatchSize)
            {
                emptyPolls = 0;
                continue;
            }

            emptyPolls = claimed == 0 ? emptyPolls + 1 : 0;
            try
            {
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                await Task.Delay(DelayAfter(emptyPolls) + jitter, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // The wait before the next poll, after this many consecutive empty polls (0 when the last poll claimed some).
    public static TimeSpan DelayAfter(int emptyPolls)
    {
        var doublings = Math.Clamp(emptyPolls - 1, 0, 4);
        var delay = IdleDelay * (1 << doublings);
        return delay < MaxIdleDelay ? delay : MaxIdleDelay;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox poll failed; retrying after the idle delay")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);
}
