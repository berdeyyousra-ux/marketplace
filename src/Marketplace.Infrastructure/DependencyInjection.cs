using Marketplace.Application.Abstractions;
using Marketplace.Application.Catalog;
using Marketplace.Application.Identity;
using Marketplace.Application.Payments;
using Marketplace.Infrastructure.Catalog;
using Marketplace.Infrastructure.Email;
using Marketplace.Infrastructure.Identity;
using Marketplace.Infrastructure.Outbox;
using Marketplace.Infrastructure.Payments;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Infrastructure.Storage;
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
        services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.SectionName);
        services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
        services.AddSingleton<IFileScanner, NoopFileScanner>();
        services.AddScoped<IProductSearch, ProductSearch>();
        services.AddScoped<IIdempotencyStore, Idempotency.IdempotencyStore>();
        services.AddOptions<Application.Orders.MarketplaceOptions>().BindConfiguration(Application.Orders.MarketplaceOptions.SectionName);

        // D-25: the provider comes from Payments:Provider. Fake is the default for development and tests.
        services.AddOptions<PaymentOptions>().BindConfiguration(PaymentOptions.SectionName);
        services.AddSingleton<FakePaymentProvider>();
        services.AddHttpClient(PaymentOptions.StripeName);
        services.AddSingleton<Stripe.IStripeClient>(provider =>
        {
            var key = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaymentOptions>>().Value.Stripe.SecretKey
                ?? throw new InvalidOperationException("Payments:Stripe:SecretKey is not configured.");
            var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(PaymentOptions.StripeName);
            // Retries belong to ResilientPaymentProvider (section 10.2), so the SDK's own are off.
            return new Stripe.StripeClient(key, httpClient: new Stripe.SystemNetHttpClient(http, maxNetworkRetries: 0));
        });
        services.AddSingleton<StripePaymentProvider>();
        services.AddScoped<IPaymentEventInbox, PaymentEventInbox>();
        services.AddScoped<IRowLocks, RowLocks>();
        services.AddScoped<Application.Reporting.IReports, Reporting.SqlReports>();
        services.AddScoped<Jobs.OrderExpiryJob>();
        services.AddScoped<Jobs.PaymentReconciliationJob>();
        services.AddScoped<Jobs.CleanupJob>();
        services.AddScoped<Jobs.SellerRevenueViewRefreshJob>();
        AddResilienceSettings(services, ResilienceSettings.PaymentsApi, ResilienceSettings.ApiDefaults());
        AddResilienceSettings(services, ResilienceSettings.PaymentsWorker, ResilienceSettings.WorkerDefaults());
        AddResilienceSettings(services, ResilienceSettings.Llm, ResilienceSettings.LlmDefaults());

        // Section 12.3: the LLM client comes from Ai:Provider. Fake is the default for development and tests.
        services.AddOptions<Ai.AiOptions>()
            .BindConfiguration(Ai.AiOptions.SectionName)
            .Validate(ai => ai.Provider is Ai.AiOptions.FakeName or Ai.AiOptions.AnthropicName, "Ai:Provider must be Fake or Anthropic.")
            .Validate(
                ai => ai.Provider != Ai.AiOptions.AnthropicName || (!string.IsNullOrWhiteSpace(ai.Model) && !string.IsNullOrWhiteSpace(ai.ApiKey)),
                "Ai:Model and Ai:ApiKey are required when Ai:Provider is Anthropic.")
            .Validate(ai => ai.MaxOutputTokens > 0 && ai.DailyRequestCap >= 0, "Ai:MaxOutputTokens must be positive and Ai:DailyRequestCap not negative.")
            .ValidateOnStart();
        services.AddSingleton<Ai.FakeLlmClient>();
        services.AddScoped<Application.Refunds.IRefundAdvisor, Ai.LlmRefundAdvisor>();

        // The resilience pipeline owns timeouts, so the HttpClient's own timeout is off.
        services.AddHttpClient(Ai.AiOptions.AnthropicName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton(provider => new Ai.AnthropicLlmClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(Ai.AiOptions.AnthropicName),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Ai.AiOptions>>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ResilienceSettings>>()));
        services.AddSingleton<Application.Refunds.ILlmClient>(provider =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Ai.AiOptions>>().Value.Provider == Ai.AiOptions.AnthropicName
                ? provider.GetRequiredService<Ai.AnthropicLlmClient>()
                : provider.GetRequiredService<Ai.FakeLlmClient>());
        services.AddSingleton<IPaymentProvider>(provider =>
        {
            IPaymentProvider selected = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaymentOptions>>().Value.Provider switch
            {
                PaymentOptions.StripeName => provider.GetRequiredService<StripePaymentProvider>(),
                PaymentOptions.FakeName => provider.GetRequiredService<FakePaymentProvider>(),
                var other => throw new InvalidOperationException($"Unknown Payments:Provider '{other}'."),
            };
            var settings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ResilienceSettings>>();
            return new ResilientPaymentProvider(selected, settings.Get(ResilienceSettings.PaymentsApi), settings.Get(ResilienceSettings.PaymentsWorker));
        });

        return services;
    }

    private static void AddResilienceSettings(IServiceCollection services, string name, ResilienceSettings defaults) =>
        services.AddOptions<ResilienceSettings>(name)
            .Configure(settings =>
            {
                settings.AttemptTimeout = defaults.AttemptTimeout;
                settings.MaxRetries = defaults.MaxRetries;
                settings.TotalTimeout = defaults.TotalTimeout;
            })
            .BindConfiguration($"{ResilienceSettings.SectionName}:{name}");
}
