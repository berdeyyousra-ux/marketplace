using Marketplace.Application.Ops;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Fulfillment;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Ledger;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Abstractions;

// D-41: handlers use this interface directly. There are no repository classes.
public interface IAppDbContext
{
    DbSet<SellerProfile> SellerProfiles { get; }

    DbSet<Category> Categories { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<OneTimeToken> OneTimeTokens { get; }

    DbSet<Product> Products { get; }

    DbSet<Order> Orders { get; }

    DbSet<OrderItem> OrderItems { get; }

    DbSet<Payment> Payments { get; }

    DbSet<PaymentEvent> PaymentEvents { get; }

    DbSet<DownloadGrant> DownloadGrants { get; }

    DbSet<RefundRequest> RefundRequests { get; }

    DbSet<AiSuggestion> AiSuggestions { get; }

    DbSet<LedgerEntry> LedgerEntries { get; }

    DbSet<IdempotencyKey> IdempotencyKeys { get; }

    DbSet<OutboxMessage> OutboxMessages { get; }

    DbSet<AuditLogEntry> AuditLog { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
