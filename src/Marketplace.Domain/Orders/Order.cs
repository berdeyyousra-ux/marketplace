using Marketplace.Domain.Common;

namespace Marketplace.Domain.Orders;

public sealed class Order : AggregateRoot
{
    public const int MaxItems = 10;
    public const int ProductTitleMaxLength = 120;

    private readonly List<OrderItem> _items = [];

    private Order(Guid buyerId, Money total, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        BuyerId = buyerId;
        Status = OrderStatus.Pending;
        Total = total;
        CreatedAt = now;
        UpdatedAt = now;
        ExpiresAt = expiresAt;
    }

    // Used by EF Core when materializing from the database.
    private Order()
    {
    }

    public Guid BuyerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public Money Total { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public DateTimeOffset? ExpiredAt { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;

    // Mapped to the PostgreSQL xmin system column as an optimistic concurrency token (D-15).
    public uint Version { get; private set; }

    public static Result<Order> Create(
        Guid buyerId, IReadOnlyList<OrderLine> lines, DateTimeOffset now, int expiryMinutes, int commissionBasisPoints)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expiryMinutes);

        if (buyerId == Guid.Empty)
        {
            return DomainErrors.Validation("Buyer is required.");
        }

        if (lines.Count is 0 or > MaxItems)
        {
            return DomainErrors.Validation($"An order needs 1 to {MaxItems} products.");
        }

        if (lines.Select(line => line.ProductId).Distinct().Count() != lines.Count)
        {
            return DomainErrors.Validation("An order cannot contain the same product twice.");
        }

        if (lines.Any(line => line.UnitPrice.Amount <= 0))
        {
            return DomainErrors.Validation("Every product price must be greater than zero.");
        }

        if (lines.Any(line => string.IsNullOrWhiteSpace(line.ProductTitle) || line.ProductTitle.Length > ProductTitleMaxLength))
        {
            return DomainErrors.Validation($"Product titles must be 1 to {ProductTitleMaxLength} characters.");
        }

        var total = lines.Skip(1).Aggregate(lines[0].UnitPrice, (sum, line) => sum + line.UnitPrice);
        var order = new Order(buyerId, total, now, now.AddMinutes(expiryMinutes));

        foreach (var line in lines)
        {
            var (platformFee, sellerAmount) = Pricing.Split(line.UnitPrice, commissionBasisPoints);
            order._items.Add(new OrderItem(order.Id, line, platformFee, sellerAmount));
        }

        return order;
    }

    // Pending or Expired to Paid: the payment is the source of truth, even after expiry (section 6.3).
    public Result MarkPaid(DateTimeOffset now)
    {
        if (Status is not (OrderStatus.Pending or OrderStatus.Expired))
        {
            return Transition(OrderStatus.Paid);
        }

        Status = OrderStatus.Paid;
        PaidAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Cancel(DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending)
        {
            return Transition(OrderStatus.Cancelled);
        }

        Status = OrderStatus.Cancelled;
        CancelledAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Expire(DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending || now <= ExpiresAt)
        {
            return Transition(OrderStatus.Expired);
        }

        Status = OrderStatus.Expired;
        ExpiredAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result MarkItemRefunded(Guid orderItemId, DateTimeOffset now)
    {
        if (Status is not (OrderStatus.Paid or OrderStatus.PartiallyRefunded))
        {
            return DomainErrors.InvalidState($"Items of a {Status} order cannot be refunded.");
        }

        var item = _items.Find(candidate => candidate.Id == orderItemId);
        if (item is null)
        {
            return new Error("not_found", "Order item not found.", ErrorType.NotFound);
        }

        if (item.Status == OrderItemStatus.Refunded)
        {
            return DomainErrors.InvalidState("This order item is already refunded.");
        }

        item.MarkRefunded(now);
        Status = _items.TrueForAll(candidate => candidate.Status == OrderItemStatus.Refunded)
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;
        UpdatedAt = now;
        return Result.Success();
    }

    private Error Transition(OrderStatus to) => DomainErrors.InvalidStateTransition(nameof(Order), Status, to);
}
