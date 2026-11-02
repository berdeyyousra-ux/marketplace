namespace Marketplace.Tools.Seeding;

// Section 13.4. Every value comes from one Random seeded with Seed and from Now, so the same options give the
// same dataset.
public sealed record SeedOptions(int Orders, int Sellers, int Buyers, int Products, int Seed, DateTimeOffset Now)
{
    public static readonly TimeSpan History = TimeSpan.FromDays(730);

    public const int CommissionBasisPoints = 1000;

    public const string Password = "seed password 2026";

    public const string EmailDomain = "seed.test";

    public static SeedOptions Load(int seed, DateTimeOffset now) => new(1_000_000, 500, 20_000, 10_000, seed, now);

    public static SeedOptions Demo(DateTimeOffset now) => new(400, 6, 30, 60, 7, now);
}

public enum SeedOrderStatus : byte
{
    Pending,
    Paid,
    PartiallyRefunded,
    Refunded,
    Cancelled,
    Expired,
}

public record struct SeedUser
{
    public Guid Id { get; set; }
    public string Email { get; set; }
    public string DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public record struct SeedSeller
{
    public Guid Id { get; set; }
    public int UserIndex { get; set; }
    public string DisplayName { get; set; }
    public string Country { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public record struct SeedProduct
{
    public Guid Id { get; set; }
    public int SellerIndex { get; set; }
    public int CategoryId { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; }
    public string? FileKey { get; set; }
    public string? FileName { get; set; }
    public long FileSize { get; set; }
    public string? FileContentType { get; set; }
    public string? FileSha256 { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}

public record struct SeedOrder
{
    public Guid Id { get; set; }
    public int BuyerIndex { get; set; }
    public SeedOrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public int FirstItem { get; set; }
    public int ItemCount { get; set; }

    // A declined attempt before the outcome. Paid orders then have two payments.
    public bool HadFailedAttempt { get; set; }
    public Guid PaymentId { get; set; }
    public Guid FailedPaymentId { get; set; }
}

public record struct SeedItem
{
    public Guid Id { get; set; }
    public int OrderIndex { get; set; }
    public int ProductIndex { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal PlatformFee { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public int DownloadCount { get; set; }
    public bool RefundRejected { get; set; }
    public Guid GrantId { get; set; }
    public Guid RefundRequestId { get; set; }
}

public sealed class SeedDataset
{
    public required SeedOptions Options { get; init; }

    public required SeedUser Admin { get; init; }

    public required SeedUser[] SellerUsers { get; init; }

    public required SeedUser[] Buyers { get; init; }

    public required SeedSeller[] Sellers { get; init; }

    public required SeedProduct[] Products { get; init; }

    public required SeedOrder[] Orders { get; init; }

    public required SeedItem[] Items { get; init; }
}
