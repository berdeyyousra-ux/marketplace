using System.Globalization;
using System.Threading.RateLimiting;
using Marketplace.Api.Errors;
using Marketplace.Application.Common;
using Marketplace.Domain.Common;
using Microsoft.AspNetCore.RateLimiting;

namespace Marketplace.Api.Infrastructure;

// Section 11.4. The limiters keep their own internal clock; TimeProvider cannot be injected into them.
public static class RateLimiting
{
    public const string Auth = "auth";
    public const string Refresh = "refresh";
    public const string Webhook = "webhook";
    public const string Download = "download";

    public static readonly Error RateLimited =
        new("rate_limited", "Too many requests. Wait and try again.", ErrorType.TooManyRequests);

    private static readonly TimeSpan _window = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddMarketplaceRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        // RateLimiting:Enabled exists so the shared integration-test host, where every request comes from one
        // address, can switch limiting off. The rate limit tests switch it back on in their own host.
        var enabled = configuration.GetValue("RateLimiting:Enabled", defaultValue: true);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = WriteRejectionAsync;

            options.AddPolicy(Auth, context => PerIp(context, enabled, 10));
            options.AddPolicy(Refresh, context => PerIp(context, enabled, 30));
            options.AddPolicy(Webhook, context => PerIp(context, enabled, 600));
            options.AddPolicy(Download, context => PerUser(context, enabled, 30));

            // "Everything else": a token bucket of 120 per minute per user, or per IP when anonymous.
            // Endpoints with a named policy or with rate limiting disabled are not also counted here.
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var metadata = context.GetEndpoint()?.Metadata;
                if (!enabled || metadata?.GetMetadata<EnableRateLimitingAttribute>() is not null || metadata?.GetMetadata<DisableRateLimitingAttribute>() is not null)
                {
                    return RateLimitPartition.GetNoLimiter("exempt");
                }

                return RateLimitPartition.GetTokenBucketLimiter(UserOrIpKey(context), _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = 120,
                    TokensPerPeriod = 120,
                    ReplenishmentPeriod = _window,
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });
        });

        return services;
    }

    private static RateLimitPartition<string> PerIp(HttpContext context, bool enabled, int permitsPerMinute) =>
        enabled ? FixedWindow("ip:" + IpKey(context), permitsPerMinute) : RateLimitPartition.GetNoLimiter("disabled");

    private static RateLimitPartition<string> PerUser(HttpContext context, bool enabled, int permitsPerMinute) =>
        enabled ? FixedWindow(UserOrIpKey(context), permitsPerMinute) : RateLimitPartition.GetNoLimiter("disabled");

    private static RateLimitPartition<string> FixedWindow(string key, int permitsPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitsPerMinute,
            Window = _window,
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    // The client IP comes from forwarded headers, which trust only the known proxy networks (section 11.4).
    private static string IpKey(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string UserOrIpKey(HttpContext context) =>
        AuthClaims.ReadUserId(context.User) is { } userId ? "user:" + userId : "ip:" + IpKey(context);

    private static async ValueTask WriteRejectionAsync(OnRejectedContext rejected, CancellationToken cancellationToken)
    {
        var context = rejected.HttpContext;
        var retryAfter = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : _window;
        context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        var problem = ApiProblems.Create(context, RateLimited);
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
    }
}
