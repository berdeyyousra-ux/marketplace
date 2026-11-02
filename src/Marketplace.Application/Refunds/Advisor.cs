namespace Marketplace.Application.Refunds;

// Section 12.2: built by deterministic code. The model sees nothing else, in particular no names, emails or
// payment details (section 12.1).
public sealed record RefundFacts(
    Guid RequestId,
    string BuyerMessage,
    string ProductTitle,
    string CategorySlug,
    string Price,
    int DaysSincePurchase,
    int WindowDays,
    bool WithinWindow,
    int DownloadCount,
    int BuyerRefundsLast180Days,
    decimal SellerRefundRateLast90Days,
    string PolicyVerdict);

public enum AdvisorStatus
{
    Valid,
    InvalidOutput,
    ProviderError,
    Skipped,
}

// Section 12.4, after validation and flag merging.
public sealed record RefundSuggestion(
    string Category, string Summary, string RecommendedAction, decimal Confidence, IReadOnlyList<string> Reasons, IReadOnlyList<string> Flags);

// Section 12.3.
public sealed record AdvisorResult(
    AdvisorStatus Status,
    RefundSuggestion? Suggestion,
    string? RawOutput,
    string Model,
    string PromptVersion,
    int? InputTokens,
    int? OutputTokens,
    int LatencyMs,
    string? Error);

public interface IRefundAdvisor
{
    Task<AdvisorResult> AnalyzeAsync(RefundFacts facts, CancellationToken cancellationToken);
}

// Section 12.3: one call, system and user text in, text and token counts out. The provider, model and key come
// from configuration (Ai:Provider, Ai:Model, Ai:ApiKey).
public interface ILlmClient
{
    string Model { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
}

public sealed record LlmRequest(string System, string User, int MaxOutputTokens);

// StopReason is the provider's reason for ending, such as end_turn, max_tokens or refusal.
public sealed record LlmResponse(string Text, int? InputTokens, int? OutputTokens, string? StopReason);

// The provider could not be reached or answered with a timeout, 429 or 5xx after retries (FM-16).
public sealed class LlmUnavailableException : Exception
{
    public LlmUnavailableException()
    {
    }

    public LlmUnavailableException(string message)
        : base(message)
    {
    }

    public LlmUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    // How long a 429 asked the caller to wait, when it said.
    public TimeSpan? RetryAfter { get; init; }
}
