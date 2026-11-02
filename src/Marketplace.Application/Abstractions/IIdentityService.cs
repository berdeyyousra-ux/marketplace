using Marketplace.Domain.Common;

namespace Marketplace.Application.Abstractions;

// Users live in Infrastructure (D-43); handlers reach them only through this interface, by Guid.
public interface IIdentityService
{
    Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken);

    // Creates an unconfirmed user. Fails with a Conflict error if the email is already taken.
    Task<Result<Guid>> CreateUserAsync(string email, string password, string displayName, DateTimeOffset now, CancellationToken cancellationToken);

    Task<AccountSummary?> GetAccountAsync(Guid userId, CancellationToken cancellationToken);

    Task ConfirmEmailAsync(Guid userId, CancellationToken cancellationToken);

    Task<LoginAccount?> FindForLoginAsync(string email, CancellationToken cancellationToken);

    Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken);

    // Counts a failed sign-in. On the fifth failure the account is locked until lockoutUntil and the count resets.
    // Returns true when this failure locked the account.
    Task<bool> RecordFailedSignInAsync(Guid userId, int maxFailures, DateTimeOffset lockoutUntil, CancellationToken cancellationToken);

    Task ResetFailedSignInsAsync(Guid userId, CancellationToken cancellationToken);

    // Replaces the password (validators run), rotates the security stamp and clears the failure count and lockout.
    Task<Result<Unit>> SetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> GetRolesAsync(Guid userId, CancellationToken cancellationToken);

    // Spends the same time as a real password hash, so responses do not reveal whether an account exists (section 9.1).
    void SpendPasswordHashTime(string password);
}

public sealed record LoginAccount(Guid UserId, string Email, string DisplayName, bool EmailConfirmed, DateTimeOffset? LockoutEnd);

public sealed record AccountSummary(Guid UserId, string Email, string DisplayName, bool EmailConfirmed);

// D-27: email goes through SMTP. Only outbox handlers send email, never request handlers (section 9.1).
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed record EmailMessage(string To, string Subject, string TextBody);

// Section 11.1: the embedded list of the 10,000 most common passwords.
public interface ICommonPasswords
{
    bool Contains(string password);
}
