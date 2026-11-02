using Marketplace.Application.Reporting;
using Marketplace.IntegrationTests.Fixtures;
using Marketplace.Tools.Seeding;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Reporting;

// T-051: every report R-01 to R-09 runs on the demo seed and is checked against the same figures computed in
// C# from the generated data. Ties in rank order are compared as values, because PostgreSQL and .NET order
// uuids differently.
public sealed class ReportQueryTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private SeedDataset _data = null!;
    private DateTimeOffset _now;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _now = Time.GetUtcNow();
        _data = await SeedCommands.LoadAsync(SeedOptions.Demo(_now), Factory.DatabaseConnectionString, TextWriter.Null, CancellationToken);
    }

    [Fact]
    public async Task R01_MonthlyRevenue_MatchesTheLedgerWithEmptyMonthsAsZero()
    {
        var seller = BusiestSeller();
        var thisMonth = new DateTimeOffset(_now.Year, _now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var from = thisMonth.AddMonths(-23);
        var to = thisMonth.AddMonths(1);

        var rows = await ReportsAsync(reports => reports.SellerMonthlyRevenueAsync(_data.Sellers[seller].Id, from, to, CancellationToken));

        rows.Count.ShouldBe(24);
        var running = 0m;
        for (var month = 0; month < 24; month++)
        {
            var start = from.AddMonths(month);
            var entries = Ledger(seller).Where(entry => entry.At >= start && entry.At < start.AddMonths(1)).ToList();
            var netSales = entries.Where(entry => entry.Type is "Sale" or "Refund").Sum(entry => entry.Amount);
            var fees = entries.Where(entry => entry.Type is "PlatformFee" or "FeeRefund").Sum(entry => entry.Amount);
            running += netSales + fees;
            rows[month].ShouldBe(new MonthlyRevenueRow($"{start:yyyy-MM}", netSales, -fees, netSales + fees, running));
        }
    }

    [Fact]
    public async Task R02_TopProducts_MatchActiveItemsPaidInThePeriod()
    {
        var since = _now.AddDays(-90);
        var expected = ActiveItemsPaidSince(since)
            .GroupBy(item => item.ProductIndex)
            .Select(group => (Product: _data.Products[group.Key], Units: (long)group.Count(), Gross: group.Sum(item => item.UnitPrice)))
            .ToList();

        var perCategory = await ReportsAsync(reports => reports.TopProductsPerCategoryAsync(since, 5, CancellationToken));
        foreach (var category in expected.GroupBy(entry => entry.Product.CategoryId))
        {
            var top = perCategory.Where(row => row.CategoryId == category.Key).ToList();
            top.Select(row => row.Units).ShouldBe(category.Select(entry => entry.Units).OrderDescending().Take(5));
            top.Select(row => row.Rank).ShouldBe(Enumerable.Range(1, top.Count).Select(rank => (long)rank));
            foreach (var row in top)
            {
                var match = category.Single(entry => entry.Product.Id == row.ProductId);
                (row.Units, row.Gross, row.Title).ShouldBe((match.Units, match.Gross, match.Product.Title));
            }
        }

        var seller = BusiestSeller();
        var sellerTop = await ReportsAsync(reports => reports.SellerTopProductsAsync(_data.Sellers[seller].Id, since, 3, CancellationToken));
        var sellerExpected = expected.Where(entry => entry.Product.SellerIndex == seller).ToList();
        sellerTop.Select(row => row.Units).ShouldBe(sellerExpected.Select(entry => entry.Units).OrderDescending().Take(3));
        sellerTop.ShouldAllBe(row => sellerExpected.Any(entry => entry.Product.Id == row.ProductId && entry.Gross == row.Gross));
    }

    [Fact]
    public async Task R03_RepeatBuyerCohorts_MatchFirstAndNextPayments()
    {
        var from = _now.AddDays(-730);
        var paidByBuyer = _data.Orders.Where(order => order.PaidAt is not null)
            .GroupBy(order => order.BuyerIndex)
            .Select(group => group.Select(order => order.PaidAt!.Value).Order().ToList())
            .Where(times => times[0] >= from && times[0] < _now)
            .ToList();
        var expected = paidByBuyer
            .GroupBy(times => $"{times[0]:yyyy-MM}")
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new RepeatBuyerCohortRow(
                group.Key, group.Count(), Share(group, 30), Share(group, 60), Share(group, 90)))
            .ToList();

        var rows = await ReportsAsync(reports => reports.RepeatBuyerCohortsAsync(from, _now, CancellationToken));

        rows.ShouldBe(expected);
    }

    [Fact]
    public async Task R04_RefundRates_MatchItemsPaidInThePeriod()
    {
        var since = _now.AddDays(-365);
        var paidItems = _data.Items.Where(item => _data.Orders[item.OrderIndex].PaidAt >= since).ToList();
        paidItems.ShouldContain(item => item.RefundedAt != null, "the demo seed has refunds in the last year");

        var bySeller = await ReportsAsync(reports => reports.RefundRatesBySellerAsync(since, CancellationToken));
        var expectedBySeller = paidItems.GroupBy(item => _data.Products[item.ProductIndex].SellerIndex).ToDictionary(
            group => _data.Sellers[group.Key].Id,
            group => (Sold: (long)group.Count(), Refunded: (long)group.Count(item => item.RefundedAt != null)));
        bySeller.Count.ShouldBe(expectedBySeller.Count);
        foreach (var row in bySeller)
        {
            var (sold, refunded) = expectedBySeller[row.SellerId];
            (row.UnitsSold, row.UnitsRefunded, row.RefundRate).ShouldBe((sold, refunded, Rate((int)refunded, (int)sold)));
        }

        bySeller.Select(row => row.RefundRate).ShouldBeInOrder(SortDirection.Descending);

        var byProduct = await ReportsAsync(reports => reports.RefundRatesByProductAsync(since, 2, 10, CancellationToken));
        var expectedRates = paidItems.GroupBy(item => item.ProductIndex)
            .Where(group => group.Count() >= 2)
            .Select(group => Rate(group.Count(item => item.RefundedAt != null), group.Count()))
            .OrderDescending()
            .Take(10);
        byProduct.Select(row => row.RefundRate).ShouldBe(expectedRates);
    }

    [Fact]
    public async Task R05_OrderFunnel_CountsEachStagePerDay()
    {
        var from = new DateTimeOffset(_now.UtcDateTime.Date, TimeSpan.Zero).AddDays(-29);
        var to = from.AddDays(30);

        var rows = await ReportsAsync(reports => reports.OrderFunnelAsync(from, to, CancellationToken));

        rows.Count.ShouldBe(30);
        for (var day = 0; day < 30; day++)
        {
            var start = from.AddDays(day);
            var orders = _data.Orders.Where(order => order.CreatedAt >= start && order.CreatedAt < start.AddDays(1)).ToList();
            rows[day].ShouldBe(new OrderFunnelRow(
                $"{start:yyyy-MM-dd}",
                orders.Count,
                orders.Count(order => order.HadFailedAttempt || order.PaidAt != null),
                orders.Count(order => order.PaidAt != null),
                orders.Count(order => order.Status is SeedOrderStatus.Refunded or SeedOrderStatus.PartiallyRefunded)));
        }

        rows.Sum(row => row.Created).ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task R06_OrderValuePercentiles_MatchLinearInterpolation()
    {
        var from = _now.AddDays(-730);
        var totals = _data.Orders.Where(order => order.PaidAt >= from && order.PaidAt < _now).Select(order => (double)order.Total).Order().ToList();

        var row = await ReportsAsync(reports => reports.OrderValuePercentilesAsync(from, _now, CancellationToken));

        row.Orders.ShouldBe(totals.Count);
        row.P50.ShouldBe(Percentile(totals, 0.50));
        row.P90.ShouldBe(Percentile(totals, 0.90));
        row.P99.ShouldBe(Percentile(totals, 0.99));
    }

    [Fact]
    public async Task R07_SellerSummary_MatchesTheLedgerAndCatalog()
    {
        var seller = BusiestSeller();
        var since = _now.AddDays(-30);

        var row = await ReportsAsync(reports => reports.SellerSummaryAsync(_data.Sellers[seller].Id, since, CancellationToken));

        var ledger = Ledger(seller).ToList();
        var recent = ledger.Where(entry => entry.At >= since).ToList();
        row.ShouldBe(new SellerSummaryRow(
            ledger.Sum(entry => entry.Amount),
            recent.Where(entry => entry.Type is "Sale" or "Refund").Sum(entry => entry.Amount),
            recent.Where(entry => entry.Type == "Sale").Select(entry => entry.Item.OrderIndex).Distinct().Count(),
            recent.Count(entry => entry.Type == "Refund"),
            _data.Products.Count(product => product.SellerIndex == seller && product.Status == "Published")));
    }

    [Fact]
    public async Task R08_TopSellers_RankByNetSales()
    {
        var since = _now.AddDays(-180);
        var expected = Enumerable.Range(0, _data.Sellers.Length)
            .Select(seller => Ledger(seller).Where(entry => entry.At >= since).ToList())
            .Select((entries, seller) => new TopSellerRow(
                _data.Sellers[seller].Id,
                _data.Sellers[seller].DisplayName,
                entries.Where(entry => entry.Type is "Sale" or "Refund").Sum(entry => entry.Amount),
                -entries.Where(entry => entry.Type is "PlatformFee" or "FeeRefund").Sum(entry => entry.Amount),
                entries.Count(entry => entry.Type == "Sale")))
            .Where(row => row.Units > 0)
            .OrderByDescending(row => row.NetSales)
            .Take(3)
            .ToList();

        var rows = await ReportsAsync(reports => reports.TopSellersAsync(since, 3, CancellationToken));

        rows.ShouldBe(expected);
    }

    [Fact]
    public async Task R09_RecentSales_PageThroughEverySaleNewestFirst()
    {
        var seller = BusiestSeller();
        var seen = new List<RecentSaleRow>();
        (DateTimeOffset, Guid)? cursor = null;
        while (true)
        {
            var after = cursor;
            var page = await ReportsAsync(reports => reports.SellerRecentSalesAsync(_data.Sellers[seller].Id, after, 7, CancellationToken));
            seen.AddRange(page);
            if (page.Count < 7)
            {
                break;
            }

            cursor = (page[^1].PaidAt, page[^1].EntryId);
        }

        var expected = Ledger(seller).Where(entry => entry.Type == "Sale").ToList();
        seen.Count.ShouldBe(expected.Count);
        seen.Select(row => row.OrderItemId).ShouldBe(expected.Select(entry => entry.Item.Id), ignoreOrder: true);
        seen.Select(row => row.PaidAt).ShouldBeInOrder(SortDirection.Descending);
    }

    private async Task<T> ReportsAsync<T>(Func<IReports, Task<T>> query)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<IReports>());
    }

    // The ledger entries the writer creates for a seller (section 9.6), derived from the generated items.
    private IEnumerable<(string Type, decimal Amount, DateTimeOffset At, SeedItem Item)> Ledger(int seller)
    {
        foreach (var item in _data.Items.Where(item => _data.Products[item.ProductIndex].SellerIndex == seller))
        {
            if (_data.Orders[item.OrderIndex].PaidAt is not { } paidAt)
            {
                continue;
            }

            yield return ("Sale", item.UnitPrice, paidAt, item);
            yield return ("PlatformFee", -item.PlatformFee, paidAt, item);
            if (item.RefundedAt is { } refundedAt)
            {
                yield return ("Refund", -item.UnitPrice, refundedAt, item);
                yield return ("FeeRefund", item.PlatformFee, refundedAt, item);
            }
        }
    }

    private int BusiestSeller() =>
        Enumerable.Range(0, _data.Sellers.Length).MaxBy(seller => Ledger(seller).Count());

    private IEnumerable<SeedItem> ActiveItemsPaidSince(DateTimeOffset since) =>
        _data.Items.Where(item => item.RefundedAt is null && _data.Orders[item.OrderIndex].PaidAt >= since);

    private static decimal Share(IGrouping<string, List<DateTimeOffset>> cohort, int days) =>
        Math.Round((decimal)cohort.Count(times => times.Count > 1 && times[1] <= times[0].AddDays(days)) / cohort.Count(), 4, MidpointRounding.AwayFromZero);

    private static decimal? Rate(int refunded, int sold) =>
        sold == 0 ? null : Math.Round((decimal)refunded / sold, 4, MidpointRounding.AwayFromZero);

    // percentile_cont: linear interpolation between the closest ranks, rounded to cents.
    private static decimal? Percentile(List<double> sorted, double fraction)
    {
        if (sorted.Count == 0)
        {
            return null;
        }

        var position = fraction * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Count - 1);
        var value = sorted[lower] + ((position - lower) * (sorted[upper] - sorted[lower]));
        return Math.Round((decimal)value, 2, MidpointRounding.AwayFromZero);
    }
}
