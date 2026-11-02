namespace Marketplace.Domain.Orders;

public enum OrderStatus
{
    Pending,
    Paid,
    PartiallyRefunded,
    Refunded,
    Cancelled,
    Expired,
}

public enum OrderItemStatus
{
    Active,
    Refunded,
}
