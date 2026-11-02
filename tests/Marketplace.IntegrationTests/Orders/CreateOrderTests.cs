using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Domain.Fulfillment;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Marketplace.IntegrationTests.Orders;

// EP-30, US-22 and section 9.3.
public sealed class CreateOrderTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task US22_CreateOrder_SnapshotsPricesFeesAndExpiry()
    {
        using var seller = await AsSellerAsync("order-seller@example.test");
        var first = await CreatePublishedProductAsync(seller, "Budget template", "19.99");
        var second = await CreatePublishedProductAsync(seller, "Font pack", "0.50");
        using var buyer = await AsBuyerAsync("order-buyer@example.test");

        var response = await CreateOrderAsync(buyer, [first, second]);
        var order = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location!.ToString().ShouldBe($"/api/v1/orders/{order.GetProperty("id").GetString()}");
        order.GetProperty("status").GetString().ShouldBe("Pending");
        order.GetProperty("total").GetProperty("amount").GetString().ShouldBe("20.49");
        order.GetProperty("expiresAt").GetDateTimeOffset().ShouldBe(Time.GetUtcNow().AddMinutes(30));
        order.GetProperty("items").GetArrayLength().ShouldBe(2);
        var fees = await WithDbAsync(db => db.OrderItems.OrderBy(item => item.ProductTitle).Select(item => new { item.ProductTitle, item.PlatformFee, item.SellerAmount }).ToListAsync(CancellationToken));
        fees.Single(fee => fee.ProductTitle == "Budget template").PlatformFee.Amount.ShouldBe(2.00m);
        fees.Single(fee => fee.ProductTitle == "Font pack").SellerAmount.Amount.ShouldBe(0.45m);
        (await WithDbAsync(db => db.AuditLog.CountAsync(entry => entry.Action == "order.created", CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public async Task US22_PriceChangeAfterOrder_DoesNotChangeTheOrder()
    {
        using var seller = await AsSellerAsync("snapshot-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller, "Snapshot", "10.00");
        using var buyer = await AsBuyerAsync("snapshot-buyer@example.test");
        var order = await (await CreateOrderAsync(buyer, [productId])).Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        var etag = (await seller.GetAsync($"/api/v1/products/{productId}", CancellationToken)).Headers.ETag!.Tag;
        using var update = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{productId}")
        {
            Content = JsonContent.Create(new { title = "Snapshot", description = "x", price = new { amount = "99.00", currency = "EUR" }, categoryId = 2 }),
        };
        update.Headers.TryAddWithoutValidation("If-Match", etag);
        (await seller.SendAsync(update, CancellationToken)).EnsureSuccessStatusCode();

        var stored = await WithDbAsync(db => db.OrderItems.Select(item => item.UnitPrice).SingleAsync(CancellationToken));
        stored.Amount.ShouldBe(10.00m);
        order.GetProperty("total").GetProperty("amount").GetString().ShouldBe("10.00");
    }

    [Fact]
    public async Task US22_UnpublishedOrUnknownProducts_Return409ListingThem()
    {
        using var seller = await AsSellerAsync("unavailable-seller@example.test");
        var published = await CreatePublishedProductAsync(seller, "Live");
        var draft = (await (await seller.PostAsJsonAsync("/api/v1/products",
            new { title = "Draft only", description = "x", price = new { amount = "5.00", currency = "EUR" }, categoryId = 2 }, CancellationToken))
            .Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid();
        var unknown = Guid.CreateVersion7();
        using var buyer = await AsBuyerAsync("unavailable-buyer@example.test");

        var response = await CreateOrderAsync(buyer, [published, draft, unknown]);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        problem.GetProperty("code").GetString().ShouldBe("product_unavailable");
        problem.GetProperty("errors").GetProperty("productIds").EnumerateArray().Select(id => id.GetGuid()).Order().ShouldBe(new[] { draft, unknown }.Order());
        (await WithDbAsync(db => db.Orders.CountAsync(CancellationToken))).ShouldBe(0);
    }

    [Fact]
    public async Task US22_OwnProduct_Returns409OwnProduct()
    {
        using var seller = await AsSellerAsync("own@example.test");
        var productId = await CreatePublishedProductAsync(seller);

        var response = await CreateOrderAsync(seller, [productId]);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe("own_product");
    }

    [Fact]
    public async Task US22_AlreadyOwned_Returns409AlreadyOwned()
    {
        using var seller = await AsSellerAsync("owned-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        var buyerId = await CreateVerifiedUserAsync("owned-buyer@example.test");
        using var buyer = WithToken((await SignInAsync("owned-buyer@example.test")).AccessToken);
        var orderId = (await (await CreateOrderAsync(buyer, [productId])).Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid();

        // Ownership arrives with payment (T-035); until then the grant is written directly.
        await WithDbAsync(async db =>
        {
            var itemId = await db.OrderItems.Where(item => item.OrderId == orderId).Select(item => item.Id).SingleAsync(CancellationToken);
            db.DownloadGrants.Add(DownloadGrant.Create(itemId, buyerId, productId, Time.GetUtcNow()));
            return await db.SaveChangesAsync(CancellationToken);
        });

        var again = await CreateOrderAsync(buyer, [productId]);

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe("already_owned");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task US22_WrongNumberOfProducts_Returns422(int count)
    {
        using var buyer = await AsBuyerAsync($"count-{count}@example.test");

        var response = await CreateOrderAsync(buyer, Enumerable.Range(0, count).Select(_ => Guid.CreateVersion7()).ToList());

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task US22_DuplicateProductIds_Returns422()
    {
        using var buyer = await AsBuyerAsync("duplicate-ids@example.test");
        var id = Guid.CreateVersion7();

        (await CreateOrderAsync(buyer, [id, id])).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task FM01_DuplicateOrderPost_ReplaysResponse()
    {
        using var seller = await AsSellerAsync("replay-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        using var buyer = await AsBuyerAsync("replay-buyer@example.test");

        var first = await CreateOrderAsync(buyer, [productId], "order-key-1");
        var second = await CreateOrderAsync(buyer, [productId], "order-key-1");

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.StatusCode.ShouldBe(HttpStatusCode.Created);
        second.Headers.GetValues("Idempotent-Replayed").Single().ShouldBe("true");
        (await second.Content.ReadAsStringAsync(CancellationToken)).ShouldBe(await first.Content.ReadAsStringAsync(CancellationToken));
        (await WithDbAsync(db => db.Orders.CountAsync(CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public async Task US22_WithoutIdempotencyKey_Returns400()
    {
        using var buyer = await AsBuyerAsync("no-key@example.test");

        var response = await buyer.PostAsJsonAsync("/api/v1/orders", new { productIds = new[] { Guid.CreateVersion7() } }, CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe("idempotency_key_required");
    }
}
