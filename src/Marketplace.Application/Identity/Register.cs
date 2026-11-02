using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Domain.Common;

namespace Marketplace.Application.Identity;

// EP-01 and US-01.
public sealed record RegisterCommand(string Email, string Password, string DisplayName);

public sealed class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    public RegisterCommandValidator(ICommonPasswords commonPasswords)
    {
        ArgumentNullException.ThrowIfNull(commonPasswords);

        RuleFor(command => command.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(command => command.DisplayName).NotEmpty().Length(2, 80);
        RuleFor(command => command.Password).SetValidator(new PasswordRules(commonPasswords));
    }
}

// Section 11.1: 10 to 128 characters, no composition rules, not a common password. These run before any
// account lookup, so a weak password gets the same answer whether or not the email is registered.
public sealed class PasswordRules : AbstractValidator<string>
{
    public PasswordRules(ICommonPasswords commonPasswords)
    {
        ArgumentNullException.ThrowIfNull(commonPasswords);

        RuleFor(password => password)
            .NotEmpty()
            .Length(10, 128)
            .Must(password => !commonPasswords.Contains(password)).WithMessage("This password is too common. Choose a different one.");
    }
}

// Always succeeds with 202 (US-01). A new email gets an unconfirmed account and a verification email;
// an existing email gets a notice email and no change. Both paths take about the same time.
public sealed class RegisterCommandHandler(IIdentityService identity, IAppDbContext db, IOutbox outbox, TimeProvider timeProvider)
    : ICommandHandler<RegisterCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(RegisterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existingId = await identity.FindUserIdByEmailAsync(command.Email, cancellationToken);
        if (existingId is not null)
        {
            identity.SpendPasswordHashTime(command.Password);
            outbox.Enqueue(OutboxMessageTypes.AccountExistsNotice, new UserMessage(existingId.Value));
            await db.SaveChangesAsync(cancellationToken);
            return Unit.Value;
        }

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var created = await identity.CreateUserAsync(command.Email, command.Password, command.DisplayName, timeProvider.GetUtcNow(), cancellationToken);
        if (created.IsFailure)
        {
            // A concurrent registration took the email first. The answer stays 202 (US-01).
            return created.Error.Type == ErrorType.Conflict ? Unit.Value : created.Error;
        }

        outbox.Enqueue(OutboxMessageTypes.EmailVerificationRequested, new UserMessage(created.Value));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Unit.Value;
    }
}
