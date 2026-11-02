using System.Text.Json.Serialization;
using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Catalog;

// EP-22 to EP-24, EP-26, EP-27 and US-11, US-13, US-14, US-21.
public sealed record ProductDetails(string Title, string Description, MoneyDto Price, int CategoryId);

public sealed record CreateProductCommand(ProductDetails Details);

public sealed record UpdateProductCommand(Guid ProductId, uint ExpectedVersion, ProductDetails Details);

public sealed record ChangeProductStatusCommand(Guid ProductId, uint ExpectedVersion, bool Publish);

public sealed record ProductQuery(Guid ProductId);

public sealed record ProductFileResponse(string Name, long SizeBytes, string ContentType, string Sha256);

public sealed record ProductResponse(
    Guid Id,
    Guid SellerId,
    int CategoryId,
    string Title,
    string Description,
    MoneyDto Price,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? PublishedAt,
    ProductFileResponse? File,
    [property: JsonIgnore] uint Version,
    [property: JsonIgnore] bool IsOwner);

public static class ProductErrors
{
    public static readonly Error NotFound = new("not_found", "Product not found.", ErrorType.NotFound);

    public static readonly Error PreconditionFailed =
        new("precondition_failed", "The product changed since you loaded it. Reload it and try again.", ErrorType.PreconditionFailed);

    public static readonly Error UnknownCategory = DomainErrors.Validation("Category does not exist.");
}

public sealed class ProductDetailsValidator : AbstractValidator<ProductDetails>
{
    public ProductDetailsValidator()
    {
        RuleFor(details => details.Title).NotEmpty().Length(Product.TitleMinLength, Product.TitleMaxLength);
        RuleFor(details => details.Description).NotEmpty().MaximumLength(Product.DescriptionMaxLength);
        RuleFor(details => details.Price).NotNull().SetValidator(new MoneyDtoValidator(Product.MinPrice, Product.MaxPrice));
        RuleFor(details => details.CategoryId).GreaterThan(0);
    }
}

// Resolves the caller's seller profile and finds products they own. Other sellers' products are reported
// as not found, never forbidden, so identifiers cannot be probed (section 8.1).
public sealed class ProductOwnership(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<Guid?> CurrentSellerIdAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        return userId is null
            ? null
            : await db.SellerProfiles.Where(profile => profile.UserId == userId).Select(profile => (Guid?)profile.Id).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Product?> FindOwnedAsync(Guid productId, CancellationToken cancellationToken)
    {
        var sellerId = await CurrentSellerIdAsync(cancellationToken);
        return sellerId is null
            ? null
            : await db.Products.SingleOrDefaultAsync(product => product.Id == productId && product.SellerId == sellerId, cancellationToken);
    }
}

internal static class ProductMapping
{
    public static ProductResponse ToResponse(Product product, bool isOwner, bool showFile) => new(
        product.Id,
        product.SellerId,
        product.CategoryId,
        product.Title,
        product.Description,
        MoneyDto.From(product.Price),
        product.Status.ToString(),
        product.CreatedAt,
        product.UpdatedAt,
        product.PublishedAt,
        showFile && product.File is { } file ? new ProductFileResponse(file.Name, file.SizeBytes, file.ContentType, file.Sha256) : null,
        product.Version,
        isOwner);

    public static Task<bool> CategoryExistsAsync(IAppDbContext db, int categoryId, CancellationToken cancellationToken) =>
        db.Categories.AnyAsync(category => category.Id == categoryId, cancellationToken);
}

public sealed class CreateProductCommandHandler(IAppDbContext db, ProductOwnership ownership, TimeProvider timeProvider)
    : ICommandHandler<CreateProductCommand, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(CreateProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sellerId = await ownership.CurrentSellerIdAsync(cancellationToken)
            ?? throw new InvalidOperationException("Creating a product requires a seller profile; the Seller policy guarantees one.");
        var details = command.Details;
        if (!await ProductMapping.CategoryExistsAsync(db, details.CategoryId, cancellationToken))
        {
            return ProductErrors.UnknownCategory;
        }

        details.Price.TryToMoney(out var price);
        var created = Product.Create(sellerId, details.CategoryId, details.Title, details.Description, price, timeProvider.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Products.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return ProductMapping.ToResponse(created.Value, isOwner: true, showFile: true);
    }
}

public sealed class UpdateProductCommandHandler(IAppDbContext db, ProductOwnership ownership, TimeProvider timeProvider)
    : ICommandHandler<UpdateProductCommand, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(UpdateProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await ownership.FindOwnedAsync(command.ProductId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound;
        }

        if (product.Version != command.ExpectedVersion)
        {
            return ProductErrors.PreconditionFailed;
        }

        if (!await ProductMapping.CategoryExistsAsync(db, command.Details.CategoryId, cancellationToken))
        {
            return ProductErrors.UnknownCategory;
        }

        command.Details.Price.TryToMoney(out var price);
        var updated = product.Update(command.Details.CategoryId, command.Details.Title, command.Details.Description, price, timeProvider.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        return await ProductSaving.SaveAsync(db, product, cancellationToken);
    }
}

public sealed class ChangeProductStatusCommandHandler(IAppDbContext db, ProductOwnership ownership, AuditTrail audit, ICurrentUser currentUser, TimeProvider timeProvider)
    : ICommandHandler<ChangeProductStatusCommand, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(ChangeProductStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await ownership.FindOwnedAsync(command.ProductId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound;
        }

        if (product.Version != command.ExpectedVersion)
        {
            return ProductErrors.PreconditionFailed;
        }

        var now = timeProvider.GetUtcNow();
        var changed = command.Publish ? product.Publish(now) : product.Unpublish(now);
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        audit.Record(command.Publish ? "product.published" : "product.unpublished", AuditActorType.User, currentUser.UserId, "Product", product.Id);
        return await ProductSaving.SaveAsync(db, product, cancellationToken);
    }
}

internal static class ProductSaving
{
    // A concurrent write between loading and saving fails the xmin check. With If-Match supplied that is 412 (section 4.6).
    public static async Task<Result<ProductResponse>> SaveAsync(IAppDbContext db, Product product, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ProductErrors.PreconditionFailed;
        }

        return ProductMapping.ToResponse(product, isOwner: true, showFile: true);
    }
}

// US-21: Published products are public. Other statuses are visible to the owner, an admin, or a buyer with an
// active download grant; everyone else gets 404. File metadata is shown only to the owner and admins.
public sealed class ProductQueryHandler(IAppDbContext db, ProductOwnership ownership, ICurrentUser currentUser)
    : IQueryHandler<ProductQuery, ProductResponse>
{
    public async Task<Result<ProductResponse>> HandleAsync(ProductQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == query.ProductId, cancellationToken);
        if (product is null)
        {
            return ProductErrors.NotFound;
        }

        var isOwner = await ownership.CurrentSellerIdAsync(cancellationToken) == product.SellerId;
        var isAdmin = currentUser.IsInRole(Roles.Admin);
        if (product.Status != ProductStatus.Published && !isOwner && !isAdmin && !await HasGrantAsync(product.Id, cancellationToken))
        {
            return ProductErrors.NotFound;
        }

        return ProductMapping.ToResponse(product, isOwner, showFile: isOwner || isAdmin);
    }

    private async Task<bool> HasGrantAsync(Guid productId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        return userId is not null
            && await db.DownloadGrants.AnyAsync(grant => grant.ProductId == productId && grant.BuyerId == userId && grant.RevokedAt == null, cancellationToken);
    }
}
