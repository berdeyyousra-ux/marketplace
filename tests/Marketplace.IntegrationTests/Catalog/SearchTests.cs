using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Marketplace.IntegrationTests.Catalog;

// EP-21, US-20 and section 13.3.
public sealed class SearchTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private const int _templates = 2;
    private const int _ebooks = 1;

    [Fact]
    public async Task US20_TextSearch_MatchesTitleAndDescriptionWithStemming()
    {
        var seller = await SellerAsync("search-text@example.test");
        await SeedAsync(seller, "Budget planner template", "Plan a monthly budget.", 10m);
        await SeedAsync(seller, "Recipe book", "Cook on a budget with these templates.", 12m);
        await SeedAsync(seller, "Pixel font", "A retro typeface.", 8m);

        var titles = await TitlesAsync("/api/v1/products?q=templates");

        titles.Order().ShouldBe(["Budget planner template", "Recipe book"]);
    }

    [Fact]
    public async Task US20_WebSearchSyntax_ExcludesNegatedTerm()
    {
        var seller = await SellerAsync("search-syntax@example.test");
        await SeedAsync(seller, "Budget planner", "Monthly budget.", 10m);
        await SeedAsync(seller, "Recipe book", "Cook on a budget.", 12m);

        (await TitlesAsync("/api/v1/products?q=budget%20-recipe")).ShouldBe(["Budget planner"]);
    }

    [Fact]
    public async Task US20_Relevance_TitleMatchRanksFirstAndHasNoCursor()
    {
        var seller = await SellerAsync("search-rank@example.test");
        await SeedAsync(seller, "Recipe book", "Cook on a budget.", 12m);
        await SeedAsync(seller, "Budget planner", "Plan your month.", 10m);

        var page = await AsAnonymous().GetFromJsonAsync<JsonElement>("/api/v1/products?q=budget&sort=Relevance", CancellationToken);

        page.GetProperty("items")[0].GetProperty("title").GetString().ShouldBe("Budget planner");
        page.GetProperty("nextCursor").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task US20_OnlyPublishedProductsAreListed()
    {
        var seller = await SellerAsync("search-status@example.test");
        await SeedAsync(seller, "Live product", "Visible.", 10m);
        await SeedAsync(seller, "Draft product", "Hidden.", 10m, publish: false);

        (await TitlesAsync("/api/v1/products")).ShouldBe(["Live product"]);
    }

    [Fact]
    public async Task US20_CategoryAndPriceFilters_Apply()
    {
        var seller = await SellerAsync("search-filters@example.test");
        await SeedAsync(seller, "Cheap template", "x", 5m, categoryId: _templates);
        await SeedAsync(seller, "Pricey template", "x", 50m, categoryId: _templates);
        await SeedAsync(seller, "An ebook", "x", 9m, categoryId: _ebooks);

        (await TitlesAsync("/api/v1/products?categorySlug=templates")).Order().ShouldBe(["Cheap template", "Pricey template"]);
        (await TitlesAsync("/api/v1/products?minPrice=6&maxPrice=20")).ShouldBe(["An ebook"]);
    }

    [Theory]
    [InlineData("PriceAsc")]
    [InlineData("PriceDesc")]
    [InlineData("Newest")]
    public async Task US20_KeysetPagination_ReturnsEveryProductOnceInOrder(string sort)
    {
        var seller = await SellerAsync($"search-paging-{sort}@example.test");
        for (var i = 0; i < 12; i++)
        {
            await SeedAsync(seller, $"Item {i:00}", "x", 5m + (i % 3));
        }

        var prices = new List<decimal>();
        var ids = new List<string>();
        string? cursor = null;
        do
        {
            var page = await AsAnonymous().GetFromJsonAsync<JsonElement>(
                $"/api/v1/products?sort={sort}&limit=5{(cursor is null ? string.Empty : "&cursor=" + cursor)}", CancellationToken);
            foreach (var item in page.GetProperty("items").EnumerateArray())
            {
                ids.Add(item.GetProperty("id").GetString()!);
                prices.Add(decimal.Parse(item.GetProperty("price").GetProperty("amount").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
            }

            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        ids.Distinct().Count().ShouldBe(12);
        if (sort == "PriceAsc")
        {
            prices.ShouldBe(prices.Order().ToList());
        }
        else if (sort == "PriceDesc")
        {
            prices.ShouldBe(prices.OrderDescending().ToList());
        }
    }

    [Theory]
    [InlineData("sort=Relevance", HttpStatusCode.UnprocessableEntity, "validation_failed")]
    [InlineData("minPrice=20&maxPrice=10", HttpStatusCode.UnprocessableEntity, "validation_failed")]
    [InlineData("cursor=%%%", HttpStatusCode.BadRequest, "invalid_cursor")]
    public async Task US20_InvalidQuery_IsRejected(string query, HttpStatusCode status, string code)
    {
        var response = await AsAnonymous().GetAsync($"/api/v1/products?{query}", CancellationToken);

        response.StatusCode.ShouldBe(status);
        (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe(code);
    }

    [Fact]
    public async Task US20_CursorFromAnotherSort_IsRejected()
    {
        var seller = await SellerAsync("search-cursor@example.test");
        for (var i = 0; i < 3; i++)
        {
            await SeedAsync(seller, $"Item {i}", "x", 5m + i);
        }

        var page = await AsAnonymous().GetFromJsonAsync<JsonElement>("/api/v1/products?sort=PriceAsc&limit=1", CancellationToken);
        var response = await AsAnonymous().GetAsync($"/api/v1/products?sort=Newest&cursor={page.GetProperty("nextCursor").GetString()}", CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private async Task<Guid> SellerAsync(string email)
    {
        using var _ = await AsSellerAsync(email);
        return await WithDbAsync(db => db.SellerProfiles
            .Where(profile => db.Users.Any(user => user.Id == profile.UserId && user.Email == email))
            .Select(profile => profile.Id).SingleAsync(CancellationToken));
    }

    private Task<int> SeedAsync(Guid sellerId, string title, string description, decimal price, int categoryId = _templates, bool publish = true) =>
        WithDbAsync(async db =>
        {
            var product = Product.Create(sellerId, categoryId, title, description, Money.Eur(price), Time.GetUtcNow()).Value;
            product.AttachFile(ProductFile.Create($"products/{product.Id}/f", "f.pdf", 1, "application/pdf", new string('a', 64)).Value, Time.GetUtcNow());
            if (publish)
            {
                product.Publish(Time.GetUtcNow()).IsSuccess.ShouldBeTrue();
            }

            db.Products.Add(product);
            return await db.SaveChangesAsync(CancellationToken);
        });

    private async Task<List<string>> TitlesAsync(string path)
    {
        var page = await AsAnonymous().GetFromJsonAsync<JsonElement>(path, CancellationToken);
        return page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("title").GetString()!).ToList();
    }
}
