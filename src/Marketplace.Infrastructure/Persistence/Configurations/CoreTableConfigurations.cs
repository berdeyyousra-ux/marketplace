using Marketplace.Domain.Catalog;
using Marketplace.Domain.Fulfillment;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Ledger;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Marketplace.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace Marketplace.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products", table =>
        {
            table.HasCheckConstraint("ck_products_status", "status IN ('Draft','Published','Unpublished','Removed')");
            table.HasCheckConstraint("ck_products_price", "price >= 0.50 AND price <= 10000.00");
            table.HasCheckConstraint("ck_products_currency", "currency = 'EUR'");
            table.HasCheckConstraint(
                "ck_products_published_has_file", "status <> 'Published' OR (file_key IS NOT NULL AND published_at IS NOT NULL)");
        });
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Version).IsRowVersion();
        builder.Property(p => p.Title).HasMaxLength(Product.TitleMaxLength);
        builder.Property(p => p.Description).HasMaxLength(Product.DescriptionMaxLength);
        builder.Property(p => p.Status).AsText();
        builder.Property(p => p.RemovedReason).HasMaxLength(Product.RemovedReasonMaxLength);
        builder.Property(p => p.CoverImageKey).HasColumnType("text");
        builder.MapMoney(p => p.Price, "price");
        builder.OwnsOne(p => p.File, file =>
        {
            file.Property(f => f.Key).HasColumnName("file_key").HasMaxLength(300);
            file.Property(f => f.Name).HasColumnName("file_name").HasMaxLength(255);
            file.Property(f => f.SizeBytes).HasColumnName("file_size_bytes");
            file.Property(f => f.ContentType).HasColumnName("file_content_type").HasMaxLength(100);
            file.Property(f => f.Sha256).HasColumnName("file_sha256").HasMaxLength(64).IsFixedLength();
        });
        builder.Property<NpgsqlTsVector>("SearchVector")
            .HasColumnName("search_vector")
            .HasComputedColumnSql(
                "setweight(to_tsvector('english', coalesce(title, '')), 'A') || setweight(to_tsvector('english', coalesce(description, '')), 'B')",
                stored: true);
        builder.HasOne<SellerProfile>().WithMany().HasForeignKey(p => p.SellerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Category>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.SellerId, p.Status }).HasDatabaseName("ix_products_seller_id_status");
        builder.HasIndex(p => new { p.CategoryId, p.PublishedAt, p.Id })
            .HasDatabaseName("ix_products_category_published")
            .IsDescending(false, true, true)
            .HasFilter("status = 'Published'");
        builder.HasIndex("SearchVector").HasDatabaseName("ix_products_search_vector").HasMethod("gin");
    }
}

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders", table =>
        {
            table.HasCheckConstraint(
                "ck_orders_status", "status IN ('Pending','Paid','PartiallyRefunded','Refunded','Cancelled','Expired')");
            table.HasCheckConstraint("ck_orders_total", "total > 0");
            table.HasCheckConstraint(
                "ck_orders_paid_at", "(status IN ('Paid','PartiallyRefunded','Refunded')) = (paid_at IS NOT NULL)");
        });
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Version).IsRowVersion();
        builder.Property(o => o.Status).AsText();
        builder.MapMoney(o => o.Total, "total");
        builder.HasOne<AppUser>().WithMany().HasForeignKey(o => o.BuyerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => new { o.BuyerId, o.CreatedAt, o.Id })
            .HasDatabaseName("ix_orders_buyer_id_created_at")
            .IsDescending(false, true, true);
        builder.HasIndex(o => o.ExpiresAt).HasDatabaseName("ix_orders_pending_expires_at").HasFilter("status = 'Pending'");
        builder.HasIndex(o => o.PaidAt).HasDatabaseName("ix_orders_paid_at").HasFilter("paid_at IS NOT NULL");

        // R-05 funnel (ADR-0003): an index-only scan of the orders created in a period.
        builder.HasIndex(o => o.CreatedAt).HasDatabaseName("ix_orders_created_at").IncludeProperties(o => new { o.Id, o.Status, o.PaidAt });
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items", table =>
        {
            table.HasCheckConstraint("ck_order_items_status", "status IN ('Active','Refunded')");
            table.HasCheckConstraint("ck_order_items_unit_price", "unit_price > 0");
            table.HasCheckConstraint(
                "ck_order_items_amounts", "platform_fee >= 0 AND seller_amount >= 0 AND platform_fee + seller_amount = unit_price");
        });
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Ignore(i => i.UnitPrice);
        builder.Ignore(i => i.PlatformFee);
        builder.Ignore(i => i.SellerAmount);
        builder.Property<decimal>("_unitPrice").HasColumnName("unit_price").HasPrecision(12, 2);
        builder.Property<decimal>("_platformFee").HasColumnName("platform_fee").HasPrecision(12, 2);
        builder.Property<decimal>("_sellerAmount").HasColumnName("seller_amount").HasPrecision(12, 2);
        builder.Property(i => i.Currency).HasMaxLength(3).IsFixedLength().WithDatabaseDefault("'EUR'");
        builder.Property(i => i.ProductTitle).HasMaxLength(Order.ProductTitleMaxLength);
        builder.Property(i => i.Status).AsText().WithDatabaseDefault("'Active'");
        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SellerProfile>().WithMany().HasForeignKey(i => i.SellerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasAlternateKey(i => new { i.OrderId, i.ProductId }).HasName("ux_order_items_order_id_product_id");
        builder.HasIndex(i => i.ProductId).HasDatabaseName("ix_order_items_product_id");
        builder.HasIndex(i => new { i.SellerId, i.OrderId }).HasDatabaseName("ix_order_items_seller_id_order_id");
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", table =>
        {
            table.HasCheckConstraint("ck_payments_status", "status IN ('RequiresPayment','Succeeded','Failed','Refunded')");
            table.HasCheckConstraint("ck_payments_amount", "amount > 0");
        });
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Provider).HasMaxLength(Payment.ProviderMaxLength);
        builder.Property(p => p.ProviderPaymentId).HasMaxLength(Payment.ProviderPaymentIdMaxLength);
        builder.Property(p => p.FailureCode).HasMaxLength(Payment.FailureCodeMaxLength);
        builder.Property(p => p.Status).AsText();
        builder.MapMoney(p => p.Amount, "amount");
        builder.HasOne<Order>().WithMany().HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasAlternateKey(p => new { p.Provider, p.ProviderPaymentId }).HasName("ux_payments_provider_payment_id");
        builder.HasIndex(p => p.OrderId, "ix_payments_order_id").HasDatabaseName("ix_payments_order_id");
        builder.HasIndex(p => p.OrderId, "ux_payments_one_live_per_order")
            .HasDatabaseName("ux_payments_one_live_per_order")
            .IsUnique()
            .HasFilter("status IN ('RequiresPayment','Succeeded')");
    }
}

internal sealed class DownloadGrantConfiguration : IEntityTypeConfiguration<DownloadGrant>
{
    public void Configure(EntityTypeBuilder<DownloadGrant> builder)
    {
        builder.ToTable("download_grants", table => table.HasCheckConstraint(
            "ck_download_grants_counts", "max_downloads > 0 AND download_count >= 0"));
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.MaxDownloads).WithDatabaseDefault("10");
        builder.Property(g => g.DownloadCount).WithDatabaseDefault("0");
        builder.HasOne<OrderItem>().WithMany().HasForeignKey(g => g.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(g => g.BuyerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(g => g.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasAlternateKey(g => g.OrderItemId).HasName("ux_download_grants_order_item_id");
        builder.HasIndex(g => new { g.BuyerId, g.ProductId })
            .HasDatabaseName("ux_download_grants_active_buyer_product")
            .IsUnique()
            .HasFilter("revoked_at IS NULL");
    }
}

internal sealed class RefundRequestConfiguration : IEntityTypeConfiguration<RefundRequest>
{
    public void Configure(EntityTypeBuilder<RefundRequest> builder)
    {
        builder.ToTable("refund_requests", table =>
        {
            table.HasCheckConstraint(
                "ck_refund_requests_status", "status IN ('Submitted','Approved','Rejected','Executing','Completed','Failed')");
            table.HasCheckConstraint(
                "ck_refund_requests_verdict", "policy_verdict IN ('Eligible','EligibleDownloaded','IneligibleOutsideWindow')");
            table.HasCheckConstraint("ck_refund_requests_decision", "decision IS NULL OR decision IN ('Approved','Rejected')");
            table.HasCheckConstraint("ck_refund_requests_decided", "(decision IS NULL) = (decided_by IS NULL)");
        });
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Version).IsRowVersion();
        builder.Property(r => r.ReasonText).HasMaxLength(RefundRequest.ReasonMaxLength);
        builder.Property(r => r.Status).AsText();
        builder.Property(r => r.PolicyVerdict).AsText();
        builder.Property(r => r.Decision).AsText();
        builder.Property(r => r.DecisionNote).HasMaxLength(RefundRequest.NoteMaxLength);
        builder.Property(r => r.OverrideReason).HasMaxLength(RefundRequest.NoteMaxLength);
        builder.Property(r => r.FailureReason).HasMaxLength(RefundRequest.FailureReasonMaxLength);
        builder.HasOne<OrderItem>().WithMany().HasForeignKey(r => r.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.RequestedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.DecidedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasAlternateKey(r => r.OrderItemId).HasName("ux_refund_requests_order_item_id");
        builder.HasIndex(r => new { r.Status, r.CreatedAt }).HasDatabaseName("ix_refund_requests_status_created_at");
    }
}

internal sealed class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("seller_ledger_entries", table =>
        {
            table.HasCheckConstraint(
                "ck_ledger_entry_type", "entry_type IN ('Sale','PlatformFee','Refund','FeeRefund','Payout')");
            table.HasCheckConstraint(
                "ck_ledger_amount_sign",
                "(entry_type IN ('Sale','FeeRefund') AND amount > 0) OR (entry_type IN ('PlatformFee','Refund','Payout') AND amount < 0)");
        });
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EntryType).AsText();
        builder.MapMoney(e => e.Amount, "amount");

        // Get-only, so it is mapped explicitly rather than through an index.
        builder.Property(e => e.CreatedAt);
        builder.HasOne<SellerProfile>().WithMany().HasForeignKey(e => e.SellerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderItem>().WithMany().HasForeignKey(e => e.OrderItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.OrderItemId, e.EntryType })
            .HasDatabaseName("ux_ledger_item_type")
            .IsUnique()
            .HasFilter("order_item_id IS NOT NULL");

        // ix_ledger_seller_id_created_at and ix_ledger_created_at cover the report queries (ADR-0003). They INCLUDE
        // the amount column, which EF cannot express for a complex property, so the AddReportingIndexes migration
        // creates them in SQL and they are not part of this model.
    }
}
