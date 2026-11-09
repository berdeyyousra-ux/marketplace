using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;

namespace Marketplace.Api.Infrastructure;

// Section 16 "Cors" configuration.
public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];
}

public static class CorsSetup
{
    public const string PolicyName = "Default";

    // Section 16: outside Development the API needs at least one allowed origin, and never the wildcard.
    public static IServiceCollection AddMarketplaceCors(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddOptions<CorsSettings>()
            .BindConfiguration(CorsSettings.SectionName)
            .Validate(
                settings => environment.IsDevelopment() || (settings.AllowedOrigins.Length > 0 && !settings.AllowedOrigins.Contains("*")),
                "Cors:AllowedOrigins must list at least one origin and must not contain * outside Development.")
            .ValidateOnStart();

        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<CorsSettings>>((cors, settings) => cors.AddPolicy(PolicyName, policy => policy
            .WithOrigins(settings.Value.AllowedOrigins)
            .AllowAnyMethod()
            .WithHeaders("Authorization", "Content-Type", "Idempotency-Key", "If-Match", "X-Correlation-Id")
            .WithExposedHeaders("ETag", "Retry-After", "Location", "X-Correlation-Id", "Idempotent-Replayed")));
        return services;
    }
}
