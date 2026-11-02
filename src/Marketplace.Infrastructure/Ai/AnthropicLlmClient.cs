using System.Net;
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

// Section 12.3: the Messages API over HttpClient, with the section 10.2 LLM policies (20 s per attempt, one retry,
// 45 s in total, circuit breaker). API version 2026-10-21 rejects sampling parameters, so no temperature is sent
// (changelog 2026-11-02). Timeouts, network errors, 429, 5xx and 529 (overloaded) are transient; any other error
// status is not retried.
public sealed class AnthropicLlmClient : ILlmClient
{
    public const string ApiVersion = "2026-10-21";

    public static readonly Uri DefaultBaseUrl = new("https://api.anthropic.com");

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly AiOptions _options;
    private readonly ResiliencePipeline _pipeline;

    public AnthropicLlmClient(HttpClient http, IOptions<AiOptions> options, IOptionsMonitor<ResilienceSettings> resilience, TimeProvider? pipelineTime = null)
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

        var body = new MessagesRequest(
            Model,
            request.MaxOutputTokens,
            request.System,
            [new Message("user", request.User)],
            _options.Effort is { Length: > 0 } effort ? new OutputConfig(effort) : null);
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

    private async Task<LlmResponse> SendAsync(MessagesRequest body, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.BaseUrl ?? DefaultBaseUrl, "v1/messages"))
        {
            Content = JsonContent.Create(body, options: _json),
        };
        message.Headers.Add("x-api-key", _options.ApiKey ?? throw new InvalidOperationException("Ai:ApiKey is not configured."));
        message.Headers.Add("anthropic-version", ApiVersion);

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
                throw new InvalidOperationException($"The LLM provider rejected the request with {status}: {Truncate(detail, 300)}");
            }

            var parsed = await response.Content.ReadFromJsonAsync<MessagesResponse>(_json, cancellationToken)
                ?? throw new LlmUnavailableException("The LLM provider returned an empty body.");
            var text = string.Concat((parsed.Content ?? []).Where(block => block.Type == "text").Select(block => block.Text));
            return new LlmResponse(text, parsed.Usage?.InputTokens, parsed.Usage?.OutputTokens, parsed.StopReason);
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    private sealed record MessagesRequest(string Model, int MaxTokens, string System, IReadOnlyList<Message> Messages, OutputConfig? OutputConfig);

    private sealed record Message(string Role, string Content);

    private sealed record OutputConfig(string Effort);

    private sealed record MessagesResponse(IReadOnlyList<ContentBlock>? Content, string? StopReason, Usage? Usage);

    private sealed record ContentBlock(string Type, string? Text);

    private sealed record Usage(int? InputTokens, int? OutputTokens);
}
