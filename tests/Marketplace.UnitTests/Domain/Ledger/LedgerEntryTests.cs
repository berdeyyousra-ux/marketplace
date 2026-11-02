using Marketplace.Domain.Common;
using Marketplace.Domain.Ledger;
using Marketplace.Domain.Orders;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Domain.Ledger;

public sealed class LedgerEntryTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LedgerEntries_SignConvention()
    {
        var item = PaidItem(19.99m, 1000);

        var sale = LedgerEntry.ForPaidItem(item, _now);
        var refund = LedgerEntry.ForRefundedItem(item, _now);

        sale.Select(entry => (entry.EntryType, entry.Amount.Amount)).ShouldBe(
            [(LedgerEntryType.Sale, 19.99m), (LedgerEntryType.PlatformFee, -2.00m)]);
        refund.Select(entry => (entry.EntryType, entry.Amount.Amount)).ShouldBe(
            [(LedgerEntryType.Refund, -19.99m), (LedgerEntryType.FeeRefund, 2.00m)]);
        sale.Concat(refund).ShouldAllBe(entry => entry.OrderItemId == item.Id && entry.SellerId == item.SellerId);
    }

    [Fact]
    public void LedgerBalance_MatchesExpected()
    {
        var item = PaidItem(19.99m, 1000);

        var afterSale = LedgerEntry.ForPaidItem(item, _now).Sum(entry => entry.Amount.Amount);
        var afterRefund = afterSale + LedgerEntry.ForRefundedItem(item, _now).Sum(entry => entry.Amount.Amount);

        afterSale.ShouldBe(17.99m);
        afterRefund.ShouldBe(0.00m);
    }

    [Fact]
    public void ForPaidItem_ZeroFee_SkipsPlatformFeeEntry()
    {
        var item = PaidItem(0.50m, 1);

        var entries = LedgerEntry.ForPaidItem(item, _now).Concat(LedgerEntry.ForRefundedItem(item, _now)).ToList();

        entries.Select(entry => entry.EntryType).ShouldBe([LedgerEntryType.Sale, LedgerEntryType.Refund]);
        entries.Sum(entry => entry.Amount.Amount).ShouldBe(0.00m);
    }

    [Theory]
    [InlineData(LedgerEntryType.Sale, "-1")]
    [InlineData(LedgerEntryType.Sale, "0")]
    [InlineData(LedgerEntryType.FeeRefund, "-1")]
    [InlineData(LedgerEntryType.PlatformFee, "1")]
    [InlineData(LedgerEntryType.PlatformFee, "0")]
    [InlineData(LedgerEntryType.Refund, "1")]
    public void Create_WrongSignOrZero_ReturnsValidationFailed(LedgerEntryType type, string amount)
    {
        var result = LedgerEntry.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), type, Money.Eur(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)), _now);

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public void Create_PayoutWithoutOrderItem_Succeeds()
    {
        var result = LedgerEntry.Create(Guid.CreateVersion7(), null, LedgerEntryType.Payout, Money.Eur(-10m), _now);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_SaleWithoutOrderItem_ReturnsValidationFailed()
    {
        var result = LedgerEntry.Create(Guid.CreateVersion7(), null, LedgerEntryType.Sale, Money.Eur(10m), _now);

        result.Error.Code.ShouldBe("validation_failed");
    }

    private static OrderItem PaidItem(decimal price, int basisPoints)
    {
        var line = new OrderLine(Guid.CreateVersion7(), Guid.CreateVersion7(), "Notion Budget Template", Money.Eur(price));
        return Order.Create(Guid.CreateVersion7(), [line], _now, 30, basisPoints).Value.Items[0];
    }
}
