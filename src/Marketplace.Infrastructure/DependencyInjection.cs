using Marketplace.Application.Abstractions;
using Marketplace.Application.Identity;
using Marketplace.Infrastructure.Email;
using Marketplace.Infrastructure.Identity;
using Marketplace.Infrastructure.Outbox;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // R-07: time comes only from an injected TimeProvider; tests replace it with FakeTimeProvider.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddSingleton<IDbExceptionTranslator, DbExceptionTranslator>();
        services.AddSingleton<ICorrelationContext, AmbientCorrelationContext>();
        services.AddScoped<IOutbox, Outbox.Outbox>();
        services.AddSingleton<OutboxProcessor>();
        services.AddMarketplaceIdentity();
        services.AddOptions<EmailOptions>().BindConfiguration(EmailOptions.SectionName);
        services.AddOptions<AccountLinkOptions>().BindConfiguration(EmailOptions.SectionName);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        return services;
    }
}
