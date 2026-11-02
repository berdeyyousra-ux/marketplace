using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Domain.Ledger;
using Marketplace.Domain.Orders;
using Marketplace.IntegrationTests.Fixtures;
using Marketplace.Tools.Seeding;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Marketplace.IntegrationTests.Tools;

// T-050: both seed commands against the migrated test database, so every constraint and trigger applies.
public sealed class SeedToolTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public void Generator_IsDeterministicForTheSameSeedAndTime()
    {
        var options = new SeedOptions(500, 5, 50, 40, 42, Time.GetUtcNow());

        var first = new SeedGenerator(options).Generate();
        var second = new SeedGenerator(options).Generate();
        var other = new SeedGenerator(options with { Seed = 43 }).Generate();

        second.Orders.Select(order => order.Id).ShouldBe(first.Orders.Select(order => order.Id));
        second.Items.Select(item => item.UnitPrice).ShouldBe(first.Items.Select(item => item.UnitPrice));
        other.Orders[0].Id.ShouldNotBe(first.Orders[0].Id);
    }

    [Fact]
    public async Task SeedLoad_WritesAConsistentDataset()
    {
        var options = new SeedOptions(3000, 12, 300, 200, 42, Time.GetUtcNow());

        var data = await SeedCommands.LoadAsync(options, Factory.DatabaseConnectionString, TextWriter.Null, CancellationToken);

        var paidItems = data.Items.Where(item => data.Orders[item.OrderIndex].PaidAt is not null).ToList();
        var refunded = paidItems.Count(item => item.RefundedAt is not null);
        (await WithDbAsync(db => db.Orders.CountAsync(CancellationToken))).ShouldBe(3000);
        (await WithDbAsync(db => db.OrderItems.CountAsync(CancellationToken))).ShouldBe(data.Items.Length);
        (await WithDbAsync(db => db.DownloadGrants.CountAsync(CancellationToken))).ShouldBe(paidItems.Count);
        (await WithDbAsync(db => db.LedgerEntries.CountAsync(CancellationToken))).ShouldBe((2 * paidItems.Count) + (2 * refunded));
        refunded.ShouldBeGreaterThan(0);

        // The ledger agrees with the items it was derived from (section 13.1 reads only the ledger).
        var sales = await WithDbAsync(db => db.LedgerEntries.Where(entry => entry.EntryType == LedgerEntryType.Sale).SumAsync(entry => entry.Amount.Amount, CancellationToken));
        sales.ShouldBe(paidItems.Sum(item => item.UnitPrice));
        var orderTotals = await WithDbAsync(db => db.Orders.Where(order => order.Status == OrderStatus.Paid).SumAsync(order => order.Total.Amount, CancellationToken));
        orderTotals.ShouldBe(data.Orders.Where(order => order.Status == SeedOrderStatus.Paid).Sum(order => order.Total));

        // Running it again on a seeded database is refused.
        await Should.ThrowAsync<InvalidOperationException>(() => SeedCommands.LoadAsync(options, Factory.DatabaseConnectionString, TextWriter.Null, CancellationToken));
    }

    [Fact]
    public async Task SeedDemo_CreatesBrowsableDataThatSeededUsersCanDownload()
    {
        CatchUpWithRealTime();

        var data = await SeedCommands.DemoAsync(
            SeedOptions.Demo(Time.GetUtcNow()), Factory.DatabaseConnectionString, Factory.BlobConnectionString, TextWriter.Null, CancellationToken);

        var products = await AsAnonymous().GetFromJsonAsync<JsonElement>("/api/v1/products?limit=100", CancellationToken);
        products.GetProperty("items").GetArrayLength().ShouldBe(data.Products.Count(product => product.Status == "Published"));

        var owner = data.Orders.First(order => order.Status == SeedOrderStatus.Paid);
        using var buyer = WithToken((await SignInAsync(data.Buyers[owner.BuyerIndex].Email, SeedOptions.Password)).AccessToken);
        var library = await buyer.GetFromJsonAsync<JsonElement>("/api/v1/library", CancellationToken);
        var item = library.GetProperty("items").EnumerateArray().First(entry => entry.GetProperty("status").GetString() == "Active");
        var link = await buyer.PostAsync($"/api/v1/downloads/{item.GetProperty("orderItemId").GetGuid()}/link", content: null, CancellationToken);
        link.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var plain = new HttpClient();
        var url = (await link.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("url").GetString()!;
        (await plain.GetStringAsync(new Uri(url), CancellationToken)).ShouldContain("seed-demo");
    }
}
