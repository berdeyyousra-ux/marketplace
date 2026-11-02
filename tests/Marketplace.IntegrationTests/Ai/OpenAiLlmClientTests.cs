using System.Net;
using System.Text;
using System.Text.Json;
using Marketplace.Application.Refunds;
using Marketplace.Infrastructure.Ai;
using Marketplace.Infrastructure.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Marketplace.IntegrationTests.Ai;

// The OpenAI client against a stub HTTP handler (no key, no network): request shape, parsing, the same
// resilience profile, and the same validation contract through LlmRefundAdvisor.
public sealed class OpenAiLlmClientTests
{
    private const string _validAnswer =
        """{"category":"other","summary":"Unclear request.","recommendedAction":"needs_more_info","confidence":0.4,"reasons":["Little detail."],"flags":[]}""";

    private static readonly LlmRequest _request = new("system text", "user text", 2000);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SendsAChatCompletionAndReadsTheAnswerAndUsage()
    {
        var handler = new StubHandler(_ => Completion("{\"a\":1}", "stop"));

        var response = await Client(handler, reasoningEffort: "low").CompleteAsync(_request, CancellationToken);

        (response.Text, response.InputTokens, response.OutputTokens, response.StopReason).ShouldBe(("{\"a\":1}", 210, 34, "stop"));
        var sent = handler.Requests.ShouldHaveSingleItem();
        sent.Uri.ShouldBe(new Uri("https://api.openai.com/v1/chat/completions"));
        sent.Authorization.ShouldBe("Bearer test-key");
        using var body = JsonDocument.Parse(sent.Body);
        var root = body.RootElement;
        root.GetProperty("model").GetString().ShouldBe("configured-model");
        root.GetProperty("max_completion_tokens").GetInt32().ShouldBe(2000);
        root.GetProperty("messages")[0].GetProperty("role").GetString().ShouldBe("system");
        root.GetProperty("messages")[0].GetProperty("content").GetString().ShouldBe("system text");
        root.GetProperty("messages")[1].GetProperty("role").GetString().ShouldBe("user");
        root.GetProperty("messages")[1].GetProperty("content").GetString().ShouldBe("user text");
        root.GetProperty("response_format").GetProperty("type").GetString().ShouldBe("json_object");
        root.GetProperty("reasoning_effort").GetString().ShouldBe("low");
        root.TryGetProperty("temperature", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task JsonSchema_IsSentInStrictMode()
    {
        var handler = new StubHandler(_ => Completion("{}", "stop"));

        await Client(handler).CompleteAsync(_request with { JsonSchema = SuggestionContract.JsonSchema() }, CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests.Single().Body);
        var format = body.RootElement.GetProperty("response_format");
        format.GetProperty("type").GetString().ShouldBe("json_schema");
        var schema = format.GetProperty("json_schema");
        schema.GetProperty("strict").GetBoolean().ShouldBeTrue();
        schema.GetProperty("schema").GetProperty("additionalProperties").GetBoolean().ShouldBeFalse();
        schema.GetProperty("schema").GetProperty("required").EnumerateArray().Select(item => item.GetString())
            .ShouldBe(["category", "summary", "recommendedAction", "confidence", "reasons", "flags"]);
        schema.GetProperty("schema").GetProperty("properties").GetProperty("flags").GetProperty("items").GetProperty("enum").GetArrayLength()
            .ShouldBe(SuggestionContract.Flags.Count);
    }

    [Fact]
    public async Task ReasoningEffort_IsLeftOutUnlessConfigured()
    {
        var handler = new StubHandler(_ => Completion("{}", "stop"));

        await Client(handler).CompleteAsync(_request, CancellationToken);

        using var body = JsonDocument.Parse(handler.Requests.Single().Body);
        body.RootElement.TryGetProperty("reasoning_effort", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Refusal_ComesBackAsAnEmptyAnswerWithStopReasonRefusal()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"choices":[{"index":0,"message":{"role":"assistant","content":null,"refusal":"I can't help with that."},"finish_reason":"stop"}],"usage":{"prompt_tokens":5,"completion_tokens":6}}"""));

        var response = await Client(handler).CompleteAsync(_request, CancellationToken);

        (response.Text, response.StopReason).ShouldBe((string.Empty, "refusal"));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task TransientStatus_IsRetriedOnce(int status)
    {
        var calls = 0;
        var handler = new StubHandler(_ => ++calls == 1
            ? Json((HttpStatusCode)status, """{"error":{"message":"busy","type":"server_error"}}""")
            : Completion("ok", "stop"));

        (await Client(handler).CompleteAsync(_request, CancellationToken)).Text.ShouldBe("ok");
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task UnreachableAfterTheRetry_ThrowsLlmUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        await Should.ThrowAsync<LlmUnavailableException>(() => Client(handler).CompleteAsync(_request, CancellationToken));
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task ClientError_IsNotRetried()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Unauthorized, """{"error":{"message":"Incorrect API key provided.","type":"invalid_request_error"}}"""));

        await Should.ThrowAsync<InvalidOperationException>(() => Client(handler).CompleteAsync(_request, CancellationToken));
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Advisor_AppliesTheSameContract_RetryingAFencedAnswer()
    {
        var calls = 0;
        var handler = new StubHandler(_ => ++calls == 1 ? Completion("```json\n" + _validAnswer + "\n```", "stop") : Completion(_validAnswer, "stop"));
        var advisor = new LlmRefundAdvisor(
            Client(handler), Options.Create(new AiOptions()), new FakeTimeProvider(), NullLogger<LlmRefundAdvisor>.Instance);

        var result = await advisor.AnalyzeAsync(
            new RefundFacts(Guid.NewGuid(), "Not sure.", "Template", "templates", "9.99 EUR", 1, 14, true, 0, 0, 0m, "Eligible"), CancellationToken);

        result.Status.ShouldBe(AdvisorStatus.Valid);
        (result.Model, result.InputTokens, result.OutputTokens).ShouldBe(("configured-model", 420, 68));
        calls.ShouldBe(2);
    }

    private static OpenAiLlmClient Client(StubHandler handler, string? reasoningEffort = null)
    {
        var services = new ServiceCollection();
        services.AddOptions<ResilienceSettings>(ResilienceSettings.Llm).Configure(settings =>
        {
            settings.AttemptTimeout = TimeSpan.FromSeconds(2);
            settings.TotalTimeout = TimeSpan.FromSeconds(5);
            settings.MaxRetries = 1;
            settings.BaseDelay = TimeSpan.FromMilliseconds(1);
        });
        var monitor = services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<ResilienceSettings>>();
        var options = Options.Create(new AiOptions
        {
            Provider = AiOptions.OpenAiName,
            Model = "configured-model",
            ApiKey = "test-key",
            OpenAiReasoningEffort = reasoningEffort,
        });
        return new OpenAiLlmClient(new HttpClient(handler), options, monitor);
    }

    private static HttpResponseMessage Completion(string content, string finishReason) => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
    {
        id = "chatcmpl-1",
        @object = "chat.completion",
        choices = new[] { new { index = 0, message = new { role = "assistant", content, refusal = (string?)null }, finish_reason = finishReason } },
        usage = new { prompt_tokens = 210, completion_tokens = 34, total_tokens = 244 },
    }));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record SentRequest(Uri Uri, string? Authorization, string Body);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<SentRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new SentRequest(
                request.RequestUri!,
                request.Headers.Authorization?.ToString(),
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }
}
