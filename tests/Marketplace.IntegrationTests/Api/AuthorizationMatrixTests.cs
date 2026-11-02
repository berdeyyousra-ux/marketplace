using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Marketplace.IntegrationTests.Fixtures;
using Shouldly;

namespace Marketplace.IntegrationTests.Api;

// Section 11.6: every protected endpoint called as each role, with the status section 8.3 and section 8.1 require.
// Other users' resources are 404, and role failures are 403.
public sealed class AuthorizationMatrixTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private const int _anonymous = 0;
    private const int _buyer = 1;
    private const int _otherBuyer = 2;
    private const int _otherSeller = 3;
    private const int _admin = 4;
    private const int _seller = 5;

    private static readonly string[] _roleNames = ["anonymous", "buyer", "other buyer", "other seller", "admin", "seller (owner)"];

    // Expected statuses in role order: anonymous, buyer, other buyer, other seller, admin, owning seller.
    // The owner runs last, so earlier denied calls cannot have changed the product it edits.
    public static TheoryData<string, string, int[]> Matrix() => new()
    {
        { "GET", "/api/v1/me", [401, 200, 200, 200, 200, 200] },
        { "GET", "/api/v1/sellers/me", [401, 403, 403, 200, 403, 200] },
        { "GET", "/api/v1/sellers/me/products", [401, 403, 403, 200, 403, 200] },
        { "POST", "/api/v1/sellers", [401, 201, 201, 409, 201, 409] },
        { "POST", "/api/v1/products", [401, 403, 403, 201, 403, 201] },
        { "GET", "/api/v1/products/{draft}", [404, 404, 404, 404, 200, 200] },
        { "PUT", "/api/v1/products/{draft}", [401, 403, 403, 404, 403, 200] },
        { "POST", "/api/v1/products/{draft}/publish", [401, 403, 403, 404, 403, 422] },
        { "POST", "/api/v1/products/{draft}/unpublish", [401, 403, 403, 404, 403, 409] },
        { "POST", "/api/v1/products/{draft}/file", [401, 403, 403, 404, 403, 200] },
        { "POST", "/api/v1/auth/change-password", [401, 422, 422, 422, 422, 422] },
        { "GET", "/api/v1/products", [200, 200, 200, 200, 200, 200] },
        { "GET", "/api/v1/categories", [200, 200, 200, 200, 200, 200] },
    };

    [Theory]
    [MemberData(nameof(Matrix))]
    public async Task AuthorizationMatrix_RoleAccess(string method, string pathTemplate, int[] expected)
    {
        var clients = new[]
        {
            AsAnonymous(),
            await AsBuyerAsync("matrix-buyer@example.test"),
            await AsBuyerAsync("matrix-other-buyer@example.test"),
            await AsSellerAsync("matrix-other-seller@example.test", "Other Studio"),
            await AsAdminAsync(),
            await AsSellerAsync("matrix-seller@example.test", "Owner Studio"),
        };
        var (draftId, etag) = await CreateDraftAsync(clients[_seller]);
        var path = pathTemplate.Replace("{draft}", draftId, StringComparison.Ordinal);

        var actual = new int[clients.Length];
        foreach (var role in new[] { _anonymous, _buyer, _otherBuyer, _otherSeller, _admin, _seller })
        {
            using var request = BuildRequest(method, path, etag);
            actual[role] = (int)(await clients[role].SendAsync(request, CancellationToken)).StatusCode;
        }

        foreach (var client in clients)
        {
            client.Dispose();
        }

        var mismatches = Enumerable.Range(0, expected.Length)
            .Where(role => actual[role] != expected[role])
            .Select(role => $"{_roleNames[role]}: expected {expected[role]}, got {actual[role]}");
        mismatches.ShouldBeEmpty($"{method} {pathTemplate}");
    }

    // Guards the matrix itself: a new protected endpoint must be added above.
    [Fact]
    public async Task AuthorizationMatrix_CoversEveryProtectedOperation()
    {
        var covered = Matrix().Select(row => $"{row.Data.Item1} {row.Data.Item2.Replace("{draft}", "{id}", StringComparison.Ordinal)}").ToHashSet();
        using var document = JsonDocument.Parse(await AsAnonymous().GetStringAsync("/openapi/v1.json", CancellationToken));

        var protectedOperations = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => operation.Value.TryGetProperty("security", out var security) && security.GetArrayLength() > 0)
                .Select(operation => $"{operation.Name.ToUpperInvariant()} {path.Name}"));

        protectedOperations.Where(operation => !covered.Contains(operation)).ShouldBeEmpty();
    }

    private static HttpRequestMessage BuildRequest(string method, string path, string etag)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (path.EndsWith("/file", StringComparison.Ordinal))
        {
            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.7\nmatrix\n"));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", "matrix.pdf");
            request.Content = form;
            return request;
        }

        request.Headers.TryAddWithoutValidation("If-Match", etag);
        request.Content = (method, path) switch
        {
            ("POST", "/api/v1/sellers") => JsonContent.Create(new { displayName = "Matrix Studio", bio = (string?)null, country = "ES" }),
            ("POST", "/api/v1/products") or ("PUT", _) => JsonContent.Create(ProductBody()),
            ("POST", "/api/v1/auth/change-password") => JsonContent.Create(new { currentPassword = "not the current one", newPassword = "a brand new passphrase" }),
            _ => null,
        };
        return request;
    }

    private static object ProductBody() =>
        new { title = "Matrix product", description = "For the matrix.", price = new { amount = "9.99", currency = "EUR" }, categoryId = 2 };

    private static async Task<(string Id, string ETag)> CreateDraftAsync(HttpClient seller)
    {
        var response = await seller.PostAsJsonAsync("/api/v1/products", ProductBody(), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetString()!;
        return (id, response.Headers.ETag!.Tag);
    }
}
