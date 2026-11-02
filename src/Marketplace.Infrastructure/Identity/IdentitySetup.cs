using System.Reflection;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marketplace.Infrastructure.Identity;

public static class IdentitySetup
{
    // D-16 and section 11.1: 10 to 128 characters with no composition rules, no common passwords,
    // PBKDF2 with at least 210,000 iterations, and a 15-minute lockout after 5 failures.
    public static IServiceCollection AddMarketplaceIdentity(this IServiceCollection services)
    {
        services.AddIdentityCore<AppUser>(options =>
            {
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = string.Empty;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddPasswordValidator<SectionElevenPasswordValidator>();

        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 210_000);
        services.AddSingleton<ICommonPasswords, EmbeddedCommonPasswords>();
        services.AddScoped<IIdentityService, IdentityService>();
        return services;
    }
}

public sealed class EmbeddedCommonPasswords : ICommonPasswords
{
    private const string _resourceName = "Marketplace.Infrastructure.Identity.Resources.common-passwords.txt";

    private static readonly Lazy<HashSet<string>> _passwords = new(Load);

    public static int Count => _passwords.Value.Count;

    public bool Contains(string password) => password is not null && _passwords.Value.Contains(password);

    private static HashSet<string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(_resourceName)
            ?? throw new InvalidOperationException($"Embedded resource {_resourceName} is missing.");
        using var reader = new StreamReader(stream);
        var passwords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                passwords.Add(line);
            }
        }

        return passwords;
    }
}

// Applies the section 11.1 rules inside Identity too, so every path that sets a password enforces them.
public sealed class SectionElevenPasswordValidator(ICommonPasswords commonPasswords) : IPasswordValidator<AppUser>
{
    public const int MaxLength = 128;

    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password)
    {
        if (password is { Length: > MaxLength })
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "PasswordTooLong", Description = $"Passwords can be at most {MaxLength} characters." }));
        }

        return Task.FromResult(password is not null && commonPasswords.Contains(password)
            ? IdentityResult.Failed(new IdentityError { Code = "PasswordTooCommon", Description = "This password is too common." })
            : IdentityResult.Success);
    }
}

public sealed class IdentityService(UserManager<AppUser> users, IPasswordHasher<AppUser> hasher) : IIdentityService
{
    private static readonly AppUser _timingUser = new();

    public async Task<Guid?> FindUserIdByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email);
        return user?.Id;
    }

    public async Task<Result<Guid>> CreateUserAsync(
        string email, string password, string displayName, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var user = new AppUser { UserName = email, Email = email, DisplayName = displayName, CreatedAt = now };

        IdentityResult result;
        try
        {
            result = await users.CreateAsync(user, password);
        }
        catch (DbUpdateException)
        {
            // ux_users_normalized_email rejected a concurrent duplicate.
            return EmailTaken;
        }

        if (result.Succeeded)
        {
            return user.Id;
        }

        return result.Errors.Any(error => error.Code is "DuplicateEmail" or "DuplicateUserName")
            ? EmailTaken
            : DomainErrors.Validation(string.Join(" ", result.Errors.Select(error => error.Description)));
    }

    public void SpendPasswordHashTime(string password) => hasher.HashPassword(_timingUser, password);

    private static Error EmailTaken => new("email_taken", "This email is already registered.", ErrorType.Conflict);
}
