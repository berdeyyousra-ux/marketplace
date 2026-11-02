using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Marketplace.Application.Common;
using Marketplace.Application.Payments;
using Marketplace.Domain.Common;
using Marketplace.Infrastructure.Payments;
using Marketplace.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Marketplace.IntegrationTests.Payments;

// EP-40, EP-99 and section 9.5 (receive).
public sealed class WebhookReceiveTests(MarketplaceWebFactory factory) : IntegrationTestBase(factory)
{
    private FakePaymentProvider Provider => Factory.Services.GetRequiredService<FakePaymentProvider>();

    [Fact]
    public async Task ValidWebhook_StoresEventAndQueuesProcessing()
    {
        var (headers, body) = Provider.CreateWebhook(PaymentEventType.Succeeded, "pi_receive", Money.Eur(19.99m), "evt_receive_1");

        var response = await SendAsync(headers, body);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var stored = await WithDbAsync(db => db.PaymentEvents.SingleAsync(CancellationToken));
        stored.Provider.ShouldBe("Fake");
        stored.ProviderEventId.ShouldBe("evt_receive_1");
        stored.EventType.ShouldBe("Succeeded");
        stored.ProcessedAt.ShouldBeNull();
        var message = await WithDbAsync(db => db.OutboxMessages.SingleAsync(CancellationToken));
        message.Type.ShouldBe(OutboxMessageTypes.ProcessPaymentEvent);
        message.Payload.ShouldContain(stored.Id.ToString());
    }

    [Fact]
    public async Task FM04_DuplicateDelivery_IsAcceptedButStoredOnce()
    {
        var (headers, body) = Provider.CreateWebhook(PaymentEventType.Succeeded, "pi_dup", Money.Eur(5m), "evt_dup");

        var first = await SendAsync(headers, body);
        var second = await SendAsync(headers, body);

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await WithDbAsync(db => db.PaymentEvents.CountAsync(CancellationToken))).ShouldBe(1);
        (await WithDbAsync(db => db.OutboxMessages.CountAsync(CancellationToken))).ShouldBe(1);
    }

    [Fact]
    public async Task TamperedOrUnsignedWebhook_Returns400AndStoresNothing()
    {
        var (headers, body) = Provider.CreateWebhook(PaymentEventType.Succeeded, "pi_bad", Money.Eur(5m));
        var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(body).Replace("5.00", "0.05", StringComparison.Ordinal));

        var badSignature = await SendAsync(headers, tampered);
        var noSignature = await SendAsync(new Dictionary<string, string>(), body);

        await ShouldHaveCodeAsync(badSignature, "webhook_signature_invalid");
        await ShouldHaveCodeAsync(noSignature, "webhook_signature_invalid");
        (await WithDbAsync(db => db.PaymentEvents.CountAsync(CancellationToken))).ShouldBe(0);
    }

    [Fact]
    public async Task DevCompleteEndpoint_IsNotAvailableOutsideDevelopment()
    {
        var response = await AsAnonymous().PostAsync("/dev/fake-payments/pi_any/complete", content: null, CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<HttpResponseMessage> SendAsync(IReadOnlyDictionary<string, string> headers, byte[] body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/payments") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        foreach (var (name, value) in headers)
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        return await AsAnonymous().SendAsync(request, CancellationToken);
    }

    private static async Task ShouldHaveCodeAsync(HttpResponseMessage response, string code)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString().ShouldBe(code);
    }
}
