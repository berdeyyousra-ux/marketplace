using Marketplace.Domain.Common;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Domain.Common;

public sealed class ResultTests
{
    private static readonly Error _conflict = new("already_owned", "Already owned.", ErrorType.Conflict);

    [Fact]
    public void Success_NoValue_IsSuccess()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
    }

    [Fact]
    public void Failure_WithError_ExposesError()
    {
        var result = Result.Failure(_conflict);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(_conflict);
    }

    [Fact]
    public void Error_OnSuccess_ThrowsInvalidOperationException()
    {
        var result = Result.Success();

        Should.Throw<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Success_WithValue_ExposesValue()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Value_OnFailure_ThrowsInvalidOperationException()
    {
        var result = Result.Failure<int>(_conflict);

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Failure_NullError_ThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => Result.Failure(null!));
        Should.Throw<ArgumentNullException>(() => Result.Failure<int>(null!));
    }

    [Fact]
    public void ImplicitConversion_FromValue_IsSuccess()
    {
        Result<string> result = "done";

        result.Value.ShouldBe("done");
    }

    [Fact]
    public void ImplicitConversion_FromError_IsFailure()
    {
        Result<string> typed = _conflict;
        Result untyped = _conflict;

        typed.Error.ShouldBe(_conflict);
        untyped.Error.ShouldBe(_conflict);
    }
}
