using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Marketplace.IntegrationTests.Auth;

// Section 11.3: outside Development the API refuses to start without a valid JWT signing key.
public sealed class StartupValidationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("c2hvcnQ=")]
    [InlineData("not base64!")]
    public async Task Startup_MissingOrWeakSigningKey_FailsFast(string signingKey)
    {
        await using var factory = new KeyFactory(signingKey);

        Should.Throw<OptionsValidationException>(() => factory.CreateClient());
    }

    private sealed class KeyFactory(string signingKey) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder
                .UseEnvironment("Staging")
                .UseSetting("ConnectionStrings:Database", "Host=localhost;Port=1;Database=none;Username=none;Password=none")
                .UseSetting("Jwt:SigningKey", signingKey);
    }
}
