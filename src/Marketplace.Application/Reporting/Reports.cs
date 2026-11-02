namespace Marketplace.Application.Reporting;

// Section 13.2. Rows are named for the SQL columns (snake_case) in docs/sql. Money is EUR (section 6.4).
public sealed record MonthlyRevenueRow(string Month, decimal NetSales, decimal PlatformFees, decimal SellerNet, decimal RunningSellerNet);

public sealed record CategoryTopProductRow(int CategoryId, Guid ProductId, string Title, long Units, decimal Gross, long Rank);

public sealed record SellerTopProductRow(Guid ProductId, string Title, long Units, decimal Gross, long Rank);

public sealed record RepeatBuyerCohortRow(string Cohort, long Buyers, decimal ThirtyDayRepeatShare, decimal SixtyDayRepeatShare, decimal NinetyDayRepeatShare);

public sealed record SellerRefundRateRow(Guid SellerId, string DisplayName, long UnitsSold, long UnitsRefunded, decimal? RefundRate);

public sealed record ProductRefundRateRow(Guid ProductId, string Title, Guid SellerId, long UnitsSold, long UnitsRefunded, decimal? RefundRate);

public sealed record OrderFunnelRow(string Day, long Created, long PaymentStarted, long Paid, long Refunded);

public sealed record OrderValuePercentilesRow(long Orders, decimal? P50, decimal? P90, decimal? P99);

public sealed record PlatformTotalsRow(decimal NetSales, decimal PlatformFees, long PaidOrders, decimal? RefundRate);

public sealed record SellerSummaryRow(decimal Balance, decimal Revenue, long Orders, long Refunds, long ActiveProducts);

public sealed record TopSellerRow(Guid SellerId, string DisplayName, decimal NetSales, decimal PlatformFees, long Units);

public sealed record RecentSaleRow(
    Guid EntryId, DateTimeOffset PaidAt, Guid OrderId, Guid OrderItemId, Guid ProductId, string ProductTitle, decimal UnitPrice, decimal SellerAmount,
    string Status);

// The Reporting module (section 4.4): raw SQL that reads everything and writes nothing. Times are passed in, so
// callers take them from TimeProvider (R-07).
public interface IReports
{
    // R-01. Months in [periodStart, periodEnd), one row per month.
    Task<List<MonthlyRevenueRow>> SellerMonthlyRevenueAsync(Guid sellerId, DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken);

    // R-02.
    Task<List<CategoryTopProductRow>> TopProductsPerCategoryAsync(DateTimeOffset since, int perCategory, CancellationToken cancellationToken);

    // R-02, seller form.
    Task<List<SellerTopProductRow>> SellerTopProductsAsync(Guid sellerId, DateTimeOffset since, int limit, CancellationToken cancellationToken);

    // R-03.
    Task<List<RepeatBuyerCohortRow>> RepeatBuyerCohortsAsync(DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken);

    // R-04.
    Task<List<SellerRefundRateRow>> RefundRatesBySellerAsync(DateTimeOffset since, CancellationToken cancellationToken);

    Task<List<ProductRefundRateRow>> RefundRatesByProductAsync(DateTimeOffset since, int minUnits, int limit, CancellationToken cancellationToken);

    // R-05. Days in [periodStart, periodEnd), one row per UTC day.
    Task<List<OrderFunnelRow>> OrderFunnelAsync(DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken);

    // EP-74 totals.
    Task<PlatformTotalsRow> PlatformTotalsAsync(DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken);

    // R-06.
    Task<OrderValuePercentilesRow> OrderValuePercentilesAsync(DateTimeOffset periodStart, DateTimeOffset periodEnd, CancellationToken cancellationToken);

    // R-07.
    Task<SellerSummaryRow> SellerSummaryAsync(Guid sellerId, DateTimeOffset since, CancellationToken cancellationToken);

    // R-08.
    Task<List<TopSellerRow>> TopSellersAsync(DateTimeOffset since, int limit, CancellationToken cancellationToken);

    // R-09. Pass null for the first page.
    Task<List<RecentSaleRow>> SellerRecentSalesAsync(Guid sellerId, (DateTimeOffset PaidAt, Guid EntryId)? after, int limit, CancellationToken cancellationToken);
}
