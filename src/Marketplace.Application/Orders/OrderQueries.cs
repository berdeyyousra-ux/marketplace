using System.Text.Json;
using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Application.Payments;
using Marketplace.Domain.Common;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Orders;

// EP-31, EP-32, EP-34, US-25 and US-27.
public sealed record MyOrdersQuery(string? Status = null, int Limit = Paging.DefaultLimit, string? Cursor = null);

public sealed record OrderQuery(Guid OrderId);

public sealed record CancelOrderCommand(Guid OrderId);

public sealed record OrderSummary(Guid Id, string Status, MoneyDto Total, int ItemCount, DateTimeOffset CreatedAt, DateTimeOffset? PaidAt);

public sealed record PaymentSummary(Guid Id, string Provider, string Status, MoneyDto Amount, DateTimeOffset CreatedAt);

public sealed record OrderDetail(
    Guid Id, string Status, MoneyDto Total, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? PaidAt,
    IReadOnlyList<OrderItemResponse> Items, PaymentSummary? Payment);

public sealed class MyOrdersQueryValidator : AbstractValidator<MyOrdersQuery>
{
    public MyOrdersQueryValidator()
    {
        RuleFor(query => query.Limit).ValidLimit();
        RuleFor(query => query.Status).Must(status => status is null || Enum.TryParse<OrderStatus>(status, ignoreCase: false, out _))
            .WithMessage("Status must be an order status, such as Pending or Paid.");
    }
}

// Own orders only, newest first, keyset on (created_at, id), which ix_orders_buyer_id_created_at serves.
public sealed class MyOrdersQueryHandler(IAppDbContext db, ICurrentUser currentUser) : IQueryHandler<MyOrdersQuery, Page<OrderSummary>>
{
    public async Task<Result<Page<OrderSummary>>> HandleAsync(MyOrdersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!Paging.TryDecode<Catalog.CreatedCursor>(query.Cursor, out var cursor))
        {
            return Paging.InvalidCursor;
        }

        var buyerId = currentUser.UserId ?? throw new InvalidOperationException("Listing orders requires an authenticated caller.");
        var orders = db.Orders.AsNoTracking().Where(order => order.BuyerId == buyerId);
        if (query.Status is not null)
        {
            var status = Enum.Parse<OrderStatus>(query.Status);
            orders = orders.Where(order => order.Status == status);
        }

        if (cursor is not null)
        {
            orders = orders.Where(order => order.CreatedAt < cursor.CreatedAt || (order.CreatedAt == cursor.CreatedAt && order.Id.CompareTo(cursor.Id) < 0));
        }

        var rows = await orders
            .OrderByDescending(order => order.CreatedAt).ThenByDescending(order => order.Id)
            .Take(query.Limit + 1)
            .Select(order => new { order.Id, order.Status, order.Total, ItemCount = order.Items.Count, order.CreatedAt, order.PaidAt })
            .ToListAsync(cancellationToken);

        var summaries = rows.ConvertAll(row => new OrderSummary(row.Id, row.Status.ToString(), MoneyDto.From(row.Total), row.ItemCount, row.CreatedAt, row.PaidAt));
        return Paging.ToPage(summaries, query.Limit, summary => new Catalog.CreatedCursor(summary.CreatedAt, summary.Id));
    }
}

// The buyer or an admin. Anyone else gets 404 (section 8.1).
public sealed class OrderQueryHandler(IAppDbContext db, ICurrentUser currentUser, IPublicImageStorage images) : IQueryHandler<OrderQuery, OrderDetail>
{
    public async Task<Result<OrderDetail>> HandleAsync(OrderQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var order = await db.Orders.AsNoTracking().Include(candidate => candidate.Items)
            .SingleOrDefaultAsync(candidate => candidate.Id == query.OrderId, cancellationToken);
        if (order is null || (order.BuyerId != currentUser.UserId && !currentUser.IsInRole(Roles.Admin)))
        {
            return PaymentErrors.OrderNotFound;
        }

        var payment = await db.Payments.AsNoTracking()
            .Where(candidate => candidate.OrderId == order.Id)
            .OrderByDescending(candidate => candidate.CreatedAt).ThenByDescending(candidate => candidate.Id)
            .Select(candidate => new { candidate.Id, candidate.Provider, candidate.Status, candidate.Amount, candidate.CreatedAt })
            .FirstOrDefaultAsync(cancellationToken);

        var response = OrderMapping.ToResponse(order, await Catalog.CoverUrls.ForProductsAsync(db, images, order.Items.Select(item => item.ProductId), cancellationToken));
        return new OrderDetail(
            response.Id, response.Status, response.Total, response.CreatedAt, response.ExpiresAt, response.PaidAt, response.Items,
            payment is null ? null : new PaymentSummary(payment.Id, payment.Provider, payment.Status.ToString(), MoneyDto.From(payment.Amount), payment.CreatedAt));
    }
}

// US-27: only Pending orders can be cancelled. A live payment is cancelled at the provider through the outbox,
// and a payment that succeeds anyway is refunded by the FM-07 path.
public sealed class CancelOrderCommandHandler(IAppDbContext db, ICurrentUser currentUser, IOutbox outbox, AuditTrail audit, TimeProvider timeProvider)
    : ICommandHandler<CancelOrderCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var buyerId = currentUser.UserId ?? throw new InvalidOperationException("Cancelling requires an authenticated caller.");
        var order = await db.Orders.SingleOrDefaultAsync(candidate => candidate.Id == command.OrderId && candidate.BuyerId == buyerId, cancellationToken);
        if (order is null)
        {
            return PaymentErrors.OrderNotFound;
        }

        var cancelled = order.Cancel(timeProvider.GetUtcNow());
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        var livePayments = await db.Payments
            .Where(payment => payment.OrderId == order.Id && payment.Status == PaymentStatus.RequiresPayment)
            .Select(payment => payment.Id)
            .ToListAsync(cancellationToken);
        foreach (var paymentId in livePayments)
        {
            outbox.Enqueue(OutboxMessageTypes.CancelPaymentIntent, new PaymentMessage(paymentId));
        }

        audit.Record("order.cancelled", AuditActorType.User, buyerId, "Order", order.Id);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Paid or expired at the same moment: the state no longer allows a cancel.
            return DomainErrors.InvalidState("The order changed while it was being cancelled.");
        }

        return Unit.Value;
    }
}

// Section 9.10: cancel at the provider; a payment that already succeeded is left to the webhook path.
public sealed class CancelPaymentIntentHandler(IAppDbContext db, IPaymentProvider provider, TimeProvider timeProvider) : IOutboxHandler
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public string MessageType => OutboxMessageTypes.CancelPaymentIntent;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var paymentId = (JsonSerializer.Deserialize<PaymentMessage>(payload, _json) ?? throw new JsonException("Missing paymentId.")).PaymentId;
        var payment = await db.Payments.SingleOrDefaultAsync(candidate => candidate.Id == paymentId, cancellationToken);
        if (payment is null || payment.Status != PaymentStatus.RequiresPayment)
        {
            return;
        }

        await provider.CancelPaymentAsync(payment.ProviderPaymentId, cancellationToken);
        var current = await provider.GetPaymentAsync(payment.ProviderPaymentId, cancellationToken);
        if (current.Status is ProviderPaymentStatus.Canceled or ProviderPaymentStatus.Failed && payment.MarkFailed("canceled_with_order", timeProvider.GetUtcNow()).IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
