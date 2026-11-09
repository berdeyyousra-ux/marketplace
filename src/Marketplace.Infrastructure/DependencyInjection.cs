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
using Microsoft.Extensions.Hosting;

namespace Marketplace.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // Section 16: both connection strings are required. Read lazily they would fail on first use, not at startup.
        services.AddOptions<RequiredConnectionStrings>()
            .BindConfiguration("ConnectionStrings")
            .Validate(
                strings => !string.IsNullOrWhiteSpace(strings.Database) && !string.IsNullOrWhiteSpace(strings.BlobStorage),
                "ConnectionStrings:Database and ConnectionStrings:BlobStorage are required.")
            .ValidateOnStart();
        services.AddSingleton<Cloud.ManagedIdentityDatabase>();
        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var connectionString = configuration.GetConnectionString("Database")
                ?? throw new InvalidOperationException("ConnectionStrings:Database is not configured.");

            // In Azure the password is a managed-identity token from a shared data source (Cloud/ManagedIdentity.cs).
            var builder = Cloud.ManagedIdentity.FromConfiguration(configuration) is null
                ? options.UseNpgsql(connectionString)
                : options.UseNpgsql(provider.GetRequiredService<Cloud.ManagedIdentityDatabase>().DataSource);
            builder.UseSnakeCaseNamingConvention();
        });

        // R-07: time comes only from an injected TimeProvider; tests replace it with FakeTimeProvider.
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAppDbContext>(provider => provider.GetRequiredService<AppDbContext>());
        services.AddSingleton<IDbExceptionTranslator, DbExceptionTranslator>();
        services.AddSingleton<ICorrelationContext, AmbientCorrelationContext>();
        services.AddScoped<IOutbox, Outbox.Outbox>();
        services.AddSingleton<OutboxProcessor>();
        services.AddMarketplaceIdentity();
        services.AddOptions<EmailOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .Validate(
                email => !string.IsNullOrWhiteSpace(email.SmtpHost) && email.SmtpPort is > 0 and <= 65535 && !string.IsNullOrWhiteSpace(email.FromAddress),
                "Email:SmtpHost, Email:SmtpPort and Email:FromAddress are required.")
            .Validate(
                email => string.IsNullOrWhiteSpace(email.SmtpUser) || !string.IsNullOrWhiteSpace(email.SmtpPassword),
                "Email:SmtpPassword is required when Email:SmtpUser is set.")
            .ValidateOnStart();
        // Section 16: links in account emails are built from this, so a relative or malformed value would send
        // every verification and reset mail to a dead address.
        services.AddOptions<AccountLinkOptions>()
            .BindConfiguration(EmailOptions.SectionName)
            .Validate(
                links => Uri.TryCreate(links.LinkBaseUrl, UriKind.Absolute, out var url) && url.Scheme is "http" or "https",
                "Email:LinkBaseUrl must be an absolute http or https URL.")
            .ValidateOnStart();
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        // Section 16: the two containers must differ. CoverContainerName is public-read (T-085), so sharing a name
        // with the private ContainerName would publish every paid file.
        services.AddOptions<StorageOptions>()
            .BindConfiguration(StorageOptions.SectionName)
            .Validate(
                storage => !string.IsNullOrWhiteSpace(storage.ContainerName) && !string.IsNullOrWhiteSpace(storage.CoverContainerName),
                "Storage:ContainerName and Storage:CoverContainerName are required.")
            .Validate(
                storage => !string.Equals(storage.ContainerName, storage.CoverContainerName, StringComparison.OrdinalIgnoreCase),
                "Storage:CoverContainerName must differ from Storage:ContainerName: covers are public-read and product files are not.")
            .Validate(storage => storage.DownloadUrlMinutes > 0, "Storage:DownloadUrlMinutes must be positive.")
            .ValidateOnStart();
        services.AddSingleton<IFileStorage, AzureBlobFileStorage>();
        services.AddSingleton<IPublicImageStorage, AzureBlobPublicImageStorage>();
        services.AddSingleton<IFileScanner, NoopFileScanner>();
        services.AddScoped<IProductSearch, ProductSearch>();
        services.AddScoped<IIdempotencyStore, Idempotency.IdempotencyStore>();
        // Section 16 and D-38: the commission is withheld from every order, so a value outside 0 to 100 percent
        // would pay a seller more than the buyer paid, or nothing at all.
        services.AddOptions<Application.Orders.MarketplaceOptions>()
            .BindConfiguration(Application.Orders.MarketplaceOptions.SectionName)
            .Validate(
                marketplace => marketplace.CommissionBasisPoints is >= 0 and <= 10_000,
                "Marketplace:CommissionBasisPoints must be between 0 and 10000.")
            .ValidateOnStart();

        // Section 16 and D-39: a zero or negative window would make every refund request ineligible on arrival.
        services.AddOptions<Application.Refunds.RefundOptions>()
            .BindConfiguration(Application.Refunds.RefundOptions.SectionName)
            .Validate(refunds => refunds.WindowDays > 0, "Refunds:WindowDays must be positive.")
            .ValidateOnStart();
        services.AddOptions<Application.Refunds.AiQuotaOptions>().BindConfiguration(Application.Refunds.AiQuotaOptions.SectionName);

        // D-25: the provider comes from Payments:Provider. Fake is the default for development and tests.
        // Section 16: Stripe in Production, and Stripe's keys whenever it is the provider.
        services.AddOptions<PaymentOptions>()
            .BindConfiguration(PaymentOptions.SectionName)
            .Validate(
                payments => payments.Provider is PaymentOptions.FakeName or PaymentOptions.StripeName,
                "Payments:Provider must be Fake or Stripe.")
            .Validate<IHostEnvironment>(
                (payments, environment) => !environment.IsProduction() || payments.Provider == PaymentOptions.StripeName,
                "Payments:Provider must be Stripe in Production.")
            .Validate(
                payments => payments.Provider != PaymentOptions.StripeName
                    || (!string.IsNullOrWhiteSpace(payments.Stripe.SecretKey) && !string.IsNullOrWhiteSpace(payments.Stripe.WebhookSecret)),
                "Payments:Stripe:SecretKey and Payments:Stripe:WebhookSecret are required when Payments:Provider is Stripe.")
            .ValidateOnStart();
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
            .Validate(
                ai => ai.Provider is Ai.AiOptions.FakeName or Ai.AiOptions.AnthropicName or Ai.AiOptions.OpenAiName,
                "Ai:Provider must be Fake, Anthropic or OpenAi.")
            .Validate(
                ai => ai.Provider == Ai.AiOptions.FakeName || (!string.IsNullOrWhiteSpace(ai.Model) && !string.IsNullOrWhiteSpace(ai.ApiKey)),
                "Ai:Model and Ai:ApiKey are required when Ai:Provider is Anthropic or OpenAi.")
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
        services.AddHttpClient(Ai.AiOptions.OpenAiName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton(provider => new Ai.OpenAiLlmClient(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(Ai.AiOptions.OpenAiName),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Ai.AiOptions>>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ResilienceSettings>>()));
        services.AddSingleton<Application.Refunds.ILlmClient>(provider =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Ai.AiOptions>>().Value.Provider switch
            {
                Ai.AiOptions.AnthropicName => provider.GetRequiredService<Ai.AnthropicLlmClient>(),
                Ai.AiOptions.OpenAiName => provider.GetRequiredService<Ai.OpenAiLlmClient>(),
                _ => provider.GetRequiredService<Ai.FakeLlmClient>(),
            });
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

internal sealed class RequiredConnectionStrings
{
    public string? Database { get; set; }

    public string? BlobStorage { get; set; }
}
