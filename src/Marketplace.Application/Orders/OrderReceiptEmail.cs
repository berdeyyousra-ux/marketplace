using System.Globalization;
using System.Text;
using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Identity;
using Marketplace.Application.Payments;
using Marketplace.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marketplace.Application.Orders;

// Section 9.10: OrderReceiptEmail sends the buyer a receipt with the order summary. It re-checks that the order
// is still paid and that the buyer still exists, so a deleted account or a reversed order sends nothing.
public sealed class OrderReceiptEmailHandler(IAppDbContext db, IIdentityService identity, IEmailSender email, IOptions<AccountLinkOptions> links)
    : IOutboxHandler
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public string MessageType => OutboxMessageTypes.OrderReceiptEmail;

    public async Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var orderId = (JsonSerializer.Deserialize<OrderMessage>(payload, _json) ?? throw new JsonException("Missing orderId.")).OrderId;
        var order = await db.Orders.AsNoTracking().Include(candidate => candidate.Items)
            .SingleOrDefaultAsync(candidate => candidate.Id == orderId, cancellationToken);
        if (order is null || order.PaidAt is null || order.Status == OrderStatus.Cancelled)
        {
            return;
        }

        var account = await identity.GetAccountAsync(order.BuyerId, cancellationToken);
        if (account is null)
        {
            return;
        }

        await email.SendAsync(new EmailMessage(account.Email, $"Your receipt for order {ShortId(order.Id)}", Body(order, account.DisplayName)), cancellationToken);
    }

    private string Body(Order order, string displayName)
    {
        var body = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"Hi {displayName},\n\nThank you for your purchase. Order {order.Id}, paid {order.PaidAt:yyyy-MM-dd HH:mm} UTC.\n\n");
        foreach (var item in order.Items)
        {
            body.Append(CultureInfo.InvariantCulture, $"- {item.ProductTitle}: {Format(item.UnitPrice)}\n");
        }

        return body
            .Append(CultureInfo.InvariantCulture, $"\nTotal: {Format(order.Total)}\n\nDownload your products from your library:\n{links.Value.LinkBaseUrl.TrimEnd('/')}/library\n")
            .ToString();
    }

    private static string Format(Domain.Common.Money money) => string.Create(CultureInfo.InvariantCulture, $"{money.Amount:0.00} {money.Currency}");

    // The last eight hex digits, as the web client shows them. Order ids are UUIDv7, whose leading digits are a
    // timestamp shared by orders placed within the same minute; the trailing ones are random.
    private static string ShortId(Guid id) => id.ToString("N")[^8..].ToUpperInvariant();
}
