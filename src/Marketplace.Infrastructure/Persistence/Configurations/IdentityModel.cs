using Marketplace.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Persistence.Configurations;

// Section 7.1: Identity tables are renamed; Identity-generated columns are kept as generated.
internal static class IdentityModel
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<AppUser>(user =>
        {
            user.ToTable("users");
            user.Property(u => u.DisplayName).HasMaxLength(AppUser.DisplayNameMaxLength).IsRequired();
            user.Property(u => u.CreatedAt).IsRequired();
            user.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ux_users_normalized_email").IsUnique();
            user.HasIndex(u => u.NormalizedUserName).HasDatabaseName("ux_users_normalized_user_name").IsUnique();
        });

        builder.Entity<IdentityRole<Guid>>(role =>
        {
            role.ToTable("roles");
            role.HasIndex(r => r.NormalizedName).HasDatabaseName("ux_roles_normalized_name").IsUnique();
        });

        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
    }
}
