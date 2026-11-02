using Marketplace.Domain.Common;

namespace Marketplace.Domain.Orders;

public sealed class OrderItem : Entity
{
    // The three amounts share one currency column in order_items (section 7.2), so they are stored as decimals.
    private decimal _unitPrice;
    private decimal _platformFee;
    private decimal _sellerAmount;

    internal OrderItem(Guid orderId, OrderLine line, Money platformFee, Money sellerAmount)
    {
        OrderId = orderId;
        ProductId = line.ProductId;
        SellerId = line.SellerId;
        ProductTitle = line.ProductTitle;
        Currency = line.UnitPrice.Currency;
        _unitPrice = line.UnitPrice.Amount;
        _platformFee = platformFee.Amount;
        _sellerAmount = sellerAmount.Amount;
        Status = OrderItemStatus.Active;
    }

    private OrderItem()
    {
        ProductTitle = string.Empty;
        Currency = Money.EuroCurrency;
    }

    public Guid OrderId { get; private set; }

    public Guid ProductId { get; private set; }

    public Guid SellerId { get; private set; }

    public string ProductTitle { get; private set; }

    public string Currency { get; private set; }

    public Money UnitPrice => new(_unitPrice, Currency);

    public Money PlatformFee => new(_platformFee, Currency);

    public Money SellerAmount => new(_sellerAmount, Currency);

    public OrderItemStatus Status { get; private set; }

    public DateTimeOffset? RefundedAt { get; private set; }

    internal void MarkRefunded(DateTimeOffset now)
    {
        Status = OrderItemStatus.Refunded;
        RefundedAt = now;
    }
}
