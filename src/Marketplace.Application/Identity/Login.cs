using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Domain.Common;
using Marketplace.Domain.Identity;

namespace Marketplace.Application.Identity;

// EP-03 and US-03.
public sealed record LoginCommand(string Email, string Password);

public sealed record TokenPair(string AccessToken, int ExpiresInSeconds, string RefreshToken, string TokenType);

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(command => command.Email).NotEmpty().MaximumLength(256);
        RuleFor(command => command.Password).NotEmpty().MaximumLength(128);
    }
}

public static class AuthErrors
{
    public static readonly Error InvalidCredentials =
        new("invalid_credentials", "The email or password is incorrect.", ErrorType.Unauthorized);

    public static readonly Error EmailNotVerified =
        new("email_not_verified", "Confirm your email address before signing in.", ErrorType.Forbidden);

    public static readonly Error LockedOut =
        new("locked_out", "Too many failed sign-ins. Try again later.", ErrorType.Locked);
}

// Issues the access token and starts a new refresh-token family. Shared with refresh (part 5).
public sealed class SessionIssuer(IAppDbContext db, IAccessTokenIssuer accessTokens, IIdentityService identity, IClientContext client, TimeProvider timeProvider)
{
    public static readonly TimeSpan RefreshLifetime = TimeSpan.FromDays(14);

    public async Task<TokenPair> IssueAsync(Guid userId, string email, string displayName, Guid familyId, CancellationToken cancellationToken)
    {
        var roles = await identity.GetRolesAsync(userId, cancellationToken);
        var access = accessTokens.Issue(new AccessTokenSubject(userId, email, displayName, roles));
        var now = timeProvider.GetUtcNow();
        var (raw, hash) = SecureTokens.Generate();
        db.RefreshTokens.Add(RefreshToken.Issue(userId, familyId, hash, now, RefreshLifetime, client.IpAddress, client.UserAgent));

        return new TokenPair(access.Token, (int)(access.ExpiresAt - now).TotalSeconds, raw, "Bearer");
    }
}

// Section 9.1: the same 401 for an unknown email and a wrong password, a dummy hash for unknown emails,
// 403 only after a correct password, and a 15-minute lockout after 5 failures.
public sealed class LoginCommandHandler(
    IIdentityService identity, SessionIssuer sessions, AuditTrail audit, IAppDbContext db, TimeProvider timeProvider)
    : ICommandHandler<LoginCommand, TokenPair>
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<Result<TokenPair>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow();
        var account = await identity.FindForLoginAsync(command.Email, cancellationToken);
        if (account is null)
        {
            identity.SpendPasswordHashTime(command.Password);
            await FailAsync(null, "unknown_account", cancellationToken);
            return AuthErrors.InvalidCredentials;
        }

        if (account.LockoutEnd > now)
        {
            await FailAsync(account.UserId, "locked_out", cancellationToken);
            return AuthErrors.LockedOut;
        }

        if (!await identity.CheckPasswordAsync(account.UserId, command.Password, cancellationToken))
        {
            var lockedNow = await identity.RecordFailedSignInAsync(account.UserId, MaxFailures, now + LockoutDuration, cancellationToken);
            if (lockedNow)
            {
                audit.Record("auth.locked_out", AuditActorType.User, account.UserId, "User", account.UserId);
            }

            await FailAsync(account.UserId, "wrong_password", cancellationToken);
            return AuthErrors.InvalidCredentials;
        }

        if (!account.EmailConfirmed)
        {
            return AuthErrors.EmailNotVerified;
        }

        await identity.ResetFailedSignInsAsync(account.UserId, cancellationToken);
        var tokens = await sessions.IssueAsync(account.UserId, account.Email, account.DisplayName, Guid.CreateVersion7(), cancellationToken);
        audit.Record("auth.login_succeeded", AuditActorType.User, account.UserId, "User", account.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return tokens;
    }

    // Details hold only the reason class, never the email or password (section 9.1).
    private async Task FailAsync(Guid? userId, string reason, CancellationToken cancellationToken)
    {
        audit.Record("auth.login_failed", AuditActorType.User, userId, userId is null ? null : "User", userId, new { reason });
        await db.SaveChangesAsync(cancellationToken);
    }
}
