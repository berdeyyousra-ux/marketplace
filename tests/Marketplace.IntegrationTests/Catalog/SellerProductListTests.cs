using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Domain.Orders;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Marketplace.IntegrationTests.Catalog;

// EP-28, US-15 and D-29 keyset pagination.
public sealed class SellerProductListTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task US15_Pagination_ReturnsEveryOwnProductOnceNewestFirst()
    {
        using var seller = await AsSellerAsync("paging@example.test");
        using var other = await AsSellerAsync("paging-other@example.test", "Other Studio");
        for (var i = 0; i < 25; i++)
        {
            await CreateProductAsync(seller, $"Product {i:00}");
        }

        await CreateProductAsync(other, "Not mine");

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await seller.GetFromJsonAsync<JsonElement>($"/api/v1/sellers/me/products?limit=10{(cursor is null ? string.Empty : "&cursor=" + cursor)}", CancellationToken);
            seen.AddRange(page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!));
            cursor = page.GetProperty("nextCursor").GetString();
            pages++;
        }
        while (cursor is not null);

        pages.ShouldBe(3);
        seen.Count.ShouldBe(25);
        seen.Distinct().Count().ShouldBe(25);
    }

    [Fact]
    public async Task US15_NewestFirstAcrossTimestamps()
    {
        using var seller = await AsSellerAsync("order@example.test");
        var first = await CreateProductAsync(seller, "Older");
        Time.Advance(TimeSpan.FromMinutes(1));
        var second = await CreateProductAsync(seller, "Newer");

        var page = await seller.GetFromJsonAsync<JsonElement>("/api/v1/sellers/me/products", CancellationToken);

        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()).ShouldBe([second, first]);
    }

    [Fact]
    public async Task US15_SalesCount_CountsPaidActiveItemsOnly()
    {
        using var seller = await AsSellerAsync("sales@example.test");
        var productId = Guid.Parse(await CreateProductAsync(seller, "Bestseller"));
        await AddOrderAsync("buyer-paid-1@example.test", productId, paid: true, refunded: false);
        await AddOrderAsync("buyer-paid-2@example.test", productId, paid: true, refunded: false);
        await AddOrderAsync("buyer-pending@example.test", productId, paid: false, refunded: false);
        await AddOrderAsync("buyer-refunded@example.test", productId, paid: true, refunded: true);

        var page = await seller.GetFromJsonAsync<JsonElement>("/api/v1/sellers/me/products", CancellationToken);

        page.GetProperty("items")[0].GetProperty("salesCount").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task US15_StatusFilter_ReturnsOnlyThatStatus()
    {
        using var seller = await AsSellerAsync("filter@example.test");
        await CreateProductAsync(seller, "Draft one");

        var drafts = await seller.GetFromJsonAsync<JsonElement>("/api/v1/sellers/me/products?status=Draft", CancellationToken);
        var published = await seller.GetFromJsonAsync<JsonElement>("/api/v1/sellers/me/products?status=Published", CancellationToken);

        drafts.GetProperty("items").GetArrayLength().ShouldBe(1);
        published.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("cursor=not-a-cursor!!", HttpStatusCode.BadRequest, "invalid_cursor")]
    [InlineData("limit=0", HttpStatusCode.UnprocessableEntity, "validation_failed")]
    [InlineData("limit=101", HttpStatusCode.UnprocessableEntity, "validation_failed")]
    [InlineData("status=Sold", HttpStatusCode.UnprocessableEntity, "validation_failed")]
    public async Task US15_InvalidQuery_IsRejected(string query, HttpStatusCode status, string code)
    {
        using var seller = await AsSellerAsync($"bad-query-{code}-{status}@example.test".Replace("_", "-", StringComparison.Ordinal));

        var response = await seller.GetAsync($"/api/v1/sellers/me/products?{query}", CancellationToken);

        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe(code);
    }

    private static async Task<string> CreateProductAsync(HttpClient seller, string title)
    {
        var response = await seller.PostAsJsonAsync(
            "/api/v1/products",
            new { title, description = "A product.", price = new { amount = "19.99", currency = "EUR" }, categoryId = 2 },
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetString()!;
    }

    // Orders arrive with T-033; until then they are written through the domain directly.
    private async Task AddOrderAsync(string buyerEmail, Guid productId, bool paid, bool refunded)
    {
        var buyerId = await CreateVerifiedUserAsync(buyerEmail);
        await WithDbAsync(async db =>
        {
            var product = await db.Products.AsNoTracking().SingleAsync(candidate => candidate.Id == productId, CancellationToken);
            var order = Order.Create(buyerId, [new OrderLine(product.Id, product.SellerId, product.Title, product.Price)], Time.GetUtcNow(), 30, 1000).Value;
            if (paid)
            {
                order.MarkPaid(Time.GetUtcNow()).IsSuccess.ShouldBeTrue();
            }

            if (refunded)
            {
                order.MarkItemRefunded(order.Items[0].Id, Time.GetUtcNow()).IsSuccess.ShouldBeTrue();
            }

            db.Orders.Add(order);
            return await db.SaveChangesAsync(CancellationToken);
        });
    }
}
