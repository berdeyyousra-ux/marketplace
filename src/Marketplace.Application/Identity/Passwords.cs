using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Domain.Common;
using Marketplace.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Identity;

// EP-06 to EP-08 and US-06 to US-08.
public sealed record ForgotPasswordCommand(string Email);

public sealed record ResetPasswordCommand(string Token, string NewPassword);

// The caller comes from ICurrentUser, never from the request body.
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword);

public sealed class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator() => RuleFor(command => command.Email).NotEmpty().MaximumLength(256);
}

public sealed class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator(ICommonPasswords commonPasswords)
    {
        RuleFor(command => command.Token).NotEmpty().MaximumLength(100);
        RuleFor(command => command.NewPassword).SetValidator(new PasswordRules(commonPasswords));
    }
}

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator(ICommonPasswords commonPasswords)
    {
        RuleFor(command => command.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(command => command.NewPassword).SetValidator(new PasswordRules(commonPasswords));
    }
}

internal static class Sessions
{
    public static Task<int> RevokeAllAsync(IAppDbContext db, Guid userId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(token => token.RevokedAt, now), cancellationToken);
}

// Always 202 (US-06). The reset email and its 30-minute token come from the outbox handler.
public sealed class ForgotPasswordCommandHandler(IIdentityService identity, IAppDbContext db, IOutbox outbox)
    : ICommandHandler<ForgotPasswordCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = await identity.FindUserIdByEmailAsync(command.Email, cancellationToken);
        if (userId is not null)
        {
            outbox.Enqueue(OutboxMessageTypes.PasswordResetRequested, new UserMessage(userId.Value));
            await db.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}

// Section 9.1: in one transaction, set the password, rotate the security stamp, reset the failure count,
// revoke every refresh token, mark the token used, and audit.
public sealed class ResetPasswordCommandHandler(IAppDbContext db, IIdentityService identity, AuditTrail audit, TimeProvider timeProvider)
    : ICommandHandler<ResetPasswordCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow();
        var hash = SecureTokens.Hash(command.Token);
        var token = await db.OneTimeTokens.AsNoTracking()
            .Where(candidate => candidate.TokenHash == hash && candidate.Purpose == OneTimeTokenPurpose.PasswordReset)
            .Select(candidate => new { candidate.Id, candidate.UserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (token is null)
        {
            return TokenErrors.InvalidToken;
        }

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var claimed = await db.OneTimeTokens
            .Where(candidate => candidate.Id == token.Id && candidate.UsedAt == null && candidate.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.UsedAt, now), cancellationToken);
        if (claimed != 1)
        {
            return TokenErrors.InvalidToken;
        }

        var set = await identity.SetPasswordAsync(token.UserId, command.NewPassword, cancellationToken);
        if (set.IsFailure)
        {
            return set.Error;
        }

        await Sessions.RevokeAllAsync(db, token.UserId, now, cancellationToken);
        audit.Record("auth.password_reset", AuditActorType.User, token.UserId, "User", token.UserId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed class ChangePasswordCommandHandler(
    IAppDbContext db, IIdentityService identity, ICurrentUser currentUser, AuditTrail audit, TimeProvider timeProvider)
    : ICommandHandler<ChangePasswordCommand, Unit>
{
    public static readonly Error WrongCurrentPassword =
        new("validation_failed", "The current password is incorrect.", ErrorType.Validation);

    public async Task<Result<Unit>> HandleAsync(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = currentUser.UserId ?? throw new InvalidOperationException("ChangePassword requires an authenticated caller.");
        if (!await identity.CheckPasswordAsync(userId, command.CurrentPassword, cancellationToken))
        {
            return WrongCurrentPassword;
        }

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);
        var set = await identity.SetPasswordAsync(userId, command.NewPassword, cancellationToken);
        if (set.IsFailure)
        {
            return set.Error;
        }

        await Sessions.RevokeAllAsync(db, userId, timeProvider.GetUtcNow(), cancellationToken);
        audit.Record("auth.password_changed", AuditActorType.User, userId, "User", userId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Unit.Value;
    }
}
