using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Identity;

// EP-04, EP-05, US-04, US-05 and FM-15.
public sealed record RefreshCommand(string RefreshToken);

public sealed record LogoutCommand(string RefreshToken);

public sealed class RefreshCommandValidator : AbstractValidator<RefreshCommand>
{
    public RefreshCommandValidator() => RuleFor(command => command.RefreshToken).NotEmpty().MaximumLength(100);
}

public sealed class LogoutCommandValidator : AbstractValidator<LogoutCommand>
{
    public LogoutCommandValidator() => RuleFor(command => command.RefreshToken).NotEmpty().MaximumLength(100);
}

public static class RefreshErrors
{
    public static readonly Error InvalidRefreshToken =
        new("invalid_refresh_token", "The refresh token is invalid, expired or revoked. Sign in again.", ErrorType.Unauthorized);

    public static readonly Error TokenReuseDetected =
        new("token_reuse_detected", "This refresh token was already used. All sessions from it were signed out.", ErrorType.Unauthorized);
}

internal static class RefreshFamilies
{
    public static Task<int> RevokeAsync(IAppDbContext db, Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(token => token.RevokedAt, now), cancellationToken);
}

// Section 9.1: rotate on every use. A used token presented again revokes its whole family, with no grace period.
public sealed class RefreshCommandHandler(
    IAppDbContext db, IIdentityService identity, SessionIssuer sessions, AuditTrail audit, TimeProvider timeProvider)
    : ICommandHandler<RefreshCommand, TokenPair>
{
    public async Task<Result<TokenPair>> HandleAsync(RefreshCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow();
        var hash = SecureTokens.Hash(command.RefreshToken);
        var token = await db.RefreshTokens.AsNoTracking()
            .Where(candidate => candidate.TokenHash == hash)
            .Select(candidate => new { candidate.Id, candidate.UserId, candidate.FamilyId, candidate.ExpiresAt, candidate.UsedAt, candidate.RevokedAt })
            .SingleOrDefaultAsync(cancellationToken);

        if (token is null || token.RevokedAt is not null || token.ExpiresAt <= now)
        {
            return RefreshErrors.InvalidRefreshToken;
        }

        if (token.UsedAt is not null)
        {
            return await ReuseDetectedAsync(token.UserId, token.FamilyId, now, cancellationToken);
        }

        var account = await identity.GetAccountAsync(token.UserId, cancellationToken);
        if (account is null)
        {
            return RefreshErrors.InvalidRefreshToken;
        }

        await using var transaction = await db.BeginTransactionAsync(cancellationToken);

        // Only one request can mark the token used; a concurrent second request is treated as reuse.
        var claimed = await db.RefreshTokens
            .Where(candidate => candidate.Id == token.Id && candidate.UsedAt == null && candidate.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.UsedAt, now), cancellationToken);
        if (claimed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await ReuseDetectedAsync(token.UserId, token.FamilyId, now, cancellationToken);
        }

        var (tokens, successorId) = await sessions.IssueAsync(account.UserId, account.Email, account.DisplayName, token.FamilyId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await db.RefreshTokens
            .Where(candidate => candidate.Id == token.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(candidate => candidate.ReplacedById, successorId), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return tokens;
    }

    private async Task<Result<TokenPair>> ReuseDetectedAsync(Guid userId, Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await RefreshFamilies.RevokeAsync(db, familyId, now, cancellationToken);
        audit.Record("auth.token_reuse_detected", AuditActorType.User, userId, "User", userId, new { familyId });
        await db.SaveChangesAsync(cancellationToken);
        return RefreshErrors.TokenReuseDetected;
    }
}

// Always succeeds (EP-05): logging out an unknown or already-revoked token reveals nothing.
public sealed class LogoutCommandHandler(IAppDbContext db, TimeProvider timeProvider) : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var hash = SecureTokens.Hash(command.RefreshToken);
        var familyId = await db.RefreshTokens.AsNoTracking()
            .Where(token => token.TokenHash == hash)
            .Select(token => (Guid?)token.FamilyId)
            .SingleOrDefaultAsync(cancellationToken);
        if (familyId is not null)
        {
            await RefreshFamilies.RevokeAsync(db, familyId.Value, timeProvider.GetUtcNow(), cancellationToken);
        }

        return Unit.Value;
    }
}
