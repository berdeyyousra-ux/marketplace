using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Application.Common;
using Marketplace.Domain.Ledger;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Refunds;
using Marketplace.Infrastructure.Ai;
using Marketplace.Infrastructure.Outbox;
using Marketplace.Infrastructure.Payments;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Refunds;

// T-073: EP-62 to EP-65, US-30, US-31, FM-12 and FM-20.
public sealed class RefundReviewTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private FakePaymentProvider Provider => Factory.Services.GetRequiredService<FakePaymentProvider>();

    private FakeEmailSender Emails => Factory.Services.GetRequiredService<FakeEmailSender>();

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        Factory.Services.GetRequiredService<FakeLlmClient>().Reset();
        Provider.FailNext(0);
    }

    [Fact]
    public async Task US30_QueueAndDetail_ShowVerdictSuggestionAndHistory()
    {
        var requestId = await SubmittedRequestAsync("queue");
        using var admin = await AsAdminAsync();

        var queue = await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/refund-requests?status=Submitted", CancellationToken);
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/refund-requests/{requestId}", CancellationToken);

        var row = queue.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        row.GetProperty("policyVerdict").GetString().ShouldBe("Eligible");
        row.GetProperty("ai").GetProperty("status").GetString().ShouldBe("Valid");
        row.GetProperty("ai").GetProperty("recommendedAction").GetString().ShouldBe("needs_more_info");
        detail.GetProperty("buyerMessage").GetString().ShouldBe("The file will not open on my laptop.");
        detail.GetProperty("aiSuggestion").GetProperty("model").GetString().ShouldBe(FakeLlmClient.ModelName);
        detail.GetProperty("aiSuggestion").GetProperty("facts").GetProperty("policyVerdict").GetString().ShouldBe("Eligible");
        detail.GetProperty("history").EnumerateArray().Select(entry => entry.GetProperty("action").GetString()).ShouldBe(["refund.requested", "refund.ai_analyzed"]);
        (await admin.GetAsync("/api/v1/admin/refund-requests?status=Nope", CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task US31_Approve_ExecutesTheRefundWithEveryEffect()
    {
        var requestId = await SubmittedRequestAsync("approve");
        using var admin = await AsAdminAsync();

        var decided = await DecideAsync(admin, requestId, "Approved", "The file is corrupt.");
        await ProcessOutboxAsync();

        decided.StatusCode.ShouldBe(HttpStatusCode.OK);
        var request = await WithDbAsync(db => db.RefundRequests.AsNoTracking().SingleAsync(CancellationToken));
        request.Status.ShouldBe(RefundRequestStatus.Completed);
        request.ExecutedAt.ShouldNotBeNull();
        (await WithDbAsync(db => db.Orders.Select(order => order.Status).SingleAsync(CancellationToken))).ShouldBe(OrderStatus.Refunded);
        (await WithDbAsync(db => db.OrderItems.Select(item => item.Status).SingleAsync(CancellationToken))).ShouldBe(OrderItemStatus.Refunded);
        (await WithDbAsync(db => db.Payments.Select(payment => payment.Status).SingleAsync(CancellationToken))).ShouldBe(PaymentStatus.Refunded);
        (await WithDbAsync(db => db.DownloadGrants.Select(grant => grant.RevokedAt).SingleAsync(CancellationToken))).ShouldNotBeNull();
        var ledger = await WithDbAsync(db => db.LedgerEntries.Select(entry => entry.EntryType).ToListAsync(CancellationToken));
        ledger.ShouldBe([LedgerEntryType.Sale, LedgerEntryType.PlatformFee, LedgerEntryType.Refund, LedgerEntryType.FeeRefund], ignoreOrder: true);
        Emails.To("approve-buyer@example.test").Select(message => message.Subject)
            .ShouldContain(subject => subject.Contains("is approved", StringComparison.Ordinal));
        Emails.To("approve-buyer@example.test").Select(message => message.Subject)
            .ShouldContain(subject => subject.Contains("is complete", StringComparison.Ordinal));
        (await WithDbAsync(db => db.AuditLog.CountAsync(entry => entry.Action == "refund.executed", CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public async Task US31_Reject_EmailsTheBuyerAndQueuesNoExecution_AndASecondDecisionIs409()
    {
        var requestId = await SubmittedRequestAsync("reject");
        using var admin = await AsAdminAsync();

        (await DecideAsync(admin, requestId, "Rejected", "Downloaded several times.")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var again = await DecideAsync(admin, requestId, "Approved", "Changed my mind.");
        await ProcessOutboxAsync();

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe("refund_already_decided");
        (await WithDbAsync(db => db.OutboxMessages.CountAsync(message => message.Type == OutboxMessageTypes.ExecuteRefund, CancellationToken))).ShouldBe(0);
        Emails.To("reject-buyer@example.test").ShouldContain(message => message.Subject.StartsWith("Your refund request for", StringComparison.Ordinal));
    }

    [Fact]
    public async Task US31_OutsideWindow_NeedsAnOverrideReasonToApprove()
    {
        var requestId = await SubmittedRequestAsync("override", lateDays: 15);
        using var admin = await AsAdminAsync();

        var withoutReason = await DecideAsync(admin, requestId, "Approved", "Goodwill.");
        var withReason = await DecideAsync(admin, requestId, "Approved", "Goodwill.", "Long-standing customer with a defective file.");

        withoutReason.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await withoutReason.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("code").GetString().ShouldBe("override_reason_required");
        withReason.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await WithDbAsync(db => db.RefundRequests.Select(request => request.OverrideReason).SingleAsync(CancellationToken)))
            .ShouldBe("Long-standing customer with a defective file.");
    }

    [Fact]
    public async Task FM20_TwoAdminsDecidingAtOnce_OneWinsOneGets409()
    {
        var requestId = await SubmittedRequestAsync("race");
        using var first = await AsAdminAsync();
        using var second = WithToken((await SignInAsync(MarketplaceWebFactory.AdminEmail, MarketplaceWebFactory.AdminPassword)).AccessToken);

        var results = await Task.WhenAll(
            DecideAsync(first, requestId, "Approved", "First admin."),
            DecideAsync(second, requestId, "Rejected", "Second admin."));

        results.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.OK, HttpStatusCode.Conflict], ignoreOrder: true);
        (await WithDbAsync(db => db.AuditLog.CountAsync(entry => entry.Action == "refund.approved" || entry.Action == "refund.rejected", CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public async Task FM12_RepeatedProviderFailure_FailsTheRefund_AndAnAdminRetryCompletesIt()
    {
        var requestId = await SubmittedRequestAsync("failing");
        using var admin = await AsAdminAsync();
        Provider.FailNext(10_000);

        await DecideAsync(admin, requestId, "Approved", "Defective file.");
        for (var round = 0; round <= OutboxProcessor.MaxAttempts; round++)
        {
            await ProcessOutboxAsync();
            Time.Advance(TimeSpan.FromHours(2));
        }

        var failed = await WithDbAsync(db => db.RefundRequests.AsNoTracking().SingleAsync(CancellationToken));
        failed.Status.ShouldBe(RefundRequestStatus.Failed);
        failed.FailureReason.ShouldNotBeNullOrWhiteSpace();

        Provider.FailNext(0);
        using var signedIn = WithToken((await SignInAsync(MarketplaceWebFactory.AdminEmail, MarketplaceWebFactory.AdminPassword)).AccessToken);
        var retried = await signedIn.PostAsync($"/api/v1/admin/refund-requests/{requestId}/retry", content: null, CancellationToken);
        await ProcessOutboxAsync();

        retried.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await WithDbAsync(db => db.RefundRequests.Select(request => request.Status).SingleAsync(CancellationToken))).ShouldBe(RefundRequestStatus.Completed);
        (await signedIn.PostAsync($"/api/v1/admin/refund-requests/{requestId}/retry", content: null, CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static Task<HttpResponseMessage> DecideAsync(HttpClient admin, Guid requestId, string decision, string note, string? overrideReason = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/refund-requests/{requestId}/decision")
        {
            Content = JsonContent.Create(new { decision, note, overrideReason }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        return admin.SendAsync(request, CancellationToken);
    }

    private async Task ProcessOutboxAsync()
    {
        var processor = Factory.Services.GetRequiredService<OutboxProcessor>();
        for (var round = 0; round < 10 && await processor.ProcessBatchAsync(CancellationToken) > 0; round++)
        {
        }
    }

    // A paid item with a submitted, analyzed refund request; lateDays moves past the 14-day window first.
    private async Task<Guid> SubmittedRequestAsync(string prefix, int lateDays = 0)
    {
        using var seller = await AsSellerAsync($"{prefix}-seller@example.test");
        var productId = await CreatePublishedProductAsync(seller);
        using var buyer = await AsBuyerAsync($"{prefix}-buyer@example.test");
        var order = await (await CreateOrderAsync(buyer, [productId])).Content.ReadFromJsonAsync<JsonElement>(CancellationToken);
        await PayOrderAsync(buyer, order.GetProperty("id").GetGuid());
        Time.Advance(TimeSpan.FromDays(lateDays));
        using var signedIn = WithToken((await SignInAsync($"{prefix}-buyer@example.test")).AccessToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/refund-requests")
        {
            Content = JsonContent.Create(new { orderItemId = order.GetProperty("items")[0].GetProperty("id").GetGuid(), reason = "The file will not open on my laptop." }),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", Guid.NewGuid().ToString("N"));
        var created = await signedIn.SendAsync(request, CancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        await ProcessOutboxAsync();
        return (await created.Content.ReadFromJsonAsync<JsonElement>(CancellationToken)).GetProperty("id").GetGuid();
    }
}
