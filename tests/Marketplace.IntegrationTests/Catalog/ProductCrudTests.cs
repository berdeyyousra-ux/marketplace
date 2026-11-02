using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Domain.Catalog;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Marketplace.IntegrationTests.Catalog;

// EP-22 to EP-24, EP-26, EP-27, US-11, US-13, US-14, US-21 and FM-14.
public sealed class ProductCrudTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private static object Details(string title = "Notion Budget Template", string amount = "19.99", string currency = "EUR", int categoryId = 2) =>
        new { title, description = "A monthly budget template.", price = new { amount, currency }, categoryId };

    [Fact]
    public async Task US11_CreateDraft_Returns201WithLocationAndETag()
    {
        using var seller = await AsSellerAsync("create@example.test");

        var response = await seller.PostAsJsonAsync("/api/v1/products", Details(), CancellationToken);
        var product = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location!.ToString().ShouldBe($"/api/v1/products/{product.GetProperty("id").GetString()}");
        response.Headers.ETag.ShouldNotBeNull();
        product.GetProperty("status").GetString().ShouldBe("Draft");
        product.GetProperty("price").GetProperty("amount").GetString().ShouldBe("19.99");
        product.GetProperty("price").GetProperty("currency").GetString().ShouldBe("EUR");
        product.TryGetProperty("version", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("0.49", "EUR", 2)]
    [InlineData("19.999", "EUR", 2)]
    [InlineData("19.99", "USD", 2)]
    [InlineData("abc", "EUR", 2)]
    [InlineData("19.99", "EUR", 999)]
    public async Task US11_InvalidDetails_Returns422(string amount, string currency, int categoryId)
    {
        using var seller = await AsSellerAsync($"invalid-{amount}-{currency}-{categoryId}@example.test");

        var response = await seller.PostAsJsonAsync("/api/v1/products", Details(amount: amount, currency: currency, categoryId: categoryId), CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task US11_Buyer_CannotCreate()
    {
        using var buyer = await AsBuyerAsync("buyer-create@example.test");

        (await buyer.PostAsJsonAsync("/api/v1/products", Details(), CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task US13_UpdateWithCurrentETag_SucceedsAndChangesETag()
    {
        using var seller = await AsSellerAsync("update@example.test");
        var (id, etag) = await CreateAsync(seller);

        var response = await PutAsync(seller, id, etag, Details(title: "Budget Template v2", amount: "24.50"));
        var product = await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        product.GetProperty("title").GetString().ShouldBe("Budget Template v2");
        product.GetProperty("price").GetProperty("amount").GetString().ShouldBe("24.50");
        response.Headers.ETag!.Tag.ShouldNotBe(etag);
    }

    [Fact]
    public async Task FM14_StaleIfMatch_Returns412()
    {
        using var seller = await AsSellerAsync("stale@example.test");
        var (id, etag) = await CreateAsync(seller);
        (await PutAsync(seller, id, etag, Details(title: "First edit"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var stale = await PutAsync(seller, id, etag, Details(title: "Second edit"));

        await ShouldHaveCodeAsync(stale, HttpStatusCode.PreconditionFailed, "precondition_failed");
    }

    [Fact]
    public async Task US13_MissingIfMatch_Returns428()
    {
        using var seller = await AsSellerAsync("no-ifmatch@example.test");
        var (id, _) = await CreateAsync(seller);

        var response = await seller.PutAsJsonAsync($"/api/v1/products/{id}", Details(), CancellationToken);

        await ShouldHaveCodeAsync(response, (HttpStatusCode)428, "precondition_required");
    }

    [Fact]
    public async Task US13_OtherSeller_GetsNotFound()
    {
        using var owner = await AsSellerAsync("owner@example.test");
        using var other = await AsSellerAsync("other-seller@example.test", "Other Studio");
        var (id, etag) = await CreateAsync(owner);

        await ShouldHaveCodeAsync(await PutAsync(other, id, etag, Details(title: "Hijacked")), HttpStatusCode.NotFound, "not_found");
    }

    [Fact]
    public async Task US14_PublishWithoutFile_Returns422ProductIncomplete()
    {
        using var seller = await AsSellerAsync("publish-nofile@example.test");
        var (id, etag) = await CreateAsync(seller);

        await ShouldHaveCodeAsync(await PostWithETagAsync(seller, $"/api/v1/products/{id}/publish", etag), HttpStatusCode.UnprocessableEntity, "product_incomplete");
    }

    [Fact]
    public async Task US14_PublishAndUnpublish_ChangeStatusAndAreAudited()
    {
        using var seller = await AsSellerAsync("publish@example.test");
        var (id, _) = await CreateAsync(seller);
        var etag = await AttachFileAsync(seller, id);

        var published = await PostWithETagAsync(seller, $"/api/v1/products/{id}/publish", etag);
        var unpublished = await PostWithETagAsync(seller, $"/api/v1/products/{id}/unpublish", published.Headers.ETag!.Tag);

        (await published.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("status").GetString().ShouldBe("Published");
        (await unpublished.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("status").GetString().ShouldBe("Unpublished");
        var actions = await WithDbAsync(db => db.AuditLog.Select(entry => entry.Action).ToListAsync(CancellationToken));
        actions.ShouldContain("product.published");
        actions.ShouldContain("product.unpublished");
    }

    [Fact]
    public async Task US21_Visibility_DraftHiddenPublishedPublicFileOnlyForOwner()
    {
        using var seller = await AsSellerAsync("visible@example.test");
        using var stranger = await AsBuyerAsync("stranger@example.test");
        using var anonymous = AsAnonymous();
        var (id, _) = await CreateAsync(seller);
        var etag = await AttachFileAsync(seller, id);

        var draftForStranger = await stranger.GetAsync($"/api/v1/products/{id}", CancellationToken);
        var draftForOwner = await seller.GetAsync($"/api/v1/products/{id}", CancellationToken);
        await PostWithETagAsync(seller, $"/api/v1/products/{id}/publish", etag);
        var publishedForAnonymous = await anonymous.GetAsync($"/api/v1/products/{id}", CancellationToken);
        var publicBody = await publishedForAnonymous.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        var ownerBody = await draftForOwner.Content.ReadFromJsonAsync<JsonElement>(CancellationToken);

        draftForStranger.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        draftForOwner.StatusCode.ShouldBe(HttpStatusCode.OK);
        draftForOwner.Headers.ETag.ShouldNotBeNull();
        ownerBody.GetProperty("file").GetProperty("name").GetString().ShouldBe("budget.pdf");
        publishedForAnonymous.StatusCode.ShouldBe(HttpStatusCode.OK);
        publishedForAnonymous.Headers.ETag.ShouldBeNull();
        publicBody.GetProperty("file").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task ConcurrencyToken_ProductUpdate()
    {
        using var seller = await AsSellerAsync("concurrent@example.test");
        var (id, etag) = await CreateAsync(seller);

        var responses = await Task.WhenAll(
            PutAsync(seller, id, etag, Details(title: "Edit A")),
            PutAsync(seller, id, etag, Details(title: "Edit B")));

        responses.Select(response => response.StatusCode).Order().ShouldBe([HttpStatusCode.OK, HttpStatusCode.PreconditionFailed]);
    }

    private static async Task<(string Id, string ETag)> CreateAsync(HttpClient seller)
    {
        var response = await seller.PostAsJsonAsync("/api/v1/products", Details(), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return (body.GetProperty("id").GetString()!, response.Headers.ETag!.Tag);
    }

    // Upload arrives in T-024 part 2; until then the file metadata is attached directly, and the new ETag is read back.
    private async Task<string> AttachFileAsync(HttpClient seller, string id)
    {
        await WithDbAsync(async db =>
        {
            var product = await db.Products.SingleAsync(candidate => candidate.Id == Guid.Parse(id), CancellationToken);
            product.AttachFile(ProductFile.Create($"products/{id}/file", "budget.pdf", 2048, "application/pdf", new string('a', 64)).Value, Time.GetUtcNow());
            return await db.SaveChangesAsync(CancellationToken);
        });
        return (await seller.GetAsync($"/api/v1/products/{id}", CancellationToken)).Headers.ETag!.Tag;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, string id, string etag, object details)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/products/{id}") { Content = JsonContent.Create(details) };
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> PostWithETagAsync(HttpClient client, string path, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task ShouldHaveCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString().ShouldBe(code);
    }
}
