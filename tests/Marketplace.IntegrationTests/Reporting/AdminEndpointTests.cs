using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Application.Ops;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Marketplace.IntegrationTests.Reporting;

// T-053: EP-74 to EP-77 and EP-80 to EP-83 (US-32, US-33, US-34).
public sealed class AdminEndpointTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task US33_Overview_ReportsTotalsPercentilesAndFunnel()
    {
        var productId = await SellAsync("overview", buyers: 2);
        using var admin = await AsAdminAsync();

        var overview = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/reports/overview", CancellationToken);

        Amount(overview, "netSales").ShouldBe("39.98");
        Amount(overview, "platformFees").ShouldBe("4.00");
        overview.GetProperty("paidOrders").GetInt64().ShouldBe(2);
        overview.GetProperty("refundRate").GetDecimal().ShouldBe(0m);
        overview.GetProperty("orderValue").GetProperty("orders").GetInt64().ShouldBe(2);
        Amount(overview.GetProperty("orderValue"), "p50").ShouldBe("19.99");
        var funnel = overview.GetProperty("funnel").EnumerateArray().ToList();
        funnel.Sum(day => day.GetProperty("created").GetInt64()).ShouldBe(2);
        funnel.Sum(day => day.GetProperty("paymentStarted").GetInt64()).ShouldBe(2);
        funnel.Sum(day => day.GetProperty("paid").GetInt64()).ShouldBe(2);
        productId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task US33_Overview_RejectsAnInvalidPeriod()
    {
        using var admin = await AsAdminAsync();
        var now = Time.GetUtcNow();

        var reversed = await admin.GetAsync($"/api/v1/admin/reports/overview?from={Iso(now)}&to={Iso(now.AddDays(-1))}", CancellationToken);
        var tooLong = await admin.GetAsync($"/api/v1/admin/reports/overview?from={Iso(now.AddDays(-400))}&to={Iso(now)}", CancellationToken);

        reversed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        tooLong.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task US33_SellerAndBuyerReports_ReflectSales()
    {
        await SellAsync("reports", buyers: 2);
        using var admin = await AsAdminAsync();

        var topSellers = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/reports/top-sellers", CancellationToken);
        var refundRates = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/reports/refund-rates", CancellationToken);
        var cohorts = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/reports/repeat-buyers?months=1", CancellationToken);

        var seller = topSellers.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        seller.GetProperty("displayName").GetString().ShouldBe("Studio reports");
        Amount(seller, "netSales").ShouldBe("39.98");
        seller.GetProperty("units").GetInt64().ShouldBe(2);
        var sellerRate = refundRates.GetProperty("sellers").EnumerateArray().ShouldHaveSingleItem();
        (sellerRate.GetProperty("unitsSold").GetInt64(), sellerRate.GetProperty("unitsRefunded").GetInt64()).ShouldBe((2L, 0L));
        refundRates.GetProperty("products").GetArrayLength().ShouldBe(0, "products need at least 5 units sold");
        var cohort = cohorts.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        cohort.GetProperty("cohort").GetString().ShouldBe($"{Time.GetUtcNow():yyyy-MM}");
        cohort.GetProperty("buyers").GetInt64().ShouldBe(2);
        (await admin.GetAsync("/api/v1/admin/reports/top-sellers?limit=0", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task US32_RemoveProduct_HidesItButBuyersKeepTheirDownloads()
    {
        CatchUpWithRealTime();
        using var seller = await AsSellerAsync("remove-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        using var buyer = await AsBuyerAsync("remove-buyer@example.test");
        var order = await (await CreateOrderAsync(buyer, [productId])).Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        await PayOrderAsync(buyer, order.GetProperty("id").GetGuid());
        using var admin = await AsAdminAsync();

        var removed = await admin.PostAsJsonAsync($"/api/v1/admin/products/{productId}/remove", new { reason = "Infringes a trademark." }, CancellationToken);

        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await removed.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("status").GetString().ShouldBe("Removed");
        var list = await AsAnonymous().GetFromJsonAsync<JsonElement>("/api/v1/products", CancellationToken);
        list.GetProperty("items").GetArrayLength().ShouldBe(0);
        (await AsAnonymous().GetAsync($"/api/v1/products/{productId}", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var itemId = order.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await buyer.PostAsync($"/api/v1/downloads/{itemId}/link", content: null, CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var audit = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/audit-log?entityType=Product&entityId={productId}&limit=5", CancellationToken);
        var entry = audit.GetProperty("items").EnumerateArray().First(item => item.GetProperty("action").GetString() == "product.removed");
        entry.GetProperty("details").GetProperty("reason").GetString().ShouldBe("Infringes a trademark.");

        (await admin.PostAsJsonAsync($"/api/v1/admin/products/{productId}/remove", new { reason = "Again." }, CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync($"/api/v1/admin/products/{productId}/remove", new { reason = "" }, CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await admin.PostAsJsonAsync($"/api/v1/admin/products/{Guid.NewGuid()}/remove", new { reason = "Missing." }, CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task US34_AuditLog_PagesNewestFirstAndFilters()
    {
        await SellAsync("audit", buyers: 1);
        using var admin = await AsAdminAsync();

        var first = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/audit-log?limit=2", CancellationToken);
        var cursor = first.GetProperty("nextCursor").GetString();
        var second = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/audit-log?limit=2&cursor={cursor}", CancellationToken);
        var paidOnly = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/audit-log?entityType=Order&limit=100", CancellationToken);

        var firstIds = first.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt64()).ToList();
        var secondIds = second.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt64()).ToList();
        firstIds.Count.ShouldBe(2);
        secondIds.ShouldNotBeEmpty();
        secondIds.Max().ShouldBeLessThan(firstIds.Min());
        paidOnly.GetProperty("items").EnumerateArray().ShouldAllBe(item => item.GetProperty("entityType").GetString() == "Order");
        paidOnly.GetProperty("items").EnumerateArray().ShouldContain(item => item.GetProperty("action").GetString() == "order.paid");
        (await admin.GetAsync("/api/v1/admin/audit-log?cursor=bad", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task US34_DeadLetter_IsListedAndCanBeRetried()
    {
        var now = Time.GetUtcNow();
        var message = new OutboxMessage { Type = "DeadForTest", Payload = "{}", CreatedAt = now, AvailableAt = now };
        await WithDbAsync(async db =>
        {
            db.OutboxMessages.Add(message);
            await db.SaveChangesAsync(CancellationToken);
            return await db.Database.ExecuteSqlAsync(
                $"UPDATE outbox_messages SET attempts = 8, last_error = 'boom', dead_lettered_at = {now} WHERE id = {message.Id}", CancellationToken);
        });
        using var admin = await AsAdminAsync();

        var letters = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/outbox/dead-letters", CancellationToken);
        var retried = await admin.PostAsync($"/api/v1/admin/outbox/{message.Id}/retry", content: null, CancellationToken);
        var again = await admin.PostAsync($"/api/v1/admin/outbox/{message.Id}/retry", content: null, CancellationToken);
        var missing = await admin.PostAsync($"/api/v1/admin/outbox/{Guid.NewGuid()}/retry", content: null, CancellationToken);

        var letter = letters.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        (letter.GetProperty("id").GetGuid(), letter.GetProperty("attempts").GetInt32(), letter.GetProperty("lastError").GetString()).ShouldBe((message.Id, 8, "boom"));
        retried.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var row = await WithDbAsync(db => db.OutboxMessages.AsNoTracking().SingleAsync(candidate => candidate.Id == message.Id, CancellationToken));
        (row.DeadLetteredAt, row.Attempts, row.AvailableAt).ShouldBe((null, 0, now));
        (await WithDbAsync(db => db.AuditLog.CountAsync(entry => entry.Action == "outbox.retried" && entry.EntityId == message.Id, CancellationToken))).ShouldBe(1);
    }

    private static string Amount(JsonElement element, string property) => element.GetProperty(property).GetProperty("amount").GetString()!;

    private static string Iso(DateTimeOffset time) => Uri.EscapeDataString(time.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

    // One product sold once to each of N buyers.
    private async Task<Guid> SellAsync(string prefix, int buyers)
    {
        using var seller = await AsSellerAsync($"{prefix}-seller@example.test", $"Studio {prefix}");
        var productId = await CreatePublishedProductAsync(seller);
        for (var i = 1; i <= buyers; i++)
        {
            using var buyer = await AsBuyerAsync($"{prefix}-buyer{i}@example.test");
            var orderId = (await (await CreateOrderAsync(buyer, [productId])).Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid();
            await PayOrderAsync(buyer, orderId);
        }

        return productId;
    }
}
