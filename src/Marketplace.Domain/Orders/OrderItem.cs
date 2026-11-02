using Marketplace.Domain.Common;

namespace Marketplace.Domain.Orders;

public sealed class OrderItem : Entity
{
    internal OrderItem(Guid orderId, OrderLine line, Money platformFee, Money sellerAmount)
    {
        OrderId = orderId;
        ProductId = line.ProductId;
        SellerId = line.SellerId;
        ProductTitle = line.ProductTitle;
        UnitPrice = line.UnitPrice;
        PlatformFee = platformFee;
        SellerAmount = sellerAmount;
        Status = OrderItemStatus.Active;
    }

    public Guid OrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid SellerId { get; private set; }

    public string ProductTitle { get; private set; }

    public Money UnitPrice { get; private set; }

    public Money PlatformFee { get; private set; }

    public Money SellerAmount { get; private set; }

    public OrderItemStatus Status { get; private set; }

    public DateTimeOffset? RefundedAt { get; private set; }

    internal void MarkRefunded(DateTimeOffset now)
    {
        Status = OrderItemStatus.Refunded;
        RefundedAt = now;
    }
}
