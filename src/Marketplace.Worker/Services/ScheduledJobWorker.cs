using Marketplace.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marketplace.Worker.Services;

// Section 9.9: runs one job on its interval, each run in its own scope. A failed run is logged and the next one
// starts on schedule. With several Worker replicas the jobs stay safe: expiry uses SKIP LOCKED, reconciliation
// goes through row locks and concurrency tokens, and cleanup deletes are idempotent.
public sealed partial class ScheduledJobWorker<TJob>(IServiceScopeFactory scopes, TimeProvider timeProvider, ILogger<ScheduledJobWorker<TJob>> logger)
    : BackgroundService
    where TJob : IScheduledJob
{
    // Runs the job once. Exposed for tests.
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<TJob>();
        var changed = await job.RunAsync(cancellationToken);
        if (changed > 0)
        {
            LogJobRan(logger, job.Name, changed);
        }

        return changed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval;
        await using (var scope = scopes.CreateAsyncScope())
        {
            interval = scope.ServiceProvider.GetRequiredService<TJob>().Interval;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogJobFailed(logger, typeof(TJob).Name, exception);
            }

            try
            {
                await Task.Delay(interval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {Job} changed {Changed} rows")]
    private static partial void LogJobRan(ILogger logger, string job, int changed);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {Job} failed; it runs again after its interval")]
    private static partial void LogJobFailed(ILogger logger, string job, Exception exception);
}
