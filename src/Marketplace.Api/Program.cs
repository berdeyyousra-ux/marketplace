using Marketplace.Api.Auth;
using Marketplace.Api.Errors;
using Marketplace.Api.Infrastructure;
using Marketplace.Api.Logging;
using Marketplace.Api.Middleware;
using Marketplace.Application;
using Marketplace.Infrastructure;
using Marketplace.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

const string CorsPolicyName = "Default";
const string ReadinessTag = "ready";

var builder = WebApplication.CreateBuilder(args);

// Section 9.2: 1 MB by default; the upload endpoint raises its own limit.
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 1024 * 1024);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.With<SensitivePropertyMasker>()
    .WriteTo.Console(new CompactJsonFormatter()));

builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddMarketplaceControllers();
builder.Services.AddMarketplaceRateLimiting(builder.Configuration);
builder.Services.AddMarketplaceOpenApi();
builder.Services.AddMarketplaceAuthentication(builder.Environment);

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    // Framework-generated problems (404, 405, 401 and so on) get the same code and traceId fields.
    if (!context.ProblemDetails.Extensions.ContainsKey("code"))
    {
        ApiProblems.Enrich(context.HttpContext, context.ProblemDetails, ApiProblems.CodeForStatus(context.ProblemDetails.Status ?? 500));
    }
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(tags: [ReadinessTag]);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    var knownNetworks = builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
    foreach (var network in knownNetworks)
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    }
});

builder.Services.AddCors(options => options.AddPolicy(CorsPolicyName, policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .AllowAnyMethod()
    .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "If-Match", "X-Correlation-Id")
    .WithExposedHeaders("ETag", "Retry-After", "Location", "X-Correlation-Id", "Idempotent-Replayed")));

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RoutePattern} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.GetMessageTemplateProperties = (context, _, elapsedMilliseconds, statusCode) =>
    [
        new LogEventProperty("RequestMethod", new ScalarValue(context.Request.Method)),
        new LogEventProperty("RoutePattern", new ScalarValue(GetRoutePattern(context))),
        new LogEventProperty("StatusCode", new ScalarValue(statusCode)),
        new LogEventProperty("Elapsed", new ScalarValue(elapsedMilliseconds)),
    ];
});
app.UseMiddleware<SecurityHeadersMiddleware>();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors(CorsPolicyName);
app.UseAuthentication();

// Section 4.5 places the rate limiter before authentication, but section 11.4's per-user limits need the
// authenticated user, so it runs straight after authentication (docs/spec-changelog.md).
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllers();
app.MapMarketplaceDocs();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).DisableRateLimiting();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains(ReadinessTag) }).DisableRateLimiting();

await app.RunAsync();

static string GetRoutePattern(HttpContext context) =>
    (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";

public partial class Program;
