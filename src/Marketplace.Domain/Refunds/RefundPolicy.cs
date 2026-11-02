using Marketplace.Domain.Orders;

namespace Marketplace.Domain.Refunds;

public enum RefundPolicyVerdict
{
    IneligibleNotPaid,
    IneligibleAlreadyRefunded,
    IneligibleOutsideWindow,
    EligibleDownloaded,
    Eligible,
}

// Section 6.5: deterministic and evaluated without AI.
public static class RefundPolicy
{
    public static RefundPolicyVerdict Evaluate(
        OrderItem item, Order order, int downloadCount, bool requestExists, DateTimeOffset now, int windowDays)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentOutOfRangeException.ThrowIfNegative(downloadCount);
        ArgumentOutOfRangeException.ThrowIfNegative(windowDays);

        if (order.Status is not (OrderStatus.Paid or OrderStatus.PartiallyRefunded) || order.PaidAt is null)
        {
            return RefundPolicyVerdict.IneligibleNotPaid;
        }

        if (item.Status == OrderItemStatus.Refunded || requestExists)
        {
            return RefundPolicyVerdict.IneligibleAlreadyRefunded;
        }

        if (now - order.PaidAt.Value > TimeSpan.FromDays(windowDays))
        {
            return RefundPolicyVerdict.IneligibleOutsideWindow;
        }

        return downloadCount > 0 ? RefundPolicyVerdict.EligibleDownloaded : RefundPolicyVerdict.Eligible;
    }

    // Verdicts that may be stored on a request; the other two refuse it with refund_not_allowed.
    public static bool AllowsRequest(RefundPolicyVerdict verdict) =>
        verdict is RefundPolicyVerdict.Eligible or RefundPolicyVerdict.EligibleDownloaded or RefundPolicyVerdict.IneligibleOutsideWindow;
}
