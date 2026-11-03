using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.IntegrationTests.Fixtures;
using Shouldly;

namespace Marketplace.IntegrationTests.Catalog;

// T-087: what the catalogue and product page need. The public detail shows the file's type and size, never its name
// or hash (EP-22 keeps those for the owner and admins).
public sealed class ProductDetailFactsTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task PublicDetailAndList_ShowSellerNameAndFileFacts_ButNotTheFileItself()
    {
        using var seller = await AsSellerAsync("facts-seller@example.test", "Facts Studio");
        var productId = await CreatePublishedProductAsync(seller);

        var detail = await AsAnonymous().GetFromJsonAsync<JsonElement>($"/api/v1/products/{productId}", CancellationToken);
        var list = await AsAnonymous().GetFromJsonAsync<JsonElement>("/api/v1/products", CancellationToken);

        detail.GetProperty("sellerName").GetString().ShouldBe("Facts Studio");
        detail.GetProperty("fileType").GetString().ShouldBe("application/pdf");
        detail.GetProperty("fileSizeBytes").GetInt64().ShouldBeGreaterThan(0);
        detail.GetProperty("file").ValueKind.ShouldBe(JsonValueKind.Null);
        detail.GetProperty("viewerOwns").GetBoolean().ShouldBeFalse();
        list.GetProperty("items")[0].GetProperty("sellerName").GetString().ShouldBe("Facts Studio");
    }

    [Fact]
    public async Task ViewerOwns_IsTrueOnlyForABuyerWithAnActiveGrant()
    {
        using var seller = await AsSellerAsync("owns-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        using var buyer = await AsBuyerAsync("owns-buyer@example.test");
        using var other = await AsBuyerAsync("owns-other@example.test");
        var order = await (await CreateOrderAsync(buyer, [productId])).Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        await PayOrderAsync(buyer, order.GetProperty("id").GetGuid());

        (await buyer.GetFromJsonAsync<JsonElement>($"/api/v1/products/{productId}", CancellationToken)).GetProperty("viewerOwns").GetBoolean().ShouldBeTrue();
        (await other.GetFromJsonAsync<JsonElement>($"/api/v1/products/{productId}", CancellationToken)).GetProperty("viewerOwns").GetBoolean().ShouldBeFalse();
    }
}
