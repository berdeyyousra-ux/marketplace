using Marketplace.Domain.Common;

namespace Marketplace.Application.Abstractions;

// Users live in Infrastructure (D-43); handlers reach them only through this interface, by Guid.
public interface IIdentityService
{
    Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken);

    // Creates an unconfirmed user. Fails with a Conflict error if the email is already taken.
    Task<Result<Guid>> CreateUserAsync(string email, string password, string displayName, DateTimeOffset now, CancellationToken cancellationToken);

    // Spends the same time as a real password hash, so responses do not reveal whether an account exists (section 9.1).
    void SpendPasswordHashTime(string password);
}

// Section 11.1: the embedded list of the 10,000 most common passwords.
public interface ICommonPasswords
{
    bool Contains(string password);
}
