using Marketplace.Domain.Common;
using Marketplace.Domain.Payments;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Domain.Payments;

public sealed class PaymentTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    public static TheoryData<PaymentStatus, PaymentStatus> AllTransitions()
    {
        var data = new TheoryData<PaymentStatus, PaymentStatus>();
        foreach (var from in Enum.GetValues<PaymentStatus>())
        {
            foreach (var to in Enum.GetValues<PaymentStatus>().Where(status => status != PaymentStatus.RequiresPayment))
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Fact]
    public void Create_ValidDetails_RequiresPayment()
    {
        var payment = Payment.Create(Guid.CreateVersion7(), "Fake", "pi_123", Money.Eur(19.99m), _now).Value;

        payment.Status.ShouldBe(PaymentStatus.RequiresPayment);
        payment.Amount.ShouldBe(Money.Eur(19.99m));
        payment.CreatedAt.ShouldBe(_now);
    }

    [Theory]
    [InlineData("", "pi_123", "19.99")]
    [InlineData("Fake", "", "19.99")]
    [InlineData("Fake", "pi_123", "0")]
    public void Create_InvalidDetails_ReturnsValidationFailed(string provider, string providerPaymentId, string amount)
    {
        var result = Payment.Create(Guid.CreateVersion7(), provider, providerPaymentId, Money.Eur(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)), _now);

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public void Create_EmptyOrder_ReturnsValidationFailed()
    {
        var result = Payment.Create(Guid.Empty, "Fake", "pi_123", Money.Eur(1m), _now);

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public void MarkFailed_LongFailureCode_IsShortened()
    {
        var payment = InState(PaymentStatus.RequiresPayment);

        payment.MarkFailed(new string('x', 150), _now).IsSuccess.ShouldBeTrue();

        payment.FailureCode!.Length.ShouldBe(100);
    }

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void PaymentStateMachine_AllTransitions(PaymentStatus from, PaymentStatus to)
    {
        var payment = InState(from);
        var legal = (from, to) is (PaymentStatus.RequiresPayment, PaymentStatus.Succeeded)
            or (PaymentStatus.RequiresPayment, PaymentStatus.Failed)
            or (PaymentStatus.Succeeded, PaymentStatus.Refunded);

        var result = Apply(payment, to);

        result.IsSuccess.ShouldBe(legal);
        payment.Status.ShouldBe(legal ? to : from);
        if (!legal)
        {
            result.Error.Code.ShouldBe("invalid_state_transition");
        }
    }

    private static Result Apply(Payment payment, PaymentStatus to) => to switch
    {
        PaymentStatus.Succeeded => payment.MarkSucceeded(_now),
        PaymentStatus.Failed => payment.MarkFailed("card_declined", _now),
        PaymentStatus.Refunded => payment.MarkRefunded(_now),
        _ => throw new ArgumentOutOfRangeException(nameof(to)),
    };

    private static Payment InState(PaymentStatus status)
    {
        var payment = Payment.Create(Guid.CreateVersion7(), "Fake", "pi_123", Money.Eur(19.99m), _now).Value;
        PaymentStatus[] path = status switch
        {
            PaymentStatus.RequiresPayment => [],
            PaymentStatus.Succeeded => [PaymentStatus.Succeeded],
            PaymentStatus.Failed => [PaymentStatus.Failed],
            PaymentStatus.Refunded => [PaymentStatus.Succeeded, PaymentStatus.Refunded],
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

        foreach (var step in path)
        {
            Apply(payment, step).IsSuccess.ShouldBeTrue();
        }

        return payment;
    }
}
