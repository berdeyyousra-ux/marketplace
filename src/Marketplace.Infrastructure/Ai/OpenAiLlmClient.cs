using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marketplace.Application.Refunds;
using Marketplace.Infrastructure.Payments;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Marketplace.Infrastructure.Ai;

// The OpenAI Chat Completions API over HttpClient, selected with Ai:Provider=OpenAi (changelog 2026-11-02). Same
// contract and section 10.2 LLM policies as AnthropicLlmClient: timeouts, network errors, 429 and 5xx are
// transient; any other error status is not retried. The answer is validated by the same strict parser, so the
// json_object response format only makes a valid answer more likely; it is not trusted on its own. No temperature
// is sent: reasoning models accept only the default.
public sealed class OpenAiLlmClient : ILlmClient
{
    public static readonly Uri DefaultBaseUrl = new("https://api.openai.com");

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly AiOptions _options;
    private readonly ResiliencePipeline _pipeline;

    public OpenAiLlmClient(HttpClient http, IOptions<AiOptions> options, IOptionsMonitor<ResilienceSettings> resilience, TimeProvider? pipelineTime = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resilience);

        _http = http;
        _options = options.Value;
        _pipeline = ResiliencePipelines.Build(
            resilience.Get(ResilienceSettings.Llm),
            pipelineTime ?? TimeProvider.System,
            exception => exception is LlmUnavailableException,
            exception => (exception as LlmUnavailableException)?.RetryAfter);
    }

    public string Model => _options.Model ?? throw new InvalidOperationException("Ai:Model is not configured.");

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = new ChatRequest(
            Model,
            [new ChatMessage("system", request.System), new ChatMessage("user", request.User)],
            request.MaxOutputTokens,
            new ResponseFormat("json_object"),
            string.IsNullOrWhiteSpace(_options.OpenAiReasoningEffort) ? null : _options.OpenAiReasoningEffort);
        try
        {
            return await _pipeline.ExecuteAsync(async token => await SendAsync(body, token), cancellationToken);
        }
        catch (TimeoutRejectedException exception)
        {
            throw new LlmUnavailableException("The LLM provider timed out.", exception);
        }
        catch (BrokenCircuitException exception)
        {
            throw new LlmUnavailableException("The LLM provider circuit is open.", exception);
        }
    }

    private async Task<LlmResponse> SendAsync(ChatRequest body, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseUrl ?? DefaultBaseUrl, "v1/chat/completions"))
        {
            Content = JsonContent.Create(body, options: _json),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", _options.ApiKey ?? throw new InvalidOperationException("Ai:ApiKey is not configured."));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new LlmUnavailableException("The LLM provider could not be reached.", exception);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.TooManyRequests || status >= 500)
            {
                throw new LlmUnavailableException($"The LLM provider returned {status}.") { RetryAfter = response.Headers.RetryAfter?.Delta };
            }

            if (!response.IsSuccessStatusCode)
            {
                // A 4xx other than 429 is a request or configuration problem; retrying would not help.
                var detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException($"The LLM provider rejected the request with {status}: {(detail.Length <= 300 ? detail : detail[..300])}");
            }

            var parsed = await response.Content.ReadFromJsonAsync<ChatResponse>(_json, cancellationToken)
                ?? throw new LlmUnavailableException("The LLM provider returned an empty body.");
            var choice = parsed.Choices is { Count: > 0 } choices ? choices[0] : null;

            // A refusal comes back in its own field with no content; it then fails validation like any other answer.
            var stopReason = choice?.Message?.Refusal is not null ? "refusal" : choice?.FinishReason;
            return new LlmResponse(choice?.Message?.Content ?? string.Empty, parsed.Usage?.PromptTokens, parsed.Usage?.CompletionTokens, stopReason);
        }
    }

    private sealed record ChatRequest(
        string Model, IReadOnlyList<ChatMessage> Messages, int MaxCompletionTokens, ResponseFormat ResponseFormat, string? ReasoningEffort);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ResponseFormat(string Type);

    private sealed record ChatResponse(IReadOnlyList<Choice>? Choices, Usage? Usage);

    private sealed record Choice(ResponseMessage? Message, string? FinishReason);

    private sealed record ResponseMessage(string? Content, string? Refusal);

    private sealed record Usage(int? PromptTokens, int? CompletionTokens);
}
