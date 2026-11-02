using Marketplace.Domain.Common;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Refunds;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Domain.Refunds;

public sealed class RefundPolicyTests
{
    private static readonly DateTimeOffset _paidAt = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Expired)]
    [InlineData(OrderStatus.Refunded)]
    public void Evaluate_OrderNotPaid_IsIneligibleNotPaid(OrderStatus status)
    {
        var order = OrderIn(status);

        var verdict = RefundPolicy.Evaluate(order.Items[0], order, 0, false, _paidAt, 14);

        verdict.ShouldBe(RefundPolicyVerdict.IneligibleNotPaid);
    }

    [Fact]
    public void Evaluate_ItemAlreadyRefunded_IsIneligibleAlreadyRefunded()
    {
        var order = OrderIn(OrderStatus.PartiallyRefunded);
        var refunded = order.Items.Single(item => item.Status == OrderItemStatus.Refunded);

        RefundPolicy.Evaluate(refunded, order, 0, false, _paidAt, 14).ShouldBe(RefundPolicyVerdict.IneligibleAlreadyRefunded);
    }

    [Fact]
    public void Evaluate_RequestAlreadyExists_IsIneligibleAlreadyRefunded()
    {
        var order = OrderIn(OrderStatus.Paid);

        RefundPolicy.Evaluate(order.Items[0], order, 0, true, _paidAt, 14).ShouldBe(RefundPolicyVerdict.IneligibleAlreadyRefunded);
    }

    [Theory]
    [InlineData(0, RefundPolicyVerdict.Eligible)]
    [InlineData(1, RefundPolicyVerdict.EligibleDownloaded)]
    public void Evaluate_WithinWindow_DependsOnDownloads(int downloads, RefundPolicyVerdict expected)
    {
        var order = OrderIn(OrderStatus.Paid);

        RefundPolicy.Evaluate(order.Items[0], order, downloads, false, _paidAt.AddDays(14), 14).ShouldBe(expected);
    }

    [Fact]
    public void Evaluate_JustOutsideWindow_IsIneligibleOutsideWindow()
    {
        var order = OrderIn(OrderStatus.Paid);

        var verdict = RefundPolicy.Evaluate(order.Items[0], order, 0, false, _paidAt.AddDays(14).AddTicks(1), 14);

        verdict.ShouldBe(RefundPolicyVerdict.IneligibleOutsideWindow);
    }

    [Fact]
    public void Evaluate_ActiveItemOfPartiallyRefundedOrder_IsEligible()
    {
        var order = OrderIn(OrderStatus.PartiallyRefunded);
        var active = order.Items.Single(item => item.Status == OrderItemStatus.Active);

        RefundPolicy.Evaluate(active, order, 0, false, _paidAt, 14).ShouldBe(RefundPolicyVerdict.Eligible);
    }

    private static Order OrderIn(OrderStatus status)
    {
        var lines = Enumerable.Range(0, status == OrderStatus.Refunded ? 1 : 2)
            .Select(_ => new OrderLine(Guid.CreateVersion7(), Guid.CreateVersion7(), "Template", Money.Eur(9.99m)))
            .ToList();
        var order = Order.Create(Guid.CreateVersion7(), lines, _paidAt.AddMinutes(-5), 30, 1000).Value;

        var ok = status switch
        {
            OrderStatus.Pending => true,
            OrderStatus.Cancelled => order.Cancel(_paidAt).IsSuccess,
            OrderStatus.Expired => order.Expire(order.ExpiresAt.AddSeconds(1)).IsSuccess,
            OrderStatus.Paid => order.MarkPaid(_paidAt).IsSuccess,
            OrderStatus.PartiallyRefunded or OrderStatus.Refunded =>
                order.MarkPaid(_paidAt).IsSuccess && order.MarkItemRefunded(order.Items[0].Id, _paidAt).IsSuccess,
            _ => false,
        };

        ok.ShouldBeTrue();
        order.Status.ShouldBe(status);
        return order;
    }
}
