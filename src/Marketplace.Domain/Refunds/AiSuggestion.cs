using Marketplace.Domain.Common;

namespace Marketplace.Domain.Refunds;

public enum AiSuggestionStatus
{
    Valid,
    InvalidOutput,
    ProviderError,
    Skipped,
}

// One row per advisor call, including failures (section 12.6). Built by the analyze handler in T-072.
public sealed class AiSuggestion : Entity
{
    private AiSuggestion(Guid refundRequestId, string model, string promptVersion, string inputFacts, AiSuggestionStatus validationStatus, DateTimeOffset createdAt)
    {
        RefundRequestId = refundRequestId;
        Model = model;
        PromptVersion = promptVersion;
        InputFacts = inputFacts;
        ValidationStatus = validationStatus;
        CreatedAt = createdAt;
    }

    public Guid RefundRequestId { get; private set; }

    public string Model { get; private set; }

    public string PromptVersion { get; private set; }

    public string InputFacts { get; private set; }

    public string? RawOutput { get; private set; }

    public string? ParsedOutput { get; private set; }

    public AiSuggestionStatus ValidationStatus { get; private set; }

    public int? InputTokens { get; private set; }

    public int? OutputTokens { get; private set; }

    public int? LatencyMs { get; private set; }

    public string? Error { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static AiSuggestion Record(
        Guid refundRequestId, string model, string promptVersion, string inputFacts, AiSuggestionStatus status, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(promptVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputFacts);

        return new AiSuggestion(refundRequestId, model, promptVersion, inputFacts, status, now);
    }

    // The call's outcome, stored for every status including failures (section 12.6).
    public AiSuggestion WithOutcome(string? rawOutput, string? parsedOutput, int? inputTokens, int? outputTokens, int? latencyMs, string? error)
    {
        RawOutput = rawOutput;
        ParsedOutput = parsedOutput;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        LatencyMs = latencyMs;
        Error = error is { Length: > 1000 } ? error[..1000] : error;
        return this;
    }
}
