using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class Product : AggregateRoot
{
    public const int TitleMinLength = 3;
    public const int TitleMaxLength = 120;
    public const int DescriptionMaxLength = 5000;
    public const int RemovedReasonMaxLength = 500;
    public const decimal MinPrice = 0.50m;
    public const decimal MaxPrice = 10000.00m;

    public static readonly Error Incomplete =
        new("product_incomplete", "A product needs a file before it can be published.", ErrorType.Validation);

    private Product(Guid sellerId, int categoryId, string title, string description, Money price, DateTimeOffset now)
    {
        SellerId = sellerId;
        CategoryId = categoryId;
        Title = title;
        Description = description;
        Price = price;
        Status = ProductStatus.Draft;
        CreatedAt = now;
        UpdatedAt = now;
    }

    // Used by EF Core when materializing from the database.
    private Product()
    {
        Title = string.Empty;
        Description = string.Empty;
    }

    public Guid SellerId { get; private set; }

    public int CategoryId { get; private set; }

    public string Title { get; private set; }

    public string Description { get; private set; }

    public Money Price { get; private set; }

    public ProductStatus Status { get; private set; }

    public ProductFile? File { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    public string? RemovedReason { get; private set; }

    // Mapped to the PostgreSQL xmin system column as an optimistic concurrency token (D-15).
    public uint Version { get; private set; }

    public static Result<Product> Create(
        Guid sellerId, int categoryId, string title, string description, Money price, DateTimeOffset now)
    {
        if (sellerId == Guid.Empty)
        {
            return DomainErrors.Validation("Seller is required.");
        }

        var error = ValidateDetails(categoryId, title, description, price);
        return error is null ? new Product(sellerId, categoryId, title, description, price, now) : error;
    }

    public Result Update(int categoryId, string title, string description, Money price, DateTimeOffset now)
    {
        if (Status == ProductStatus.Removed)
        {
            return DomainErrors.InvalidState("A removed product cannot be edited.");
        }

        var error = ValidateDetails(categoryId, title, description, price);
        if (error is not null)
        {
            return error;
        }

        CategoryId = categoryId;
        Title = title;
        Description = description;
        Price = price;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result AttachFile(ProductFile file, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (Status == ProductStatus.Removed)
        {
            return DomainErrors.InvalidState("A removed product cannot receive a file.");
        }

        File = file;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Publish(DateTimeOffset now)
    {
        if (Status is not (ProductStatus.Draft or ProductStatus.Unpublished))
        {
            return Transition(ProductStatus.Published);
        }

        if (File is null)
        {
            return Incomplete;
        }

        Status = ProductStatus.Published;
        PublishedAt ??= now;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Unpublish(DateTimeOffset now)
    {
        if (Status != ProductStatus.Published)
        {
            return Transition(ProductStatus.Unpublished);
        }

        Status = ProductStatus.Unpublished;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Remove(string reason, DateTimeOffset now)
    {
        if (Status == ProductStatus.Removed)
        {
            return Transition(ProductStatus.Removed);
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > RemovedReasonMaxLength)
        {
            return DomainErrors.Validation($"Removal reason must be 1 to {RemovedReasonMaxLength} characters.");
        }

        Status = ProductStatus.Removed;
        RemovedReason = reason;
        RemovedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    private Error Transition(ProductStatus to) => DomainErrors.InvalidStateTransition(nameof(Product), Status, to);

    private static Error? ValidateDetails(int categoryId, string title, string description, Money price)
    {
        if (categoryId <= 0)
        {
            return DomainErrors.Validation("Category is required.");
        }

        if (string.IsNullOrWhiteSpace(title) || title.Length is < TitleMinLength or > TitleMaxLength)
        {
            return DomainErrors.Validation($"Title must be {TitleMinLength} to {TitleMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(description) || description.Length > DescriptionMaxLength)
        {
            return DomainErrors.Validation($"Description must be 1 to {DescriptionMaxLength} characters.");
        }

        if (price.Currency != Money.EuroCurrency || price.Amount is < MinPrice or > MaxPrice)
        {
            return DomainErrors.Validation($"Price must be {MinPrice} to {MaxPrice} EUR.");
        }

        return null;
    }
}
