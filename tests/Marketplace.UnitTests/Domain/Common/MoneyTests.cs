using System.Globalization;
using Marketplace.Domain.Common;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Domain.Common;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("1.005", "1.01")]
    [InlineData("2.675", "2.68")]
    [InlineData("-1.005", "-1.01")]
    [InlineData("19.994", "19.99")]
    [InlineData("19.99", "19.99")]
    public void Constructor_MoreThanTwoDecimals_RoundsAwayFromZero(string input, string expected)
    {
        var money = new Money(decimal.Parse(input, CultureInfo.InvariantCulture), "EUR");

        money.Amount.ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("eur")]
    [InlineData("EU")]
    [InlineData("EURO")]
    [InlineData("E1R")]
    [InlineData("")]
    [InlineData(null)]
    public void Constructor_InvalidCurrency_ThrowsArgumentException(string? currency)
    {
        Should.Throw<ArgumentException>(() => new Money(1m, currency!));
    }

    [Fact]
    public void Eur_Amount_UsesEuroCurrency()
    {
        var money = Money.Eur(19.99m);

        money.ShouldBe(new Money(19.99m, "EUR"));
    }

    [Fact]
    public void Add_SameCurrency_ReturnsSum()
    {
        var sum = Money.Eur(19.99m) + Money.Eur(0.01m);

        sum.ShouldBe(Money.Eur(20.00m));
    }

    [Fact]
    public void Subtract_SameCurrency_ReturnsDifference()
    {
        var difference = Money.Eur(19.99m) - Money.Eur(2.00m);

        difference.ShouldBe(Money.Eur(17.99m));
    }

    [Fact]
    public void Subtract_LargerAmount_ReturnsNegative()
    {
        var difference = Money.Eur(2.00m) - Money.Eur(19.99m);

        difference.ShouldBe(Money.Eur(-17.99m));
    }

    [Fact]
    public void Add_DifferentCurrencies_ThrowsInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(() => Money.Eur(1m) + new Money(1m, "USD"));
    }

    [Fact]
    public void Subtract_DifferentCurrencies_ThrowsInvalidOperationException()
    {
        Should.Throw<InvalidOperationException>(() => Money.Eur(1m) - new Money(1m, "USD"));
    }

    [Fact]
    public void Equals_SameValueWithDifferentScale_AreEqual()
    {
        var left = Money.Eur(19.9m);
        var right = Money.Eur(19.90m);

        left.ShouldBe(right);
    }
}
