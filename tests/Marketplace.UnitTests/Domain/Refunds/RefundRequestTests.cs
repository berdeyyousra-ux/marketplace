using Marketplace.Domain.Common;
using Marketplace.Domain.Refunds;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Domain.Refunds;

public sealed class RefundRequestTests
{
    private static readonly DateTimeOffset _now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid _adminId = Guid.CreateVersion7();

    public static TheoryData<RefundRequestStatus, RefundRequestStatus> AllTransitions()
    {
        var data = new TheoryData<RefundRequestStatus, RefundRequestStatus>();
        foreach (var from in Enum.GetValues<RefundRequestStatus>())
        {
            foreach (var to in Enum.GetValues<RefundRequestStatus>().Where(status => status != RefundRequestStatus.Submitted))
            {
                data.Add(from, to);
            }
        }

        return data;
    }

    [Theory]
    [InlineData(RefundPolicyVerdict.IneligibleNotPaid)]
    [InlineData(RefundPolicyVerdict.IneligibleAlreadyRefunded)]
    public void Submit_IneligibleVerdict_ReturnsRefundNotAllowed(RefundPolicyVerdict verdict)
    {
        var result = RefundRequest.Submit(Guid.CreateVersion7(), Guid.CreateVersion7(), "The file is corrupted.", verdict, 0, _now);

        result.Error.Code.ShouldBe("refund_not_allowed");
    }

    [Theory]
    [InlineData("too short")]
    [InlineData("          ")]
    public void Submit_InvalidReason_ReturnsValidationFailed(string reason)
    {
        var result = RefundRequest.Submit(Guid.CreateVersion7(), Guid.CreateVersion7(), reason, RefundPolicyVerdict.Eligible, 0, _now);

        result.Error.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public void Approve_OutsideWindowWithoutOverride_ReturnsOverrideReasonRequired()
    {
        var request = Submitted(RefundPolicyVerdict.IneligibleOutsideWindow);

        var result = request.Approve(_adminId, "Goodwill.", " ", _now);

        result.Error.Code.ShouldBe("override_reason_required");
        request.Status.ShouldBe(RefundRequestStatus.Submitted);
    }

    [Fact]
    public void Approve_OutsideWindowWithOverride_RecordsDecision()
    {
        var request = Submitted(RefundPolicyVerdict.IneligibleOutsideWindow);

        request.Approve(_adminId, "Goodwill.", "Seller confirmed the file was broken.", _now).IsSuccess.ShouldBeTrue();

        request.Decision.ShouldBe(RefundDecision.Approved);
        request.DecidedBy.ShouldBe(_adminId);
        request.DecidedAt.ShouldBe(_now);
        request.OverrideReason.ShouldBe("Seller confirmed the file was broken.");
    }

    [Fact]
    public void Reject_BlankNote_ReturnsValidationFailed()
    {
        var request = Submitted(RefundPolicyVerdict.Eligible);

        request.Reject(_adminId, "", null, _now).Error.Code.ShouldBe("validation_failed");
    }

    [Fact]
    public void Fail_LongReason_IsShortened()
    {
        var request = InState(RefundRequestStatus.Executing);

        request.Fail(new string('x', 600), _now).IsSuccess.ShouldBeTrue();

        request.FailureReason!.Length.ShouldBe(500);
    }

    [Theory]
    [MemberData(nameof(AllTransitions))]
    public void RefundStateMachine_AllTransitions(RefundRequestStatus from, RefundRequestStatus to)
    {
        var request = InState(from);
        var legal = (from, to) is (RefundRequestStatus.Submitted, RefundRequestStatus.Approved)
            or (RefundRequestStatus.Submitted, RefundRequestStatus.Rejected)
            or (RefundRequestStatus.Approved, RefundRequestStatus.Executing)
            or (RefundRequestStatus.Executing, RefundRequestStatus.Completed)
            or (RefundRequestStatus.Executing, RefundRequestStatus.Failed)
            or (RefundRequestStatus.Failed, RefundRequestStatus.Executing);

        var result = Apply(request, to);

        result.IsSuccess.ShouldBe(legal);
        request.Status.ShouldBe(legal ? to : from);
        if (!legal)
        {
            var isDecision = to is RefundRequestStatus.Approved or RefundRequestStatus.Rejected;
            result.Error.Code.ShouldBe(isDecision ? "refund_already_decided" : "invalid_state_transition");
        }
    }

    private static Result Apply(RefundRequest request, RefundRequestStatus to) => to switch
    {
        RefundRequestStatus.Approved => request.Approve(_adminId, "Approved.", null, _now),
        RefundRequestStatus.Rejected => request.Reject(_adminId, "Rejected.", null, _now),
        RefundRequestStatus.Executing => request.StartExecution(_now),
        RefundRequestStatus.Completed => request.Complete(_now),
        RefundRequestStatus.Failed => request.Fail("Provider unavailable.", _now),
        _ => throw new ArgumentOutOfRangeException(nameof(to)),
    };

    private static RefundRequest Submitted(RefundPolicyVerdict verdict) =>
        RefundRequest.Submit(Guid.CreateVersion7(), Guid.CreateVersion7(), "The file is corrupted.", verdict, 0, _now).Value;

    private static RefundRequest InState(RefundRequestStatus status)
    {
        var request = Submitted(RefundPolicyVerdict.Eligible);
        RefundRequestStatus[] path = status switch
        {
            RefundRequestStatus.Submitted => [],
            RefundRequestStatus.Approved => [RefundRequestStatus.Approved],
            RefundRequestStatus.Rejected => [RefundRequestStatus.Rejected],
            RefundRequestStatus.Executing => [RefundRequestStatus.Approved, RefundRequestStatus.Executing],
            RefundRequestStatus.Completed => [RefundRequestStatus.Approved, RefundRequestStatus.Executing, RefundRequestStatus.Completed],
            RefundRequestStatus.Failed => [RefundRequestStatus.Approved, RefundRequestStatus.Executing, RefundRequestStatus.Failed],
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };

        foreach (var step in path)
        {
            Apply(request, step).IsSuccess.ShouldBeTrue();
        }

        return request;
    }

    [Fact]
    public void Reject_KeepsAnOverrideReason_WhenTheAdminGivesOne()
    {
        var request = Submitted(RefundPolicyVerdict.Eligible);

        request.Reject(_adminId, "Not eligible.", "The AI suggested approving, but the file was downloaded twice.", _now).IsSuccess.ShouldBeTrue();

        request.OverrideReason.ShouldBe("The AI suggested approving, but the file was downloaded twice.");
    }
}
