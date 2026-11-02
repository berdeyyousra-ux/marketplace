using Marketplace.Infrastructure;
using Marketplace.Worker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Formatting.Compact;

// The settings files are copied next to the binary, so the content root is the output folder
// whether the Worker starts from dotnet run, a container or the binary itself.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddInfrastructure();
builder.Services.AddHostedService<OutboxWorker>();

// In-flight handlers get up to 30 seconds after shutdown starts (section 10.3), plus a margin.
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = OutboxWorker.ShutdownGrace + TimeSpan.FromSeconds(5));

await builder.Build().RunAsync();
