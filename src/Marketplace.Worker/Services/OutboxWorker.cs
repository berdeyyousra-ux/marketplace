using Marketplace.Infrastructure.Outbox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marketplace.Worker.Services;

// Section 10.3 polling: wait 2 seconds (plus jitter) after an empty poll, poll again at once after a full batch.
// On shutdown it stops claiming and gives in-flight handlers up to 30 seconds to finish.
public sealed partial class OutboxWorker(OutboxProcessor processor, TimeProvider timeProvider, ILogger<OutboxWorker> logger) : BackgroundService
{
    public static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var handlerStop = new CancellationTokenSource();
        await using var registration = stoppingToken.Register(() => handlerStop.CancelAfter(ShutdownGrace));

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
                continue;
            }

            try
            {
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                await Task.Delay(IdleDelay + jitter, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox poll failed; retrying after the idle delay")]
    private static partial void LogPollFailed(ILogger logger, Exception exception);
}
