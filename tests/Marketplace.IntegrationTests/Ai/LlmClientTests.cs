using System.Net;
using System.Text;
using System.Text.Json;
using Marketplace.Application.Refunds;
using Marketplace.Infrastructure.Ai;
using Marketplace.Infrastructure.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;

namespace Marketplace.IntegrationTests.Ai;

// T-070: the fake client, and the Anthropic client against a stub HTTP handler (no key, no network).
public sealed class LlmClientTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private static readonly LlmRequest _request = new("system text", "user text", 2000);

    [Fact]
    public async Task Fake_ReturnsQueuedResponsesThenTheDefault_AndRecordsRequests()
    {
        var fake = new FakeLlmClient();
        fake.Enqueue("first");
        fake.EnqueueFailure(new LlmUnavailableException("down"));

        (await fake.CompleteAsync(_request, CancellationToken)).Text.ShouldBe("first");
        await Should.ThrowAsync<LlmUnavailableException>(() => fake.CompleteAsync(_request, CancellationToken));
        (await fake.CompleteAsync(_request, CancellationToken)).Text.ShouldBe(FakeLlmClient.DefaultOutput);
        fake.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Anthropic_SendsTheMessagesRequestAndReadsTextAndUsage()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, """
            {"id":"msg_1","type":"message","role":"assistant","stop_reason":"end_turn",
             "content":[{"type":"thinking","thinking":""},{"type":"text","text":"{\"a\":"},{"type":"text","text":"1}"}],
             "usage":{"input_tokens":321,"output_tokens":45}}
            """));
        var client = Client(handler);

        var response = await client.CompleteAsync(_request, CancellationToken);

        (response.Text, response.InputTokens, response.OutputTokens, response.StopReason).ShouldBe(("{\"a\":1}", 321, 45, "end_turn"));
        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Uri.ShouldBe(new Uri("https://api.anthropic.com/v1/messages"));
        sent.Headers["x-api-key"].ShouldBe("test-key");
        sent.Headers["anthropic-version"].ShouldBe("2026-10-21");
        using var body = JsonDocument.Parse(sent.Body);
        var root = body.RootElement;
        root.GetProperty("model").GetString().ShouldBe("configured-model");
        root.GetProperty("max_tokens").GetInt32().ShouldBe(2000);
        root.GetProperty("system").GetString().ShouldBe("system text");
        root.GetProperty("messages")[0].GetProperty("role").GetString().ShouldBe("user");
        root.GetProperty("messages")[0].GetProperty("content").GetString().ShouldBe("user text");
        root.GetProperty("output_config").GetProperty("effort").GetString().ShouldBe("low");
        root.TryGetProperty("temperature", out _).ShouldBeFalse("API version 2026-10-21 rejects sampling parameters");
    }

    [Theory]
    [InlineData(529)]
    [InlineData(503)]
    [InlineData(429)]
    public async Task Anthropic_RetriesATransientStatusOnce(int status)
    {
        var calls = 0;
        var handler = new StubHandler(_ => ++calls == 1
            ? Json((HttpStatusCode)status, """{"type":"error","error":{"type":"overloaded_error","message":"busy"}}""")
            : Json(HttpStatusCode.OK, """{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}"""));

        (await Client(handler).CompleteAsync(_request, CancellationToken)).Text.ShouldBe("ok");
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task Anthropic_UnavailableAfterTheRetry_ThrowsLlmUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        await Should.ThrowAsync<LlmUnavailableException>(() => Client(handler).CompleteAsync(_request, CancellationToken));
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Anthropic_ClientError_IsNotRetried()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.BadRequest, """{"type":"error","error":{"type":"invalid_request_error","message":"bad"}}"""));

        await Should.ThrowAsync<InvalidOperationException>(() => Client(handler).CompleteAsync(_request, CancellationToken));
        handler.Requests.Count.ShouldBe(1);
    }

    private static AnthropicLlmClient Client(StubHandler handler)
    {
        var services = new ServiceCollection();
        services.AddOptions<ResilienceSettings>(ResilienceSettings.Llm).Configure(settings =>
        {
            settings.AttemptTimeout = TimeSpan.FromSeconds(2);
            settings.TotalTimeout = TimeSpan.FromSeconds(5);
            settings.MaxRetries = 1;
            settings.BaseDelay = TimeSpan.FromMilliseconds(1);
            settings.MaxRetryAfter = TimeSpan.FromMilliseconds(10);
        });
        var monitor = services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<ResilienceSettings>>();
        var options = Options.Create(new AiOptions { Provider = AiOptions.AnthropicName, Model = "configured-model", ApiKey = "test-key" });
        return new AnthropicLlmClient(new HttpClient(handler), options, monitor);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record SentRequest(Uri Uri, Dictionary<string, string> Headers, string Body);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(
                request.RequestUri!,
                request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase),
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }
}
