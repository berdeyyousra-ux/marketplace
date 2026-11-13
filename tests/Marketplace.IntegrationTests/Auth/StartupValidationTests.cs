using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Marketplace.IntegrationTests.Auth;

// Section 16 startup validation: outside Development the API refuses to start with unsafe or missing configuration.
public sealed class StartupValidationTests
{
    // The published development key from docker-compose.override.yml, assembled so the secret scan does not match it here.
    private static readonly string _developmentKey = Convert.ToBase64String("development-only-signing-key-do-not-use-elsewhere"u8.ToArray());

    private static readonly string _validKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Theory]
    [InlineData("")]
    [InlineData("c2hvcnQ=")]
    [InlineData("not base64!")]
    public async Task Startup_MissingOrWeakSigningKey_FailsFast(string signingKey)
    {
        await using var factory = new ConfigFactory("Staging", ("Jwt:SigningKey", signingKey));

        Should.Throw<OptionsValidationException>(() => factory.CreateClient()).Message.ShouldContain("SigningKey");
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public async Task Startup_DevelopmentSigningKey_FailsFast(string environment)
    {
        await using var factory = new ConfigFactory(environment, ("Jwt:SigningKey", _developmentKey), ("Payments:Provider", "Stripe"),
            ("Payments:Stripe:SecretKey", "sk_test_x"), ("Payments:Stripe:WebhookSecret", "whsec_x"),
            ("Payments:Stripe:PublishableKey", "pk_test_x"));

        StartupFailure(factory).ShouldContain("development key");
    }

    [Fact]
    public async Task Startup_DevelopmentKeyAmongPreviousKeys_FailsFast()
    {
        await using var factory = new ConfigFactory("Staging", ("Jwt:PreviousSigningKeys:0", _developmentKey));

        StartupFailure(factory).ShouldContain("development key");
    }

    [Fact]
    public async Task Startup_FakePaymentsInProduction_FailsFast()
    {
        await using var factory = new ConfigFactory("Production", ("Payments:Provider", "Fake"));

        StartupFailure(factory).ShouldContain("Stripe in Production");
    }

    [Fact]
    public async Task Startup_StripeWithoutKeys_FailsFast()
    {
        await using var factory = new ConfigFactory("Staging", ("Payments:Provider", "Stripe"));

        StartupFailure(factory).ShouldContain("Payments:Stripe:SecretKey");
    }

    // T-064: the client mounts Stripe's payment form with this key, so Stripe without it is a checkout that cannot
    // take a payment. It is public, so it is configuration rather than a vault secret, and still required.
    [Fact]
    public async Task Startup_StripeWithoutPublishableKey_FailsFast()
    {
        await using var factory = new ConfigFactory(
            "Staging",
            ("Payments:Provider", "Stripe"),
            ("Payments:Stripe:SecretKey", "sk_test_x"),
            ("Payments:Stripe:WebhookSecret", "whsec_x"));

        StartupFailure(factory).ShouldContain("Payments:Stripe:PublishableKey");
    }

    [Fact]
    public async Task Startup_WildcardCorsOrigin_FailsFast()
    {
        await using var factory = new ConfigFactory("Staging", ("Cors:AllowedOrigins:0", "*"));

        StartupFailure(factory).ShouldContain("Cors:AllowedOrigins");
    }

    [Fact]
    public async Task Startup_MissingBlobConnectionString_FailsFast()
    {
        await using var factory = new ConfigFactory("Staging", ("ConnectionStrings:BlobStorage", ""));

        StartupFailure(factory).ShouldContain("ConnectionStrings:BlobStorage");
    }

    [Fact]
    public async Task Startup_EmailUserWithoutPassword_FailsFast()
    {
        await using var factory = new ConfigFactory("Staging", ("Email:SmtpUser", "apikey"));

        StartupFailure(factory).ShouldContain("Email:SmtpPassword");
    }

    [Theory]
    [InlineData("")]
    [InlineData("/verify")]
    [InlineData("ftp://marketplace.test")]
    public async Task Startup_LinkBaseUrlNotAnAbsoluteWebUrl_FailsFast(string linkBaseUrl)
    {
        await using var factory = new ConfigFactory("Staging", ("Email:LinkBaseUrl", linkBaseUrl));

        StartupFailure(factory).ShouldContain("Email:LinkBaseUrl");
    }

    // The cover container is public-read, so sharing its name with the private product-file container would
    // publish every paid file.
    [Fact]
    public async Task Startup_CoverContainerSameAsProductContainer_FailsFast()
    {
        await using var factory = new ConfigFactory("Staging", ("Storage:CoverContainerName", "product-files"));

        StartupFailure(factory).ShouldContain("Storage:CoverContainerName");
    }

    [Fact]
    public async Task Startup_NonPositiveDownloadUrlMinutes_FailsFast()
    {
        await using var factory = new ConfigFactory("Staging", ("Storage:DownloadUrlMinutes", "0"));

        StartupFailure(factory).ShouldContain("Storage:DownloadUrlMinutes");
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("10001")]
    public async Task Startup_CommissionOutsideZeroToOneHundredPercent_FailsFast(string basisPoints)
    {
        await using var factory = new ConfigFactory("Staging", ("Marketplace:CommissionBasisPoints", basisPoints));

        StartupFailure(factory).ShouldContain("Marketplace:CommissionBasisPoints");
    }

    [Fact]
    public async Task Startup_NonPositiveRefundWindow_FailsFast()
    {
        await using var factory = new ConfigFactory("Staging", ("Refunds:WindowDays", "0"));

        StartupFailure(factory).ShouldContain("Refunds:WindowDays");
    }

    [Fact]
    public async Task Startup_ValidStagingConfiguration_Starts()
    {
        await using var factory = new ConfigFactory("Staging");

        Should.NotThrow(() => factory.CreateClient());
    }

    // Several rules can fail together, in which case the validator throws them as one AggregateException.
    private static string StartupFailure(WebApplicationFactory<Program> factory)
    {
        var exception = Should.Throw<Exception>(() => factory.CreateClient());
        (exception is OptionsValidationException or AggregateException).ShouldBeTrue(exception.ToString());
        return exception.Message;
    }

    // A Staging or Production host with a valid baseline; each test overrides one setting. Nothing here reaches a database.
    private sealed class ConfigFactory(string environment, params (string Key, string Value)[] overrides) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder
                .UseEnvironment(environment)
                .UseSetting("Ai:Provider", "Fake")
                .UseSetting("ConnectionStrings:Database", "Host=localhost;Port=1;Database=none;Username=none;Password=none")
                .UseSetting("ConnectionStrings:BlobStorage", "UseDevelopmentStorage=true")
                .UseSetting("Jwt:SigningKey", _validKey);
            foreach (var (key, value) in overrides)
            {
                builder.UseSetting(key, value);
            }
        }
    }
}
