using Marketplace.Application.Payments;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Payments;

public sealed class PaymentEventInbox(AppDbContext db) : IPaymentEventInbox
{
    public async Task<Guid?> TryStoreAsync(string provider, PaymentWebhookResult webhook, DateTimeOffset receivedAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(webhook);

        var id = Guid.CreateVersion7();
        var eventType = webhook.Type.ToString();
        var inserted = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO payment_events (id, provider, provider_event_id, event_type, payload, received_at, attempts)
            VALUES ({id}, {provider}, {webhook.ProviderEventId}, {eventType}, CAST({webhook.RawPayload} AS jsonb), {receivedAt}, 0)
            ON CONFLICT (provider, provider_event_id) DO NOTHING
            """, cancellationToken);
        return inserted == 1 ? id : null;
    }
}
