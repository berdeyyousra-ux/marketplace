using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Marketplace.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("Database")
                ?? throw new InvalidOperationException("ConnectionStrings:Database is not configured.");

            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
        });

        return services;
    }
}
