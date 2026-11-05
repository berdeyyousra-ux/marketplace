using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Marketplace.IntegrationTests.Orders;

// EP-50, EP-51, US-26 and FM-19.
public sealed class DownloadTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task US26_PaidBuyer_GetsSignedUrlThatDownloadsTheFileFromAzurite()
    {
        CatchUpWithRealTime();

        var (buyer, itemId) = await PaidItemAsync("signed");
        using (buyer)
        {
            var response = await buyer.PostAsync($"/api/v1/downloads/{itemId}/link", content: null, CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var link = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
            link.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(Time.GetUtcNow().AddMinutes(5));

            using var plain = new HttpClient();
            var file = await plain.GetAsync(new Uri(link.GetProperty("url").GetString()!), CancellationToken);

            file.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await file.Content.ReadAsStringAsync(CancellationToken)).ShouldBe("%PDF-1.7\ntest product\n");
            file.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
            file.Content.Headers.ContentDisposition.FileName!.Trim('"').ShouldBe("product.pdf");
            (await WithDbAsync(db => db.DownloadGrants.Select(grant => grant.DownloadCount).SingleAsync(CancellationToken))).ShouldBe(1);
            (await WithDbAsync(db => db.AuditLog.CountAsync(entry => entry.Action == "download.link_issued", CancellationToken))).ShouldBe(1);
        }
    }

    [Fact]
    public async Task FM19_BeyondTenDownloads_StaysAvailable()
    {
        var (buyer, itemId) = await PaidItemAsync("limit");
        using (buyer)
        {
            for (var i = 0; i < 15; i++)
            {
                (await buyer.PostAsync($"/api/v1/downloads/{itemId}/link", content: null, CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
            }

            (await WithDbAsync(db => db.DownloadGrants.Select(grant => grant.DownloadCount).SingleAsync(CancellationToken))).ShouldBe(15);
        }
    }

    [Fact]
    public async Task US26_RevokedOrExpiredGrant_Returns409WithReason()
    {
        var (buyer, itemId) = await PaidItemAsync("revoked");
        var (otherBuyer, otherItemId) = await PaidItemAsync("expired");
        using (buyer)
        using (otherBuyer)
        {
            await WithDbAsync(async db =>
            {
                var grant = await db.DownloadGrants.SingleAsync(candidate => candidate.OrderItemId == itemId, CancellationToken);
                grant.Revoke(Time.GetUtcNow());
                return await db.SaveChangesAsync(CancellationToken);
            });
            await ShouldBeUnavailableAsync(await buyer.PostAsync($"/api/v1/downloads/{itemId}/link", content: null, CancellationToken), "revoked");

            Time.Advance(TimeSpan.FromDays(366));
            using var signedIn = WithToken((await SignInAsync("expired-buyer@example.test")).AccessToken);
            await ShouldBeUnavailableAsync(await signedIn.PostAsync($"/api/v1/downloads/{otherItemId}/link", content: null, CancellationToken), "expired");
        }
    }

    [Fact]
    public async Task US26_AnotherBuyersItem_Returns404()
    {
        var (buyer, itemId) = await PaidItemAsync("owner");
        using (buyer)
        {
            using var stranger = await AsBuyerAsync("stranger@example.test");

            var response = await stranger.PostAsync($"/api/v1/downloads/{itemId}/link", content: null, CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task US26_Library_ListsOwnedProductsWithGrantStatus()
    {
        var (buyer, itemId) = await PaidItemAsync("library");
        using (buyer)
        {
            await buyer.PostAsync($"/api/v1/downloads/{itemId}/link", content: null, CancellationToken);

            var library = await buyer.GetFromJsonAsync<JsonElement>("/api/v1/library", CancellationToken);
            using var stranger = await AsBuyerAsync("library-stranger@example.test");
            var strangers = await stranger.GetFromJsonAsync<JsonElement>("/api/v1/library", CancellationToken);

            var item = library.GetProperty("items").EnumerateArray().Single();
            item.GetProperty("orderItemId").GetGuid().ShouldBe(itemId);
            item.GetProperty("title").GetString().ShouldBe("Notion Budget Template");
            item.GetProperty("fileName").GetString().ShouldBe("product.pdf");
            item.GetProperty("downloadCount").GetInt32().ShouldBe(1);
            item.GetProperty("status").GetString().ShouldBe("Active");
            strangers.GetProperty("items").GetArrayLength().ShouldBe(0);
        }
    }

    private static async Task ShouldBeUnavailableAsync(HttpResponseMessage response, string reason)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        problem.GetProperty("code").GetString().ShouldBe("download_unavailable");
        problem.GetProperty("reason").GetString().ShouldBe(reason);
    }

    // Places an order, pays it through the fake provider and its signed webhook, and processes the outbox.
    private async Task<(HttpClient Buyer, Guid ItemId)> PaidItemAsync(string prefix)
    {
        using var seller = await AsSellerAsync($"{prefix}-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        var buyer = await AsBuyerAsync($"{prefix}-buyer@example.test");
        var order = await (await CreateOrderAsync(buyer, [productId])).Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var orderId = order.GetProperty("id").GetGuid();

        await PayOrderAsync(buyer, orderId);
        return (buyer, order.GetProperty("items")[0].GetProperty("id").GetGuid());
    }
}
