using Marketplace.Application.Ops;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Fulfillment;
using Marketplace.Domain.Identity;
using Marketplace.Domain.Ledger;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Marketplace.Infrastructure.Identity;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Testcontainers.PostgreSql;

namespace Marketplace.IntegrationTests.Persistence;

// Checks that every mapped entity saves and loads with its values intact. The schema here comes from
// EnsureCreated; the migration and schema contract test arrive with T-013.
public sealed class ModelRoundTripTests : IAsyncLifetime
{
    private static readonly DateTimeOffset _now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync(TestContext.Current.CancellationToken);
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task SaveChanges_EveryEntity_RoundTripsValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var buyer = new AppUser { UserName = "buyer@example.test", Email = "buyer@example.test", DisplayName = "Buyer", CreatedAt = _now };
        var sellerUser = new AppUser { UserName = "seller@example.test", Email = "seller@example.test", DisplayName = "Seller", CreatedAt = _now };
        var seller = SellerProfile.Create(sellerUser.Id, "Studio Lumen", null, "ES", _now).Value;
        var category = new Category("templates", "Templates", 1);

        await using (var db = CreateContext())
        {
            db.Users.AddRange(buyer, sellerUser);
            db.SellerProfiles.Add(seller);
            db.Categories.Add(category);
            await db.SaveChangesAsync(ct);
        }

        var product = Product.Create(seller.Id, category.Id, "Notion Budget Template", "A budget template.", Money.Eur(19.99m), _now).Value;
        product.AttachFile(ProductFile.Create("products/a/b", "budget.pdf", 2048, "application/pdf", new string('a', 64)).Value, _now);
        product.Publish(_now);
        var order = Order.Create(buyer.Id, [new OrderLine(product.Id, seller.Id, product.Title, product.Price)], _now, 30, 1000).Value;
        order.MarkPaid(_now);
        var item = order.Items[0];
        var payment = Payment.Create(order.Id, "Fake", "pi_123", order.Total, _now).Value;
        payment.MarkSucceeded(_now);
        var grant = DownloadGrant.Create(item.Id, buyer.Id, product.Id, _now);
        var refund = RefundRequest.Submit(item.Id, buyer.Id, "The file does not open.", RefundPolicyVerdict.Eligible, 0, _now).Value;
        var ledger = LedgerEntry.ForPaidItem(item, _now);
        var token = RefreshToken.Issue(buyer.Id, Guid.CreateVersion7(), new string('b', 64), _now, TimeSpan.FromDays(14), System.Net.IPAddress.Loopback, "tests");

        await using (var db = CreateContext())
        {
            db.Products.Add(product);
            db.Orders.Add(order);
            db.Payments.Add(payment);
            db.DownloadGrants.Add(grant);
            db.RefundRequests.Add(refund);
            db.LedgerEntries.AddRange(ledger);
            db.RefreshTokens.Add(token);
            await db.SaveChangesAsync(ct);
        }

        await using var read = CreateContext();
        var savedProduct = await read.Products.SingleAsync(ct);
        savedProduct.Price.ShouldBe(Money.Eur(19.99m));
        savedProduct.File.ShouldBe(product.File);
        savedProduct.Status.ShouldBe(ProductStatus.Published);
        savedProduct.Version.ShouldNotBe(0u);

        var savedOrder = await read.Orders.Include(o => o.Items).SingleAsync(ct);
        savedOrder.Total.ShouldBe(Money.Eur(19.99m));
        savedOrder.PaidAt.ShouldBe(_now);
        savedOrder.Items.Single().PlatformFee.ShouldBe(Money.Eur(2.00m));
        savedOrder.Items.Single().SellerAmount.ShouldBe(Money.Eur(17.99m));

        (await read.Payments.SingleAsync(ct)).Status.ShouldBe(PaymentStatus.Succeeded);
        (await read.DownloadGrants.SingleAsync(ct)).ExpiresAt.ShouldBe(_now.AddDays(365));
        (await read.RefundRequests.SingleAsync(ct)).PolicyVerdict.ShouldBe(RefundPolicyVerdict.Eligible);
        (await read.LedgerEntries.SumAsync(e => e.Amount.Amount, ct)).ShouldBe(17.99m);
        (await read.RefreshTokens.SingleAsync(ct)).CreatedIp.ShouldBe(System.Net.IPAddress.Loopback);
    }

    [Fact]
    public async Task SaveChanges_StaleProductVersion_ThrowsConcurrencyException()
    {
        var ct = TestContext.Current.CancellationToken;
        var sellerUser = new AppUser { UserName = "s2@example.test", Email = "s2@example.test", DisplayName = "Seller", CreatedAt = _now };
        var seller = SellerProfile.Create(sellerUser.Id, "Studio Two", null, "ES", _now).Value;
        var category = new Category("ebooks", "Ebooks", 1);
        await using (var db = CreateContext())
        {
            db.Users.Add(sellerUser);
            db.SellerProfiles.Add(seller);
            db.Categories.Add(category);
            await db.SaveChangesAsync(ct);
            db.Products.Add(Product.Create(seller.Id, category.Id, "Field Guide", "A guide.", Money.Eur(5m), _now).Value);
            await db.SaveChangesAsync(ct);
        }

        await using var first = CreateContext();
        await using var second = CreateContext();
        var a = await first.Products.SingleAsync(ct);
        var b = await second.Products.SingleAsync(ct);
        a.Update(category.Id, "Field Guide v2", "A guide.", Money.Eur(5m), _now);
        await first.SaveChangesAsync(ct);
        b.Update(category.Id, "Field Guide v3", "A guide.", Money.Eur(5m), _now);

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task SaveChanges_OperationsTables_RoundTripValues()
    {
        var ct = TestContext.Current.CancellationToken;
        var user = new AppUser { UserName = "ops@example.test", Email = "ops@example.test", DisplayName = "Ops", CreatedAt = _now };
        await using (var db = CreateContext())
        {
            db.Users.Add(user);
            db.PaymentEvents.Add(PaymentEvent.Receive("Fake", "evt_1", "payment_intent.succeeded", """{"id":"evt_1"}""", _now));
            db.OutboxMessages.Add(new OutboxMessage { Type = "OrderReceiptEmail", Payload = """{"orderId":"x"}""", CreatedAt = _now, AvailableAt = _now, CorrelationId = "corr-1" });
            db.AuditLog.Add(new AuditLogEntry { OccurredAt = _now, ActorType = AuditActorType.Worker, Action = "order.expired", EntityType = "Order", EntityId = Guid.CreateVersion7(), Details = """{"reason":"expired"}""" });
            db.IdempotencyKeys.Add(new IdempotencyKey { UserId = user.Id, Key = "key-1", Method = "POST", Path = "/api/v1/orders", RequestHash = new string('c', 64), CreatedAt = _now, LockedUntil = _now.AddSeconds(60), ExpiresAt = _now.AddHours(24) });
            await db.SaveChangesAsync(ct);
        }

        await using var read = CreateContext();
        (await read.PaymentEvents.SingleAsync(ct)).Attempts.ShouldBe(0);
        (await read.OutboxMessages.SingleAsync(ct)).CorrelationId.ShouldBe("corr-1");
        var audit = await read.AuditLog.SingleAsync(ct);
        audit.Id.ShouldBeGreaterThan(0);
        audit.ActorType.ShouldBe(AuditActorType.Worker);
        (await read.IdempotencyKeys.SingleAsync(ct)).Status.ShouldBe(IdempotencyKeyStatus.InProgress);
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options);
}
