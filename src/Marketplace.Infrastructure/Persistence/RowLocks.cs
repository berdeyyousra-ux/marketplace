using Marketplace.Application.Payments;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Persistence;

// SELECT ... FOR UPDATE inside the caller's transaction (section 4.6, "Mark order paid").
public sealed class RowLocks(AppDbContext db) : IRowLocks
{
    public Task LockOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT 1 FROM orders WHERE id = {orderId} FOR UPDATE", cancellationToken);
}
