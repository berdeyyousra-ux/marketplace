using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Marketplace.Domain.Fulfillment;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
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
        { "GET", "/api/v1/sellers/me/dashboard/summary", [401, 403, 403, 200, 403, 200] },
        { "GET", "/api/v1/sellers/me/dashboard/revenue-by-month", [401, 403, 403, 200, 403, 200] },
        { "GET", "/api/v1/sellers/me/dashboard/top-products", [401, 403, 403, 200, 403, 200] },
        { "GET", "/api/v1/sellers/me/dashboard/recent-orders", [401, 403, 403, 200, 403, 200] },
        { "POST", "/api/v1/sellers", [401, 201, 201, 409, 201, 409] },
        { "POST", "/api/v1/products", [401, 403, 403, 201, 403, 201] },
        { "GET", "/api/v1/products/{draft}", [404, 404, 404, 404, 200, 200] },
        { "PUT", "/api/v1/products/{draft}", [401, 403, 403, 404, 403, 200] },
        { "POST", "/api/v1/products/{draft}/publish", [401, 403, 403, 404, 403, 422] },
        { "POST", "/api/v1/products/{draft}/unpublish", [401, 403, 403, 404, 403, 409] },
        { "POST", "/api/v1/products/{draft}/file", [401, 403, 403, 404, 403, 200] },
        { "PUT", "/api/v1/products/{draft}/cover", [401, 403, 403, 404, 403, 200] },
        { "POST", "/api/v1/auth/change-password", [401, 422, 422, 422, 422, 422] },
        { "POST", "/api/v1/orders", [401, 201, 201, 201, 201, 409] },
        { "POST", "/api/v1/orders/{order}/payment", [401, 200, 404, 404, 404, 404] },
        { "GET", "/api/v1/orders", [401, 200, 200, 200, 200, 200] },
        { "GET", "/api/v1/orders/{order}", [401, 200, 404, 404, 200, 404] },
        { "POST", "/api/v1/orders/{order}/cancel", [401, 204, 404, 404, 404, 404] },
        { "POST", "/api/v1/downloads/{item}/link", [401, 200, 404, 404, 404, 404] },
        { "GET", "/api/v1/library", [401, 200, 200, 200, 200, 200] },
        { "GET", "/api/v1/admin/reports/overview", [401, 403, 403, 403, 200, 403] },
        { "GET", "/api/v1/admin/reports/top-sellers", [401, 403, 403, 403, 200, 403] },
        { "GET", "/api/v1/admin/reports/refund-rates", [401, 403, 403, 403, 200, 403] },
        { "GET", "/api/v1/admin/reports/repeat-buyers", [401, 403, 403, 403, 200, 403] },
        { "GET", "/api/v1/admin/audit-log", [401, 403, 403, 403, 200, 403] },
        { "GET", "/api/v1/admin/outbox/dead-letters", [401, 403, 403, 403, 200, 403] },
        { "POST", "/api/v1/admin/products/{draft}/remove", [401, 403, 403, 403, 200, 403] },
        { "POST", "/api/v1/admin/outbox/{order}/retry", [401, 403, 403, 403, 404, 403] },
        { "POST", "/api/v1/refund-requests", [401, 404, 404, 404, 404, 404] },
        { "GET", "/api/v1/refund-requests/mine", [401, 200, 200, 200, 200, 200] },
        { "GET", "/api/v1/admin/refund-requests", [401, 403, 403, 403, 200, 403] },
        { "GET", "/api/v1/admin/refund-requests/{order}", [401, 403, 403, 403, 404, 403] },
        { "POST", "/api/v1/admin/refund-requests/{order}/decision", [401, 403, 403, 403, 404, 403] },
        { "POST", "/api/v1/admin/refund-requests/{order}/retry", [401, 403, 403, 403, 404, 403] },
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
        var publishedId = await CreatePublishedProductAsync(clients[_seller], "Matrix published");
        var orderId = (await (await CreateOrderAsync(clients[_buyer], [publishedId])).Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetString()!;
        var itemId = pathTemplate.Contains("{item}", StringComparison.Ordinal) ? await GrantDownloadAsync(Guid.Parse(orderId)) : Guid.Empty;
        var path = pathTemplate.Replace("{draft}", draftId, StringComparison.Ordinal).Replace("{order}", orderId, StringComparison.Ordinal)
            .Replace("{item}", itemId.ToString(), StringComparison.Ordinal);

        var actual = new int[clients.Length];
        foreach (var role in new[] { _anonymous, _buyer, _otherBuyer, _otherSeller, _admin, _seller })
        {
            using var request = BuildRequest(method, path, etag, publishedId);
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

    // The buyer owns the order's item without the order being paid, so the payment and cancel rows still apply.
    private async Task<Guid> GrantDownloadAsync(Guid orderId) =>
        await WithDbAsync(async db =>
        {
            var order = await db.Orders.Include(candidate => candidate.Items).SingleAsync(candidate => candidate.Id == orderId, CancellationToken);
            var item = order.Items.Single();
            db.DownloadGrants.Add(DownloadGrant.Create(item.Id, order.BuyerId, item.ProductId, Time.GetUtcNow()));
            await db.SaveChangesAsync(CancellationToken);
            return item.Id;
        });

    // Guards the matrix itself: a new protected endpoint must be added above.
    [Fact]
    public async Task AuthorizationMatrix_CoversEveryProtectedOperation()
    {
        var covered = Matrix().Select(row => $"{row.Data.Item1} {row.Data.Item2.Replace("{draft}", "{id}", StringComparison.Ordinal).Replace("{order}", "{id}", StringComparison.Ordinal).Replace("{item}", "{orderItemId}", StringComparison.Ordinal)}").ToHashSet();
        using var document = JsonDocument.Parse(await AsAnonymous().GetStringAsync("/openapi/v1.json", CancellationToken));

        var protectedOperations = document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => operation.Value.TryGetProperty("security", out var security) && security.GetArrayLength() > 0)
                .Select(operation => $"{operation.Name.ToUpperInvariant()} {path.Name}"));

        protectedOperations.Where(operation => !covered.Contains(operation)).ShouldBeEmpty();
    }

    private static HttpRequestMessage BuildRequest(string method, string path, string etag, Guid publishedId)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (path.EndsWith("/cover", StringComparison.Ordinal))
        {
            var cover = new MultipartFormDataContent();
            var image = new ByteArrayContent(Catalog.CoverImageTests.JpegWithExif());
            image.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            cover.Add(image, "file", "cover.jpg");
            request.Content = cover;
            return request;
        }

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
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Content = (method, path) switch
        {
            ("POST", "/api/v1/sellers") => JsonContent.Create(new { displayName = "Matrix Studio", bio = (string?)null, country = "ES" }),
            ("POST", "/api/v1/products") or ("PUT", _) => JsonContent.Create(ProductBody()),
            ("POST", "/api/v1/refund-requests") => JsonContent.Create(new { orderItemId = Guid.NewGuid(), reason = "Matrix refund request." }),
            ("POST", _) when path.EndsWith("/decision", StringComparison.Ordinal) => JsonContent.Create(new { decision = "Approved", note = "Matrix decision." }),
            ("POST", "/api/v1/orders") => JsonContent.Create(new { productIds = new[] { publishedId } }),
            ("POST", "/api/v1/auth/change-password") => JsonContent.Create(new { currentPassword = "not the current one", newPassword = "a brand new passphrase" }),
            ("POST", _) when path.EndsWith("/remove", StringComparison.Ordinal) => JsonContent.Create(new { reason = "Matrix removal." }),
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
