using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.IntegrationTests.Fixtures;
using Shouldly;

namespace Marketplace.IntegrationTests.Reporting;

// T-052: EP-70 to EP-73 and US-16, driven by real purchases.
public sealed class SellerDashboardTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task US16_Dashboard_ShowsSalesFromPaidOrders()
    {
        using var seller = await AsSellerAsync("dash-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        await BuyAsync("dash-buyer1@example.test", productId);
        await BuyAsync("dash-buyer2@example.test", productId);

        var summary = await seller.GetFromJsonAsync<JsonElement>("/api/v1/sellers/me/dashboard/summary", CancellationToken);
        var months = await seller.GetFromJsonAsync<JsonElement>("/api/v1/sellers/me/dashboard/revenue-by-month?months=3", CancellationToken);
        var top = await seller.GetFromJsonAsync<JsonElement>("/api/v1/sellers/me/dashboard/top-products", CancellationToken);

        Amount(summary, "balance").ShouldBe("35.98");
        Amount(summary, "revenueLast30Days").ShouldBe("39.98");
        summary.GetProperty("ordersLast30Days").GetInt64().ShouldBe(2);
        summary.GetProperty("refundsLast30Days").GetInt64().ShouldBe(0);
        summary.GetProperty("activeProducts").GetInt64().ShouldBe(1);

        var monthRows = months.GetProperty("items").EnumerateArray().ToList();
        monthRows.Count.ShouldBe(3);
        monthRows[^1].GetProperty("month").GetString().ShouldBe($"{Time.GetUtcNow():yyyy-MM}");
        Amount(monthRows[^1], "netSales").ShouldBe("39.98");
        Amount(monthRows[^1], "platformFees").ShouldBe("4.00");
        Amount(monthRows[^1], "runningSellerNet").ShouldBe("35.98");
        Amount(monthRows[0], "netSales").ShouldBe("0.00");

        var topRow = top.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        topRow.GetProperty("productId").GetGuid().ShouldBe(productId);
        topRow.GetProperty("units").GetInt64().ShouldBe(2);
        Amount(topRow, "gross").ShouldBe("39.98");
    }

    [Fact]
    public async Task US16_RecentOrders_PagesNewestFirst()
    {
        using var seller = await AsSellerAsync("recent-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        var first = await BuyAsync("recent-buyer1@example.test", productId);
        Time.Advance(TimeSpan.FromMinutes(1));
        var second = await BuyAsync("recent-buyer2@example.test", productId);
        using var signedIn = WithToken((await SignInAsync("recent-seller@example.test")).AccessToken);

        var page1 = await signedIn.GetFromJsonAsync<JsonElement>("/api/v1/sellers/me/dashboard/recent-orders?limit=1", CancellationToken);
        var cursor = page1.GetProperty("nextCursor").GetString();
        var page2 = await signedIn.GetFromJsonAsync<JsonElement>($"/api/v1/sellers/me/dashboard/recent-orders?limit=1&cursor={cursor}", CancellationToken);

        page1.GetProperty("items")[0].GetProperty("orderId").GetGuid().ShouldBe(second);
        Amount(page1.GetProperty("items")[0], "sellerAmount").ShouldBe("17.99");
        page2.GetProperty("items")[0].GetProperty("orderId").GetGuid().ShouldBe(first);
        page2.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task US16_InvalidQueries_AreRejected()
    {
        using var seller = await AsSellerAsync("bad-seller@example.test");

        (await seller.GetAsync("/api/v1/sellers/me/dashboard/revenue-by-month?months=25", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await seller.GetAsync("/api/v1/sellers/me/dashboard/top-products?days=0", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await seller.GetAsync("/api/v1/sellers/me/dashboard/recent-orders?cursor=not-a-cursor", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private static string Amount(JsonElement element, string property) => element.GetProperty(property).GetProperty("amount").GetString()!;

    private async Task<Guid> BuyAsync(string email, Guid productId)
    {
        using var buyer = await AsBuyerAsync(email);
        var orderId = (await (await CreateOrderAsync(buyer, [productId])).Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid();
        await PayOrderAsync(buyer, orderId);
        return orderId;
    }
}
