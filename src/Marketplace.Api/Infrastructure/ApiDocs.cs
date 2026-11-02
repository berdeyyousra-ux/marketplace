using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace Marketplace.Api.Infrastructure;

// D-20 and EP-92: the built-in OpenAPI document and the Scalar UI, both behind Api:ExposeDocs.
public static class ApiDocs
{
    public const string DocsPath = "/docs";
    public const string BearerScheme = "Bearer";

    public static IServiceCollection AddMarketplaceOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Marketplace API",
                    Version = "v1",
                    Description = "Digital product marketplace. Errors are RFC 9457 problem+json with code and traceId fields.",
                };
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Access token from POST /api/v1/auth/login.",
                };
                return Task.CompletedTask;
            });

            // Endpoints that need a signed-in caller declare the Bearer requirement.
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                if (metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any())
                {
                    operation.Security =
                    [
                        new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference(BearerScheme, context.Document)] = [] },
                    ];
                }

                return Task.CompletedTask;
            });
        });
        return services;
    }

    public static void MapMarketplaceDocs(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (!app.Configuration.GetValue("Api:ExposeDocs", defaultValue: true))
        {
            return;
        }

        app.MapOpenApi();
        app.MapScalarApiReference(DocsPath, options => options.WithTitle("Marketplace API"));
    }

    public static bool IsDocsRequest(PathString path) =>
        path.StartsWithSegments(DocsPath, StringComparison.OrdinalIgnoreCase);
}
