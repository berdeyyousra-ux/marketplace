using System.Net;
using System.Text.Json;
using Marketplace.IntegrationTests.Fixtures;
using Shouldly;

namespace Marketplace.IntegrationTests.Api;

// T-026, D-20, EP-92 and section 8.1: the OpenAPI document is reviewed through a snapshot.
public sealed class OpenApiTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private static readonly string[] _methods = ["get", "put", "post", "delete", "patch"];

    [Fact]
    public async Task OpenApiSnapshot_MatchesVerifiedBaseline()
    {
        var json = await AsAnonymous().GetStringAsync("/openapi/v1.json", CancellationToken);

        await VerifyJson(json);
    }

    [Fact]
    public async Task OpenApi_EveryOperationHasASummary()
    {
        var operations = await OperationsAsync();

        operations.ShouldNotBeEmpty();
        operations.Where(operation => string.IsNullOrWhiteSpace(operation.Summary)).Select(operation => operation.Name).ShouldBeEmpty();
    }

    [Fact]
    public async Task OpenApi_ProtectedOperationsDeclareBearerAndAnonymousOnesDoNot()
    {
        var operations = (await OperationsAsync()).ToDictionary(operation => operation.Name, operation => operation.Secured);

        operations["GET /api/v1/me"].ShouldBeTrue();
        operations["POST /api/v1/products"].ShouldBeTrue();
        operations["POST /api/v1/auth/login"].ShouldBeFalse();
        operations["GET /api/v1/products"].ShouldBeFalse();
    }

    [Fact]
    public async Task Docs_ServesScalarWithDocsContentSecurityPolicy()
    {
        var response = await AsAnonymous().GetAsync("/docs", CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("script-src 'self'");
    }

    [Fact]
    public async Task Docs_ExposeDocsFalse_HidesDocumentAndUi()
    {
        using var client = Factory.WithWebHostBuilder(builder => builder.UseSetting("Api:ExposeDocs", "false")).CreateClient();

        (await client.GetAsync("/openapi/v1.json", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/docs", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<List<(string Name, string? Summary, bool Secured)>> OperationsAsync()
    {
        using var document = JsonDocument.Parse(await AsAnonymous().GetStringAsync("/openapi/v1.json", CancellationToken));
        var operations = new List<(string, string?, bool)>();
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject().Where(entry => _methods.Contains(entry.Name)))
            {
                var summary = method.Value.TryGetProperty("summary", out var value) ? value.GetString() : null;
                var secured = method.Value.TryGetProperty("security", out var security) && security.GetArrayLength() > 0;
                operations.Add(($"{method.Name.ToUpperInvariant()} {path.Name}", summary, secured));
            }
        }

        return operations;
    }
}
