using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;
using Marketplace.Application.Ops;
using Marketplace.Domain.Common;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Payments;

// EP-33 and US-23.
public sealed record CreatePaymentCommand(Guid OrderId);

// PublishableKey is the provider's public key, for the client's payment form (T-064). It is null for a provider
// that has none, such as Fake.
public sealed record PaymentResponse(Guid PaymentId, string Provider, string ClientSecret, MoneyDto Amount, string? PublishableKey);

public static class PaymentErrors
{
    public static readonly Error OrderNotFound = new("not_found", "Order not found.", ErrorType.NotFound);

    public static readonly Error NotAllowed =
        new("payment_not_allowed", "This order can no longer be paid.", ErrorType.Conflict);

    public static readonly Error ProviderUnavailable =
        new("payment_provider_unavailable", "The payment provider is unavailable. Try again shortly.", ErrorType.Unavailable);
}

// Section 9.4. The provider call is the one synchronous provider call the Api makes (section 4.2), and it runs
// outside any database transaction. Client secrets are never stored; they are fetched again when needed.
public sealed class CreatePaymentCommandHandler(
    IAppDbContext db, IPaymentProvider provider, ICurrentUser currentUser, AuditTrail audit, TimeProvider timeProvider)
    : ICommandHandler<CreatePaymentCommand, PaymentResponse>
{
    public async Task<Result<PaymentResponse>> HandleAsync(CreatePaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var buyerId = currentUser.UserId ?? throw new InvalidOperationException("Paying requires an authenticated caller.");
        var order = await db.Orders.AsNoTracking()
            .Where(candidate => candidate.Id == command.OrderId && candidate.BuyerId == buyerId)
            .Select(candidate => new { candidate.Id, candidate.Status, candidate.ExpiresAt, candidate.Total })
            .SingleOrDefaultAsync(cancellationToken);
        if (order is null)
        {
            return PaymentErrors.OrderNotFound;
        }

        if (order.Status != OrderStatus.Pending || order.ExpiresAt <= timeProvider.GetUtcNow())
        {
            return PaymentErrors.NotAllowed;
        }

        var live = await FindLivePaymentAsync(order.Id, cancellationToken);
        if (live is not null)
        {
            return await RespondAsync(live, cancellationToken);
        }

        var attempt = await db.Payments.CountAsync(payment => payment.OrderId == order.Id, cancellationToken) + 1;
        ProviderPayment created;
        try
        {
            created = await provider.CreatePaymentAsync(
                new CreateProviderPayment(order.Id, order.Total, $"order-{order.Id}-attempt-{attempt}"), cancellationToken);
        }
        catch (PaymentProviderUnavailableException)
        {
            return PaymentErrors.ProviderUnavailable;
        }

        var payment = Payment.Create(order.Id, provider.Name, created.ProviderPaymentId, order.Total, timeProvider.GetUtcNow());
        if (payment.IsFailure)
        {
            return payment.Error;
        }

        db.Payments.Add(payment.Value);
        audit.Record("payment.created", AuditActorType.User, buyerId, "Payment", payment.Value.Id, new { orderId = order.Id, attempt });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request recorded the payment first (ux_payments_one_live_per_order or the provider ID).
            // A retry recomputes the same provider idempotency key, so the provider holds just one payment.
            var winner = await FindLivePaymentAsync(order.Id, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return await RespondAsync(winner, cancellationToken);
        }

        return new PaymentResponse(
            payment.Value.Id, provider.Name, created.ClientSecret ?? string.Empty, MoneyDto.From(order.Total), provider.PublishableKey);
    }

    private Task<LivePayment?> FindLivePaymentAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.Payments.AsNoTracking()
            .Where(payment => payment.OrderId == orderId && payment.Status == PaymentStatus.RequiresPayment)
            .Select(payment => new LivePayment(payment.Id, payment.ProviderPaymentId, payment.Amount))
            .SingleOrDefaultAsync(cancellationToken);

    private async Task<Result<PaymentResponse>> RespondAsync(LivePayment live, CancellationToken cancellationToken)
    {
        try
        {
            var current = await provider.GetPaymentAsync(live.ProviderPaymentId, cancellationToken);
            return new PaymentResponse(
                live.Id, provider.Name, current.ClientSecret ?? string.Empty, MoneyDto.From(live.Amount), provider.PublishableKey);
        }
        catch (PaymentProviderUnavailableException)
        {
            return PaymentErrors.ProviderUnavailable;
        }
    }

    private sealed record LivePayment(Guid Id, string ProviderPaymentId, Money Amount);
}
