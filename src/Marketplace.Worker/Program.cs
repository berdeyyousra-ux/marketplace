using Marketplace.Application;
using Marketplace.Infrastructure;
using Marketplace.Infrastructure.Configuration;
using Marketplace.Infrastructure.Jobs;
using Marketplace.Infrastructure.Logging;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Worker.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Formatting.Compact;

namespace Marketplace.Worker;

// A named entry point rather than top-level statements: with ASP.NET Core referenced, a top-level Program would be
// public and clash with the Api's Program in the integration tests.
public static class WorkerProgram
{
    private const string _readinessTag = "ready";

    public static async Task Main(string[] args)
    {
        // The settings files are copied next to the binary, so the content root is the output folder
        // whether the Worker starts from dotnet run, a container or the binary itself.
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });

        // Section 15.7: the Worker serves nothing but health, on its own port (8081 by default).
        var healthPort = builder.Configuration.GetValue("Worker:HealthPort", 8081);
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenAnyIP(healthPort));

        builder.Services.AddSerilog((services, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.With<SensitivePropertyMasker>()
            .WriteTo.Console(new CompactJsonFormatter()));

        builder.Services.AddApplicationBackground();
        builder.Services.AddInfrastructure();
        builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>(tags: [_readinessTag]);
        builder.Services.AddHostedService<OutboxWorker>();
        builder.Services.AddHostedService<ScheduledJobWorker<OrderExpiryJob>>();
        builder.Services.AddHostedService<ScheduledJobWorker<PaymentReconciliationJob>>();
        builder.Services.AddHostedService<ScheduledJobWorker<CleanupJob>>();
        builder.Services.AddHostedService<ScheduledJobWorker<SellerRevenueViewRefreshJob>>();

        // In-flight handlers get up to 30 seconds after shutdown starts (section 10.3), plus a margin.
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = OutboxWorker.ShutdownGrace + TimeSpan.FromSeconds(5));

        var app = builder.Build();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(_readinessTag) });
        app.Services.ValidateConfigurationOrLogFatal(app.Logger);
        await app.RunAsync();
    }
}
