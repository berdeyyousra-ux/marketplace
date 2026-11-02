using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Catalog;
using Marketplace.Application.Common;
using Marketplace.Domain.Common;

namespace Marketplace.Application.Reporting;

// EP-70 to EP-73 and US-16. Every query is for the caller's own seller profile; the Seller policy guarantees one.
public sealed record SellerSummaryQuery;

public sealed record SellerSummaryResponse(MoneyDto Balance, MoneyDto RevenueLast30Days, long OrdersLast30Days, long RefundsLast30Days, long ActiveProducts);

public sealed record RevenueByMonthQuery(int Months = 12);

public sealed record MonthlyRevenue(string Month, MoneyDto NetSales, MoneyDto PlatformFees, MoneyDto SellerNet, MoneyDto RunningSellerNet);

public sealed record SellerTopProductsQuery(int Days = 30, int Limit = 10);

public sealed record TopProduct(Guid ProductId, string Title, long Units, MoneyDto Gross);

public sealed record RecentOrdersQuery(int Limit = Paging.DefaultLimit, string? Cursor = null);

public sealed record RecentOrderLine(
    Guid OrderId, Guid OrderItemId, Guid ProductId, string ProductTitle, MoneyDto UnitPrice, MoneyDto SellerAmount, string Status, DateTimeOffset PaidAt);

// A list that is not paged.
public sealed record ItemList<T>(IReadOnlyList<T> Items);

public sealed record RecentSaleCursor(DateTimeOffset PaidAt, Guid Id);

public sealed class RevenueByMonthQueryValidator : AbstractValidator<RevenueByMonthQuery>
{
    public RevenueByMonthQueryValidator() => RuleFor(query => query.Months).InclusiveBetween(1, 24);
}

public sealed class SellerTopProductsQueryValidator : AbstractValidator<SellerTopProductsQuery>
{
    public SellerTopProductsQueryValidator()
    {
        RuleFor(query => query.Days).InclusiveBetween(1, 365);
        RuleFor(query => query.Limit).InclusiveBetween(1, 50);
    }
}

public sealed class RecentOrdersQueryValidator : AbstractValidator<RecentOrdersQuery>
{
    public RecentOrdersQueryValidator() => RuleFor(query => query.Limit).ValidLimit();
}

internal static class Eur
{
    public static MoneyDto Dto(decimal amount) => MoneyDto.From(Money.Eur(amount));
}

// R-07.
public sealed class SellerSummaryQueryHandler(IReports reports, ProductOwnership ownership, TimeProvider timeProvider)
    : IQueryHandler<SellerSummaryQuery, SellerSummaryResponse>
{
    public async Task<Result<SellerSummaryResponse>> HandleAsync(SellerSummaryQuery query, CancellationToken cancellationToken)
    {
        var sellerId = await ownership.CurrentSellerIdAsync(cancellationToken) ?? throw new InvalidOperationException("The Seller policy guarantees a seller profile.");
        var row = await reports.SellerSummaryAsync(sellerId, timeProvider.GetUtcNow().AddDays(-30), cancellationToken);
        return new SellerSummaryResponse(Eur.Dto(row.Balance), Eur.Dto(row.Revenue), row.Orders, row.Refunds, row.ActiveProducts);
    }
}

// R-01: the current month and the months before it, oldest first.
public sealed class RevenueByMonthQueryHandler(IReports reports, ProductOwnership ownership, TimeProvider timeProvider)
    : IQueryHandler<RevenueByMonthQuery, ItemList<MonthlyRevenue>>
{
    public async Task<Result<ItemList<MonthlyRevenue>>> HandleAsync(RevenueByMonthQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sellerId = await ownership.CurrentSellerIdAsync(cancellationToken) ?? throw new InvalidOperationException("The Seller policy guarantees a seller profile.");
        var now = timeProvider.GetUtcNow();
        var nextMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
        var rows = await reports.SellerMonthlyRevenueAsync(sellerId, nextMonth.AddMonths(-query.Months), nextMonth, cancellationToken);
        return new ItemList<MonthlyRevenue>(rows.ConvertAll(row => new MonthlyRevenue(
            row.Month, Eur.Dto(row.NetSales), Eur.Dto(row.PlatformFees), Eur.Dto(row.SellerNet), Eur.Dto(row.RunningSellerNet))));
    }
}

// R-02, seller form.
public sealed class SellerTopProductsQueryHandler(IReports reports, ProductOwnership ownership, TimeProvider timeProvider)
    : IQueryHandler<SellerTopProductsQuery, ItemList<TopProduct>>
{
    public async Task<Result<ItemList<TopProduct>>> HandleAsync(SellerTopProductsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var sellerId = await ownership.CurrentSellerIdAsync(cancellationToken) ?? throw new InvalidOperationException("The Seller policy guarantees a seller profile.");
        var rows = await reports.SellerTopProductsAsync(sellerId, timeProvider.GetUtcNow().AddDays(-query.Days), query.Limit, cancellationToken);
        return new ItemList<TopProduct>(rows.ConvertAll(row => new TopProduct(row.ProductId, row.Title, row.Units, Eur.Dto(row.Gross))));
    }
}

// R-09: newest sale first, keyset on the sale's (paid_at, ledger entry id).
public sealed class RecentOrdersQueryHandler(IReports reports, ProductOwnership ownership)
    : IQueryHandler<RecentOrdersQuery, Page<RecentOrderLine>>
{
    public async Task<Result<Page<RecentOrderLine>>> HandleAsync(RecentOrdersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Paging.TryDecode<RecentSaleCursor>(query.Cursor, out var cursor))
        {
            return Paging.InvalidCursor;
        }

        var sellerId = await ownership.CurrentSellerIdAsync(cancellationToken) ?? throw new InvalidOperationException("The Seller policy guarantees a seller profile.");
        var rows = await reports.SellerRecentSalesAsync(sellerId, cursor is null ? null : (cursor.PaidAt, cursor.Id), query.Limit + 1, cancellationToken);
        var page = Paging.ToPage(rows, query.Limit, row => new RecentSaleCursor(row.PaidAt, row.EntryId));
        return new Page<RecentOrderLine>(
            [.. page.Items.Select(row => new RecentOrderLine(
                row.OrderId, row.OrderItemId, row.ProductId, row.ProductTitle, Eur.Dto(row.UnitPrice), Eur.Dto(row.SellerAmount), row.Status, row.PaidAt))],
            page.NextCursor);
    }
}
