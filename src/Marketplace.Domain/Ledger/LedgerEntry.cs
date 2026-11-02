using Marketplace.Domain.Common;
using Marketplace.Domain.Orders;

namespace Marketplace.Domain.Ledger;

public enum LedgerEntryType
{
    Sale,
    PlatformFee,
    Refund,
    FeeRefund,
    Payout,
}

// Immutable once created. Signs are fixed by entry type (section 7.2); zero amounts are never written.
public sealed class LedgerEntry : Entity
{
    private LedgerEntry(Guid sellerId, Guid? orderItemId, LedgerEntryType entryType, Money amount, DateTimeOffset now)
    {
        SellerId = sellerId;
        OrderItemId = orderItemId;
        EntryType = entryType;
        Amount = amount;
        CreatedAt = now;
    }

    public Guid SellerId { get; }

    public Guid? OrderItemId { get; }

    public LedgerEntryType EntryType { get; }

    public Money Amount { get; }

    public DateTimeOffset CreatedAt { get; }

    public static Result<LedgerEntry> Create(
        Guid sellerId, Guid? orderItemId, LedgerEntryType entryType, Money amount, DateTimeOffset now)
    {
        if (sellerId == Guid.Empty)
        {
            return DomainErrors.Validation("Seller is required.");
        }

        if ((entryType == LedgerEntryType.Payout) != (orderItemId is null))
        {
            return DomainErrors.Validation("Payout entries have no order item; every other entry needs one.");
        }

        var signIsValid = entryType is LedgerEntryType.Sale or LedgerEntryType.FeeRefund
            ? amount.Amount > 0
            : amount.Amount < 0;

        return signIsValid
            ? new LedgerEntry(sellerId, orderItemId, entryType, amount, now)
            : DomainErrors.Validation($"{entryType} entries must have a {(amount.Amount == 0 ? "non-zero" : "correctly signed")} amount.");
    }

    // Section 9.6: Sale +unit price, and PlatformFee -fee only when the fee is non-zero.
    public static IReadOnlyList<LedgerEntry> ForPaidItem(OrderItem item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Build(item, now, (LedgerEntryType.Sale, item.UnitPrice), (LedgerEntryType.PlatformFee, Negate(item.PlatformFee)));
    }

    // Section 9.8: Refund -unit price, and FeeRefund +fee only when the fee is non-zero.
    public static IReadOnlyList<LedgerEntry> ForRefundedItem(OrderItem item, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);
        return Build(item, now, (LedgerEntryType.Refund, Negate(item.UnitPrice)), (LedgerEntryType.FeeRefund, item.PlatformFee));
    }

    private static List<LedgerEntry> Build(OrderItem item, DateTimeOffset now, params (LedgerEntryType Type, Money Amount)[] entries) =>
        entries
            .Where(entry => entry.Amount.Amount != 0)
            .Select(entry => Create(item.SellerId, item.Id, entry.Type, entry.Amount, now).Value)
            .ToList();

    private static Money Negate(Money money) => new(-money.Amount, money.Currency);
}
