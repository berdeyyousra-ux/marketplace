using System.Security.Cryptography;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Marketplace.Api.Auth;

public static class AuthenticationSetup
{
    public const string RequireSeller = nameof(RequireSeller);
    public const string RequireAdmin = nameof(RequireAdmin);

    public static IServiceCollection AddMarketplaceAuthentication(this IServiceCollection services, IHostEnvironment environment)
    {
        var jwt = services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => JwtOptions.IsValidKey(options.SigningKey), "Jwt:SigningKey must be base64 and at least 32 bytes.")
            .Validate(options => options.PreviousSigningKeys.All(JwtOptions.IsValidKey), "Every Jwt:PreviousSigningKeys entry must be base64 and at least 32 bytes.")
            .ValidateOnStart();

        // No signing key is committed for local development: without one, Development uses a random key per process.
        // Docker Compose supplies a fixed development key, and staging and production must configure their own (section 11.3).
        if (environment.IsDevelopment())
        {
            jwt.PostConfigure(options =>
            {
                if (string.IsNullOrWhiteSpace(options.SigningKey))
                {
                    options.SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(JwtOptions.MinimumKeyBytes));
                }
            });
        }

        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((bearer, options, timeProvider) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = JwtTokens.CreateValidationParameters(options.Value, timeProvider);
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(RequireSeller, policy => policy.RequireAuthenticatedUser().RequireRole(Roles.Seller))
            .AddPolicy(RequireAdmin, policy => policy.RequireAuthenticatedUser().RequireRole(Roles.Admin));

        return services;
    }
}
