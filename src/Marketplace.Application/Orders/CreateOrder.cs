using FluentValidation;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marketplace.Application.Orders;

// Section 16 "Marketplace" configuration and D-38.
public sealed class MarketplaceOptions
{
    public const string SectionName = "Marketplace";

    public int CommissionBasisPoints { get; set; } = 1000;
}

// EP-30 and US-22.
public sealed record CreateOrderCommand(IReadOnlyList<Guid> ProductIds);

public sealed record OrderItemResponse(Guid Id, Guid ProductId, string Title, MoneyDto UnitPrice, string Status);

public sealed record OrderResponse(
    Guid Id, string Status, MoneyDto Total, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? PaidAt, IReadOnlyList<OrderItemResponse> Items);

public static class OrderErrors
{
    public static Error ProductUnavailable(IEnumerable<Guid> productIds) => new(
        "product_unavailable", "One or more products cannot be purchased.", ErrorType.Conflict,
        new Dictionary<string, string[]> { ["productIds"] = productIds.Select(id => id.ToString()).ToArray() });

    public static readonly Error OwnProduct = new("own_product", "You cannot buy your own product.", ErrorType.Conflict);

    public static readonly Error AlreadyOwned = new("already_owned", "You already own one of these products.", ErrorType.Conflict);
}

public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(command => command.ProductIds).NotNull()
            .Must(ids => ids is { Count: >= 1 and <= Order.MaxItems }).WithMessage($"An order needs 1 to {Order.MaxItems} products.")
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count).WithMessage("Product IDs must not repeat.");
    }
}

public static class OrderMapping
{
    public static OrderResponse ToResponse(Order order) => new(
        order.Id,
        order.Status.ToString(),
        MoneyDto.From(order.Total),
        order.CreatedAt,
        order.ExpiresAt,
        order.PaidAt,
        order.Items.Select(item => new OrderItemResponse(item.Id, item.ProductId, item.ProductTitle, MoneyDto.From(item.UnitPrice), item.Status.ToString())).ToList());
}

// Section 9.3. A buyer may hold several Pending orders for one product; ownership is enforced at payment
// time by the grant's unique index (changelog 2026-11-02).
public sealed class CreateOrderCommandHandler(
    IAppDbContext db, ICurrentUser currentUser, AuditTrail audit, IOptions<MarketplaceOptions> options, TimeProvider timeProvider)
    : ICommandHandler<CreateOrderCommand, OrderResponse>
{
    public const int ExpiryMinutes = 30;

    public async Task<Result<OrderResponse>> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var buyerId = currentUser.UserId ?? throw new InvalidOperationException("Ordering requires an authenticated caller.");
        var products = await db.Products.AsNoTracking()
            .Where(product => command.ProductIds.Contains(product.Id))
            .Select(product => new
            {
                product.Id,
                product.Title,
                product.Price,
                product.Status,
                product.SellerId,
                SellerUserId = db.SellerProfiles.Where(seller => seller.Id == product.SellerId).Select(seller => seller.UserId).Single(),
            })
            .ToListAsync(cancellationToken);

        var unavailable = command.ProductIds
            .Where(id => products.All(product => product.Id != id || product.Status != ProductStatus.Published))
            .ToList();
        if (unavailable.Count > 0)
        {
            return OrderErrors.ProductUnavailable(unavailable);
        }

        if (products.Any(product => product.SellerUserId == buyerId))
        {
            return OrderErrors.OwnProduct;
        }

        if (await db.DownloadGrants.AnyAsync(
            grant => grant.BuyerId == buyerId && command.ProductIds.Contains(grant.ProductId) && grant.RevokedAt == null, cancellationToken))
        {
            return OrderErrors.AlreadyOwned;
        }

        var lines = command.ProductIds
            .Select(id => products.Single(product => product.Id == id))
            .Select(product => new OrderLine(product.Id, product.SellerId, product.Title, product.Price))
            .ToList();
        var created = Order.Create(buyerId, lines, timeProvider.GetUtcNow(), ExpiryMinutes, options.Value.CommissionBasisPoints);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Orders.Add(created.Value);
        audit.Record("order.created", AuditActorType.User, buyerId, "Order", created.Value.Id, new { items = lines.Count, total = created.Value.Total.Amount });
        await db.SaveChangesAsync(cancellationToken);
        return OrderMapping.ToResponse(created.Value);
    }
}
