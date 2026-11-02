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

        // Identity's own foreign key indexes, kept as Identity generates them (section 7.1).
        builder.Entity<IdentityUserRole<Guid>>(table => table.ToTable("user_roles").HasIndex(r => r.RoleId, "ix_user_roles_role_id").HasDatabaseName("ix_user_roles_role_id"));
        builder.Entity<IdentityUserClaim<Guid>>(table => table.ToTable("user_claims").HasIndex(c => c.UserId, "ix_user_claims_user_id").HasDatabaseName("ix_user_claims_user_id"));
        builder.Entity<IdentityUserLogin<Guid>>(table => table.ToTable("user_logins").HasIndex(l => l.UserId, "ix_user_logins_user_id").HasDatabaseName("ix_user_logins_user_id"));
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>(table => table.ToTable("role_claims").HasIndex(c => c.RoleId, "ix_role_claims_role_id").HasDatabaseName("ix_role_claims_role_id"));
    }
}
