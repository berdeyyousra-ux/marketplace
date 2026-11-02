using Marketplace.Application.Abstractions;
using Marketplace.Application.Identity;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Application.Identity;

public sealed class RegisterCommandValidatorTests
{
    private readonly RegisterCommandValidator _validator;

    public RegisterCommandValidatorTests()
    {
        var commonPasswords = Substitute.For<ICommonPasswords>();
        commonPasswords.Contains("password1234").Returns(true);
        _validator = new RegisterCommandValidator(commonPasswords);
    }

    [Fact]
    public void Validate_ValidCommand_HasNoErrors()
    {
        var result = _validator.Validate(new RegisterCommand("buyer@example.test", "correct horse battery", "Buyer"));

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("not-an-email", "correct horse battery", "Buyer", "Email")]
    [InlineData("buyer@example.test", "short", "Buyer", "Password")]
    [InlineData("buyer@example.test", "password1234", "Buyer", "Password")]
    [InlineData("buyer@example.test", "correct horse battery", "B", "DisplayName")]
    public void Validate_InvalidField_ReportsThatField(string email, string password, string displayName, string field)
    {
        var result = _validator.Validate(new RegisterCommand(email, password, displayName));

        result.Errors.Select(error => error.PropertyName).ShouldContain(field);
    }

    [Fact]
    public void Validate_PasswordBoundaries_AcceptsTenAndRejects129()
    {
        _validator.Validate(new RegisterCommand("a@example.test", new string('x', 10), "Buyer")).IsValid.ShouldBeTrue();
        _validator.Validate(new RegisterCommand("a@example.test", new string('x', 128), "Buyer")).IsValid.ShouldBeTrue();
        _validator.Validate(new RegisterCommand("a@example.test", new string('x', 129), "Buyer")).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_TooLongEmail_IsRejected()
    {
        var email = new string('a', 250) + "@x.test";

        _validator.Validate(new RegisterCommand(email, "correct horse battery", "Buyer")).IsValid.ShouldBeFalse();
    }
}
