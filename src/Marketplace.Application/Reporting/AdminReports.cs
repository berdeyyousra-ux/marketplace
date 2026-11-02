using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Domain.Common;

namespace Marketplace.Application.Reporting;

// EP-74 to EP-77 and US-33. Admin only.
public sealed record OverviewQuery(DateTimeOffset? From = null, DateTimeOffset? To = null)
{
    public static readonly TimeSpan DefaultPeriod = TimeSpan.FromDays(30);
    public static readonly TimeSpan MaxPeriod = TimeSpan.FromDays(366);
}

public sealed record OrderValue(long Orders, MoneyDto? P50, MoneyDto? P90, MoneyDto? P99);

public sealed record FunnelDay(string Day, long Created, long PaymentStarted, long Paid, long Refunded);

public sealed record OverviewResponse(
    DateTimeOffset From, DateTimeOffset To, MoneyDto NetSales, MoneyDto PlatformFees, long PaidOrders, decimal? RefundRate, OrderValue OrderValue,
    IReadOnlyList<FunnelDay> Funnel);

public sealed record TopSellersQuery(int Days = 30, int Limit = 10);

public sealed record TopSeller(Guid SellerId, string DisplayName, MoneyDto NetSales, MoneyDto PlatformFees, long Units);

public sealed record RefundRatesQuery(int Days = 90)
{
    public const int ProductMinUnits = 5;
    public const int ProductLimit = 20;
}

public sealed record SellerRefundRate(Guid SellerId, string DisplayName, long UnitsSold, long UnitsRefunded, decimal? RefundRate);

public sealed record ProductRefundRate(Guid ProductId, string Title, Guid SellerId, long UnitsSold, long UnitsRefunded, decimal? RefundRate);

public sealed record RefundRatesResponse(IReadOnlyList<SellerRefundRate> Sellers, IReadOnlyList<ProductRefundRate> Products);

public sealed record RepeatBuyersQuery(int Months = 12);

public sealed record RepeatBuyerCohort(string Cohort, long Buyers, decimal ThirtyDayRepeatShare, decimal SixtyDayRepeatShare, decimal NinetyDayRepeatShare);

public sealed class OverviewQueryValidator : AbstractValidator<OverviewQuery>
{
    public OverviewQueryValidator()
    {
        RuleFor(query => query).Must(query => query.From is null || query.To is null || query.From < query.To)
            .WithName("from").WithMessage("from must be before to.");
        RuleFor(query => query).Must(query => query.From is null || query.To is null || query.To - query.From <= OverviewQuery.MaxPeriod)
            .WithName("to").WithMessage("The period can be at most 366 days.");
    }
}

public sealed class TopSellersQueryValidator : AbstractValidator<TopSellersQuery>
{
    public TopSellersQueryValidator()
    {
        RuleFor(query => query.Days).InclusiveBetween(1, 365);
        RuleFor(query => query.Limit).InclusiveBetween(1, Paging.MaxLimit);
    }
}

public sealed class RefundRatesQueryValidator : AbstractValidator<RefundRatesQuery>
{
    public RefundRatesQueryValidator() => RuleFor(query => query.Days).InclusiveBetween(1, 365);
}

public sealed class RepeatBuyersQueryValidator : AbstractValidator<RepeatBuyersQuery>
{
    public RepeatBuyersQueryValidator() => RuleFor(query => query.Months).InclusiveBetween(1, 24);
}

// R-05, R-06 and the totals. By default the period is whole UTC days: the 30 days ending with today. With only
// one end given, the period is 30 days from or to it. A period longer than 366 days is refused above.
public sealed class OverviewQueryHandler(IReports reports, TimeProvider timeProvider) : IQueryHandler<OverviewQuery, OverviewResponse>
{
    public async Task<Result<OverviewResponse>> HandleAsync(OverviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var tomorrow = new DateTimeOffset(timeProvider.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero).AddDays(1);
        var to = query.To ?? (query.From is { } start ? start + OverviewQuery.DefaultPeriod : tomorrow);
        var from = query.From ?? to - OverviewQuery.DefaultPeriod;
        if (from >= to)
        {
            return DomainErrors.Validation("from must be before to.");
        }

        var totals = await reports.PlatformTotalsAsync(from, to, cancellationToken);
        var values = await reports.OrderValuePercentilesAsync(from, to, cancellationToken);
        var funnel = await reports.OrderFunnelAsync(from, to, cancellationToken);
        return new OverviewResponse(
            from, to, Eur.Dto(totals.NetSales), Eur.Dto(totals.PlatformFees), totals.PaidOrders, totals.RefundRate,
            new OrderValue(values.Orders, Optional(values.P50), Optional(values.P90), Optional(values.P99)),
            funnel.ConvertAll(day => new FunnelDay(day.Day, day.Created, day.PaymentStarted, day.Paid, day.Refunded)));
    }

    private static MoneyDto? Optional(decimal? amount) => amount is { } value ? Eur.Dto(value) : null;
}

// R-08.
public sealed class TopSellersQueryHandler(IReports reports, TimeProvider timeProvider) : IQueryHandler<TopSellersQuery, ItemList<TopSeller>>
{
    public async Task<Result<ItemList<TopSeller>>> HandleAsync(TopSellersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = await reports.TopSellersAsync(timeProvider.GetUtcNow().AddDays(-query.Days), query.Limit, cancellationToken);
        return new ItemList<TopSeller>(rows.ConvertAll(row => new TopSeller(row.SellerId, row.DisplayName, Eur.Dto(row.NetSales), Eur.Dto(row.PlatformFees), row.Units)));
    }
}

// R-04: every seller with sales in the period, and the 20 products with the highest refund rate among those with
// at least 5 units sold.
public sealed class RefundRatesQueryHandler(IReports reports, TimeProvider timeProvider) : IQueryHandler<RefundRatesQuery, RefundRatesResponse>
{
    public async Task<Result<RefundRatesResponse>> HandleAsync(RefundRatesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var since = timeProvider.GetUtcNow().AddDays(-query.Days);
        var sellers = await reports.RefundRatesBySellerAsync(since, cancellationToken);
        var products = await reports.RefundRatesByProductAsync(since, RefundRatesQuery.ProductMinUnits, RefundRatesQuery.ProductLimit, cancellationToken);
        return new RefundRatesResponse(
            sellers.ConvertAll(row => new SellerRefundRate(row.SellerId, row.DisplayName, row.UnitsSold, row.UnitsRefunded, row.RefundRate)),
            products.ConvertAll(row => new ProductRefundRate(row.ProductId, row.Title, row.SellerId, row.UnitsSold, row.UnitsRefunded, row.RefundRate)));
    }
}

// R-03: cohorts for the current month and the months before it.
public sealed class RepeatBuyersQueryHandler(IReports reports, TimeProvider timeProvider) : IQueryHandler<RepeatBuyersQuery, ItemList<RepeatBuyerCohort>>
{
    public async Task<Result<ItemList<RepeatBuyerCohort>>> HandleAsync(RepeatBuyersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = timeProvider.GetUtcNow();
        var nextMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
        var rows = await reports.RepeatBuyerCohortsAsync(nextMonth.AddMonths(-query.Months), nextMonth, cancellationToken);
        return new ItemList<RepeatBuyerCohort>(rows.ConvertAll(row => new RepeatBuyerCohort(
            row.Cohort, row.Buyers, row.ThirtyDayRepeatShare, row.SixtyDayRepeatShare, row.NinetyDayRepeatShare)));
    }
}
