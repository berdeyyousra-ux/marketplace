using Marketplace.Application.Common;
using Marketplace.Domain.Common;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Application.Common;

public sealed class ErrorStatusCodesTests
{
    [Theory]
    [InlineData(ErrorType.Validation, 422)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.PreconditionFailed, 412)]
    [InlineData(ErrorType.Locked, 423)]
    [InlineData(ErrorType.TooManyRequests, 429)]
    [InlineData(ErrorType.Failure, 500)]
    [InlineData(ErrorType.Unavailable, 503)]
    public void For_EachErrorType_ReturnsSection61Status(ErrorType type, int expected)
    {
        var status = ErrorStatusCodes.For(type);

        status.ShouldBe(expected);
    }

    [Fact]
    public void For_EveryDefinedErrorType_HasAStatus()
    {
        var types = Enum.GetValues<ErrorType>();

        types.ShouldAllBe(type => ErrorStatusCodes.For(type) >= 400);
    }

    [Fact]
    public void For_UndefinedErrorType_ThrowsArgumentOutOfRangeException()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ErrorStatusCodes.For((ErrorType)99));
    }
}
