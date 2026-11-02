using Marketplace.Domain.Common;

namespace Marketplace.Domain.Orders;

public static class Pricing
{
    public const int MaxBasisPoints = 10000;

    // Section 6.4: fee = Round(unit price * basis points / 10000, 2, AwayFromZero); seller amount = unit price - fee.
    public static (Money PlatformFee, Money SellerAmount) Split(Money unitPrice, int commissionBasisPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(commissionBasisPoints);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(commissionBasisPoints, MaxBasisPoints);

        var fee = Math.Round(unitPrice.Amount * commissionBasisPoints / MaxBasisPoints, 2, MidpointRounding.AwayFromZero);
        var platformFee = new Money(fee, unitPrice.Currency);
        return (platformFee, unitPrice - platformFee);
    }
}
