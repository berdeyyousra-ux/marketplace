using Marketplace.Application.Common;
using Marketplace.Domain.Common;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Application.Common;

public sealed class DatabaseErrorsTests
{
    [Theory]
    [InlineData("ux_seller_profiles_user_id", "already_seller")]
    [InlineData("ux_download_grants_active_buyer_product", "already_owned")]
    [InlineData("ux_refund_requests_order_item_id", "refund_not_allowed")]
    [InlineData("ux_some_other_constraint", "concurrency_conflict")]
    [InlineData(null, "concurrency_conflict")]
    public void FromPostgres_UniqueViolation_MapsToConflict(string? constraint, string expectedCode)
    {
        var error = DatabaseErrors.FromPostgres("23505", constraint);

        error.ShouldNotBeNull();
        error.Code.ShouldBe(expectedCode);
        error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Theory]
    [InlineData("ux_seller_profiles_user_id")]
    [InlineData("ux_some_other_constraint")]
    public void FromPostgres_UniqueViolation_NeverExposesConstraintName(string constraint)
    {
        var error = DatabaseErrors.FromPostgres("23505", constraint)!;

        error.Message.ShouldNotContain(constraint);
        error.Code.ShouldNotContain(constraint);
    }

    [Theory]
    [InlineData("23503")]
    [InlineData("23514")]
    [InlineData("40001")]
    [InlineData(null)]
    public void FromPostgres_OtherFailures_ReturnNull(string? sqlState)
    {
        DatabaseErrors.FromPostgres(sqlState, "anything").ShouldBeNull();
    }

    [Theory]
    [InlineData("23503", true)]
    [InlineData("23514", true)]
    [InlineData("23505", false)]
    public void IsBug_ForeignKeyAndCheckViolations_AreBugs(string sqlState, bool expected)
    {
        DatabaseErrors.IsBug(sqlState).ShouldBe(expected);
    }
}
