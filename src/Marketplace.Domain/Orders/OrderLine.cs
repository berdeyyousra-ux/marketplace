using Marketplace.Domain.Common;

namespace Marketplace.Domain.Orders;

public sealed record OrderLine(Guid ProductId, Guid SellerId, string ProductTitle, Money UnitPrice);
