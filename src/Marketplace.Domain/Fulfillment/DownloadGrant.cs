using Marketplace.Domain.Common;

namespace Marketplace.Domain.Fulfillment;

public enum DownloadUnavailableReason
{
    Revoked,
    Expired,
    Limit,
}

public sealed class DownloadGrant : AggregateRoot
{
    public const int DefaultMaxDownloads = 10;
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(365);

    private DownloadGrant(Guid orderItemId, Guid buyerId, Guid productId, DateTimeOffset paidAt)
    {
        OrderItemId = orderItemId;
        BuyerId = buyerId;
        ProductId = productId;
        CreatedAt = paidAt;
        ExpiresAt = paidAt + Lifetime;
        MaxDownloads = DefaultMaxDownloads;
    }

    // Used by EF Core when materializing from the database.
    private DownloadGrant()
    {
    }

    public Guid OrderItemId { get; private set; }

    public Guid BuyerId { get; private set; }

    public Guid ProductId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public int MaxDownloads { get; private set; }

    public int DownloadCount { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    // Created in the same transaction that marks the order paid (section 9.6).
    public static DownloadGrant Create(Guid orderItemId, Guid buyerId, Guid productId, DateTimeOffset paidAt) =>
        new(orderItemId, buyerId, productId, paidAt);

    public static Error Unavailable(DownloadUnavailableReason reason) =>
        new("download_unavailable", $"This download is unavailable: {reason}.", ErrorType.Conflict,
            Extensions: new Dictionary<string, string> { ["reason"] = reason.ToString().ToLowerInvariant() });

    // Same conditions as the section 9.7 update: not revoked, expires_at > now, download_count < max_downloads.
    public DownloadUnavailableReason? GetUnavailableReason(DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return DownloadUnavailableReason.Revoked;
        }

        if (ExpiresAt <= now)
        {
            return DownloadUnavailableReason.Expired;
        }

        return DownloadCount >= MaxDownloads ? DownloadUnavailableReason.Limit : null;
    }

    public bool CanDownload(DateTimeOffset now) => GetUnavailableReason(now) is null;

    public Result RecordDownload(DateTimeOffset now)
    {
        var reason = GetUnavailableReason(now);
        if (reason is not null)
        {
            return Unavailable(reason.Value);
        }

        DownloadCount++;
        return Result.Success();
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
