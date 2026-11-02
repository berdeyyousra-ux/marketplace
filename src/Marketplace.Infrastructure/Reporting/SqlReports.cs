using System.Collections.Concurrent;
using Marketplace.Application.Reporting;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Marketplace.Infrastructure.Reporting;

// Section 13.2. The SQL lives in docs/sql, the single copy, and is embedded at build time. Each query runs with
// named parameters through Database.SqlQueryRaw.
public sealed class SqlReports(AppDbContext db) : IReports
{
    public static readonly DateTimeOffset FirstPageTime = new(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static readonly ConcurrentDictionary<string, string> _sql = new(StringComparer.Ordinal);

    public Task<List<MonthlyRevenueRow>> SellerMonthlyRevenueAsync(Guid sellerId, DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken) =>
        QueryAsync<MonthlyRevenueRow>("R10-seller-monthly-revenue", cancellationToken, Uuid("seller_id", sellerId), Time("from", periodStart), Time("to", periodEnd));

    public Task<List<MonthlyRevenueRow>> SellerMonthlyRevenueFromLedgerAsync(Guid sellerId, DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken) =>
        QueryAsync<MonthlyRevenueRow>("R01-seller-monthly-revenue", cancellationToken, Uuid("seller_id", sellerId), Time("from", periodStart), Time("to", periodEnd));

    public Task<List<CategoryTopProductRow>> TopProductsPerCategoryAsync(DateTimeOffset since, int perCategory, CancellationToken cancellationToken) =>
        QueryAsync<CategoryTopProductRow>("R02-top-products-per-category", cancellationToken, Time("since", since), Int("per_category", perCategory));

    public Task<List<SellerTopProductRow>> SellerTopProductsAsync(Guid sellerId, DateTimeOffset since, int limit, CancellationToken cancellationToken) =>
        QueryAsync<SellerTopProductRow>("R02-seller-top-products", cancellationToken, Uuid("seller_id", sellerId), Time("since", since), Int("limit", limit));

    public Task<List<RepeatBuyerCohortRow>> RepeatBuyerCohortsAsync(DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken) =>
        QueryAsync<RepeatBuyerCohortRow>("R03-repeat-buyer-cohorts", cancellationToken, Time("from", periodStart), Time("to", periodEnd));

    public Task<List<SellerRefundRateRow>> RefundRatesBySellerAsync(DateTimeOffset since, CancellationToken cancellationToken) =>
        QueryAsync<SellerRefundRateRow>("R04-refund-rates-by-seller", cancellationToken, Time("since", since));

    public Task<List<ProductRefundRateRow>> RefundRatesByProductAsync(DateTimeOffset since, int minUnits, int limit, CancellationToken cancellationToken) =>
        QueryAsync<ProductRefundRateRow>("R04-refund-rates-by-product", cancellationToken, Time("since", since), Int("min_units", minUnits), Int("limit", limit));

    public Task<List<OrderFunnelRow>> OrderFunnelAsync(DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken) =>
        QueryAsync<OrderFunnelRow>("R05-order-funnel", cancellationToken, Time("from", periodStart), Time("to", periodEnd));

    public async Task<PlatformTotalsRow> PlatformTotalsAsync(DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken) =>
        (await QueryAsync<PlatformTotalsRow>("R05-overview-totals", cancellationToken, Time("from", periodStart), Time("to", periodEnd))).Single();

    public async Task<OrderValuePercentilesRow> OrderValuePercentilesAsync(DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken) =>
        (await QueryAsync<OrderValuePercentilesRow>("R06-order-value-percentiles", cancellationToken, Time("from", periodStart), Time("to", periodEnd))).Single();

    public async Task<SellerSummaryRow> SellerSummaryAsync(Guid sellerId, DateTimeOffset since, CancellationToken cancellationToken) =>
        (await QueryAsync<SellerSummaryRow>("R07-seller-dashboard-summary", cancellationToken, Uuid("seller_id", sellerId), Time("since", since))).Single();

    public Task<List<TopSellerRow>> TopSellersAsync(DateTimeOffset since, int limit, CancellationToken cancellationToken) =>
        QueryAsync<TopSellerRow>("R08-top-sellers", cancellationToken, Time("since", since), Int("limit", limit));

    public Task<List<RecentSaleRow>> SellerRecentSalesAsync(
        Guid sellerId, (DateTimeOffset PaidAt, Guid EntryId)? after, int limit, CancellationToken cancellationToken)
    {
        // The first page starts after a key larger than any real one.
        var (lastPaidAt, lastId) = after ?? (FirstPageTime, Guid.AllBitsSet);
        return QueryAsync<RecentSaleRow>(
            "R09-seller-recent-orders", cancellationToken, Uuid("seller_id", sellerId), Time("last_paid_at", lastPaidAt), Uuid("last_id", lastId),
            Int("limit", limit));
    }

    private Task<List<T>> QueryAsync<T>(string name, CancellationToken cancellationToken, params NpgsqlParameter[] parameters) =>
        db.Database.SqlQueryRaw<T>(Sql(name), parameters).ToListAsync(cancellationToken);

    private static string Sql(string name) => _sql.GetOrAdd(name, static key =>
    {
        using var stream = typeof(SqlReports).Assembly.GetManifestResourceStream($"Marketplace.Sql.{key}.sql")
            ?? throw new InvalidOperationException($"The report query {key}.sql is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    private static NpgsqlParameter Uuid(string name, Guid value) => new(name, NpgsqlDbType.Uuid) { Value = value };

    private static NpgsqlParameter Int(string name, int value) => new(name, NpgsqlDbType.Integer) { Value = value };

    private static NpgsqlParameter Time(string name, DateTimeOffset value) => new(name, NpgsqlDbType.TimestampTz) { Value = value.ToUniversalTime() };
}
