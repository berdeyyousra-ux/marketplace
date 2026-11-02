using Marketplace.Domain.Common;
using Marketplace.Domain.Orders;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Domain.Orders;

public sealed class OrderTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid _buyerId = Guid.CreateVersion7();

    public static TheoryData<OrderStatus, OrderStatus> AllTransitions()
    {
        var data = new TheoryData<OrderStatus, OrderStatus>();
        foreach (var from in Enum.GetValues<OrderStatus>())
        {
            foreach (var to in Enum.GetValues<OrderStatus>().Where(status => status != OrderStatus.Pending))
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Fact]
    public void Create_ValidLines_SnapshotsPricesFeesAndExpiry()
    {
        var order = Order.Create(_buyerId, [Line(19.99m), Line(0.50m)], _now, 30, 1000).Value;

        order.Status.ShouldBe(OrderStatus.Pending);
        order.Total.ShouldBe(Money.Eur(20.49m));
        order.ExpiresAt.ShouldBe(_now.AddMinutes(30));
        order.Items.Count.ShouldBe(2);
        order.Items[0].PlatformFee.ShouldBe(Money.Eur(2.00m));
        order.Items[0].SellerAmount.ShouldBe(Money.Eur(17.99m));
        order.Items[1].PlatformFee.ShouldBe(Money.Eur(0.05m));
        order.Items.ShouldAllBe(item => item.OrderId == order.Id && item.Status == OrderItemStatus.Active);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Create_WrongNumberOfLines_ReturnsValidationFailed(int count)
    {
        var lines = Enumerable.Range(0, count).Select(_ => Line(1m)).ToList();

        var result = Order.Create(_buyerId, lines, _now, 30, 1000);

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public void Create_TenLines_Succeeds()
    {
        var lines = Enumerable.Range(0, 10).Select(_ => Line(1m)).ToList();

        Order.Create(_buyerId, lines, _now, 30, 1000).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_DuplicateProduct_ReturnsValidationFailed()
    {
        var line = Line(1m);

        var result = Order.Create(_buyerId, [line, line with { }], _now, 30, 1000);

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public void Create_ZeroPrice_ReturnsValidationFailed()
    {
        var result = Order.Create(_buyerId, [Line(0m)], _now, 30, 1000);

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public void Expire_BeforeExpiry_ReturnsInvalidStateTransition()
    {
        var order = InState(OrderStatus.Pending, 1);

        var result = order.Expire(_now.AddMinutes(30));

        result.Error.Code.ShouldBe("invalid_state_transition");
        order.Status.ShouldBe(OrderStatus.Pending);
    }

    [Fact]
    public void MarkItemRefunded_UnknownItem_ReturnsNotFound()
    {
        var order = InState(OrderStatus.Paid, 1);

        var result = order.MarkItemRefunded(Guid.CreateVersion7(), _now);

        result.Error.Code.ShouldBe("not_found");
    }

    [Fact]
    public void MarkItemRefunded_AlreadyRefundedItem_ReturnsInvalidStateTransition()
    {
        var order = InState(OrderStatus.PartiallyRefunded, 2);
        var refunded = order.Items.Single(item => item.Status == OrderItemStatus.Refunded);

        var result = order.MarkItemRefunded(refunded.Id, _now);

        result.Error.Code.ShouldBe("invalid_state_transition");
    }

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void OrderStateMachine_AllTransitions(OrderStatus from, OrderStatus to)
    {
        // Paid to Refunded needs a single item; staying PartiallyRefunded needs a third item.
        var itemCount = (from, to) switch
        {
            (OrderStatus.Paid, OrderStatus.Refunded) => 1,
            (OrderStatus.PartiallyRefunded, OrderStatus.PartiallyRefunded) => 3,
            _ => 2,
        };
        var order = InState(from, itemCount);
        var legal = IsLegal(from, to);

        var result = to switch
        {
            OrderStatus.Paid => order.MarkPaid(_now),
            OrderStatus.Cancelled => order.Cancel(_now),
            OrderStatus.Expired => order.Expire(order.ExpiresAt.AddSeconds(1)),
            OrderStatus.PartiallyRefunded or OrderStatus.Refunded => order.MarkItemRefunded(FirstActiveItemId(order), _now),
            _ => throw new ArgumentOutOfRangeException(nameof(to)),
        };

        result.IsSuccess.ShouldBe(legal);
        if (legal)
        {
            order.Status.ShouldBe(to);
        }
        else
        {
            result.Error.Code.ShouldBe("invalid_state_transition");
            order.Status.ShouldBe(from);
        }
    }

    // Section 6.3, plus PartiallyRefunded staying PartiallyRefunded when a middle item of three is refunded.
    private static bool IsLegal(OrderStatus from, OrderStatus to) => (from, to) switch
    {
        (OrderStatus.Pending, OrderStatus.Paid) => true,
        (OrderStatus.Expired, OrderStatus.Paid) => true,
        (OrderStatus.Pending, OrderStatus.Cancelled) => true,
        (OrderStatus.Pending, OrderStatus.Expired) => true,
        (OrderStatus.Paid, OrderStatus.PartiallyRefunded) => true,
        (OrderStatus.Paid, OrderStatus.Refunded) => true,
        (OrderStatus.PartiallyRefunded, OrderStatus.Refunded) => true,
        (OrderStatus.PartiallyRefunded, OrderStatus.PartiallyRefunded) => true,
        _ => false,
    };

    private static Guid FirstActiveItemId(Order order) =>
        order.Items.FirstOrDefault(item => item.Status == OrderItemStatus.Active)?.Id ?? order.Items[0].Id;

    private static OrderLine Line(decimal price) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), "Notion Budget Template", Money.Eur(price));

    private static Order InState(OrderStatus status, int itemCount)
    {
        var lines = Enumerable.Range(0, itemCount).Select(_ => Line(19.99m)).ToList();
        var order = Order.Create(_buyerId, lines, _now, 30, 1000).Value;
        var afterExpiry = order.ExpiresAt.AddSeconds(1);

        bool Step(Result result) => result.IsSuccess;

        var reached = status switch
        {
            OrderStatus.Pending => true,
            OrderStatus.Paid => Step(order.MarkPaid(_now)),
            OrderStatus.Cancelled => Step(order.Cancel(_now)),
            OrderStatus.Expired => Step(order.Expire(afterExpiry)),
            OrderStatus.PartiallyRefunded => Step(order.MarkPaid(_now)) && Step(order.MarkItemRefunded(order.Items[0].Id, _now)),
            OrderStatus.Refunded => Step(order.MarkPaid(_now))
                && order.Items.All(item => Step(order.MarkItemRefunded(item.Id, _now))),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

        reached.ShouldBeTrue();
        order.Status.ShouldBe(status);
        return order;
    }
}
