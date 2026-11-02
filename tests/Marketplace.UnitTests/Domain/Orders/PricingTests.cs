using System.Globalization;
using Marketplace.Domain.Common;
using Marketplace.Domain.Orders;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Domain.Orders;

public sealed class PricingTests
{
    // The first two rows are the mandatory worked examples from section 6.4.
    [Theory]
    [InlineData("19.99", 1000, "2.00", "17.99")]
    [InlineData("0.50", 1000, "0.05", "0.45")]
    [InlineData("10000.00", 1000, "1000.00", "9000.00")]
    [InlineData("0.50", 1, "0.00", "0.50")]
    [InlineData("10000.00", 1, "1.00", "9999.00")]
    [InlineData("19.99", 0, "0.00", "19.99")]
    [InlineData("19.99", 10000, "19.99", "0.00")]
    [InlineData("0.50", 100, "0.01", "0.49")]
    public void PricingAndCommission_WorkedExamples(string price, int basisPoints, string fee, string sellerAmount)
    {
        var (platformFee, seller) = Pricing.Split(Money.Eur(Parse(price)), basisPoints);

        platformFee.ShouldBe(Money.Eur(Parse(fee)));
        seller.ShouldBe(Money.Eur(Parse(sellerAmount)));
        (platformFee + seller).ShouldBe(Money.Eur(Parse(price)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10001)]
    public void Split_BasisPointsOutOfRange_ThrowsArgumentOutOfRangeException(int basisPoints)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Pricing.Split(Money.Eur(1m), basisPoints));
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
