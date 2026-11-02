using Marketplace.Application.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Marketplace.Infrastructure.Identity;

// Section 7.6: if Seed:Admin:Email and Seed:Admin:Password are set and no user has the Admin role, create a
// confirmed admin. Admin is never self-assignable (section 1.3); this is the only way an admin comes to exist.
public static class AdminBootstrap
{
    public static async Task SeedAdminAsync(this IServiceProvider services, IConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var email = configuration["Seed:Admin:Email"];
        var password = configuration["Seed:Admin:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if ((await users.GetUsersInRoleAsync(Roles.Admin)).Count > 0)
        {
            return;
        }

        var admin = await users.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new AppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = "Administrator",
                CreatedAt = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow(),
            };
            Require(await users.CreateAsync(admin, password), "create the admin user");
        }
        else if (!admin.EmailConfirmed)
        {
            admin.EmailConfirmed = true;
            Require(await users.UpdateAsync(admin), "confirm the admin user");
        }

        Require(await users.AddToRoleAsync(admin, Roles.Admin), "grant the Admin role");
    }

    private static void Require(IdentityResult result, string step)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Admin bootstrap could not {step}: {string.Join(" ", result.Errors.Select(error => error.Code))}");
        }
    }
}
