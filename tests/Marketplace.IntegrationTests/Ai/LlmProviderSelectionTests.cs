using Marketplace.Application.Refunds;
using Marketplace.Infrastructure;
using Marketplace.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Marketplace.IntegrationTests.Ai;

// Ai:Provider picks the client; a real provider needs Ai:Model and Ai:ApiKey.
public sealed class LlmProviderSelectionTests
{
    [Theory]
    [InlineData("Fake", typeof(FakeLlmClient))]
    [InlineData("Anthropic", typeof(AnthropicLlmClient))]
    [InlineData("OpenAi", typeof(OpenAiLlmClient))]
    public void Provider_SelectsTheClient(string provider, Type expected)
    {
        using var services = Build(new() { ["Ai:Provider"] = provider, ["Ai:Model"] = "configured-model", ["Ai:ApiKey"] = "test-key" });

        services.GetRequiredService<ILlmClient>().ShouldBeOfType(expected);
    }

    [Theory]
    [InlineData("OpenAi", null, "test-key")]
    [InlineData("OpenAi", "configured-model", null)]
    [InlineData("Anthropic", null, "test-key")]
    [InlineData("SomethingElse", "configured-model", "test-key")]
    public void InvalidConfiguration_FailsValidation(string provider, string? model, string? apiKey)
    {
        using var services = Build(new() { ["Ai:Provider"] = provider, ["Ai:Model"] = model, ["Ai:ApiKey"] = apiKey });

        Should.Throw<OptionsValidationException>(() => services.GetRequiredService<IOptions<AiOptions>>().Value);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:Database"] = "Host=localhost;Database=unused";
        settings["ConnectionStrings:BlobStorage"] = "UseDevelopmentStorage=true";
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddLogging();
        services.AddInfrastructure();
        return services.BuildServiceProvider();
    }
}
