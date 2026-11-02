using Marketplace.Domain.Catalog;
using Marketplace.Domain.Identity;
using Marketplace.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

// Section 7.2 declares these uniques as constraints, so they are mapped as alternate keys rather than unique indexes.
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).HasMaxLength(RefreshToken.TokenHashLength).IsFixedLength();
        builder.Property(t => t.UserAgent).HasMaxLength(RefreshToken.UserAgentMaxLength);
        builder.Property(t => t.CreatedIp).HasColumnType("inet");
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasAlternateKey(t => t.TokenHash).HasName("ux_refresh_tokens_token_hash");
        builder.HasIndex(t => t.UserId).HasDatabaseName("ix_refresh_tokens_user_id");
        builder.HasIndex(t => t.FamilyId).HasDatabaseName("ix_refresh_tokens_family_id");
    }
}

internal sealed class OneTimeTokenConfiguration : IEntityTypeConfiguration<OneTimeToken>
{
    public void Configure(EntityTypeBuilder<OneTimeToken> builder)
    {
        builder.ToTable("one_time_tokens", table => table.HasCheckConstraint(
            "ck_one_time_tokens_purpose", "purpose IN ('EmailVerification','PasswordReset')"));
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Purpose).HasConversion<string>().HasColumnType("text");
        builder.Property(t => t.TokenHash).HasMaxLength(RefreshToken.TokenHashLength).IsFixedLength();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasAlternateKey(t => t.TokenHash).HasName("ux_one_time_tokens_token_hash");
        builder.HasIndex(t => new { t.UserId, t.Purpose }).HasDatabaseName("ix_one_time_tokens_user_id_purpose");
    }
}

internal sealed class SellerProfileConfiguration : IEntityTypeConfiguration<SellerProfile>
{
    public void Configure(EntityTypeBuilder<SellerProfile> builder)
    {
        builder.ToTable("seller_profiles");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.DisplayName).HasMaxLength(SellerProfile.DisplayNameMaxLength);
        builder.Property(s => s.Bio).HasMaxLength(SellerProfile.BioMaxLength);
        builder.Property(s => s.Country).HasMaxLength(2).IsFixedLength();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasAlternateKey(s => s.UserId).HasName("ux_seller_profiles_user_id");
    }
}

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.Property(c => c.Id).UseIdentityAlwaysColumn();
        builder.Property(c => c.Slug).HasMaxLength(60);
        builder.Property(c => c.Name).HasMaxLength(80);
        builder.HasAlternateKey(c => c.Slug).HasName("ux_categories_slug");
    }
}
