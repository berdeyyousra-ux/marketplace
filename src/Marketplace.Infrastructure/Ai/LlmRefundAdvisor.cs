using System.Text;
using System.Text.Json;
using Marketplace.Application.Refunds;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marketplace.Infrastructure.Ai;

// Sections 12.3 to 12.5. Builds the prompt, calls the model, validates the answer strictly, retries once on an
// invalid answer, and merges the flags code computes. It never changes state: the caller stores the result.
public sealed partial class LlmRefundAdvisor(ILlmClient llm, IOptions<AiOptions> options, TimeProvider timeProvider, ILogger<LlmRefundAdvisor> logger)
    : IRefundAdvisor
{
    public const string PromptVersion = "refund-advisor.v1";
    public const int MaxAttempts = 2;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<string> _systemPrompt = new(LoadSystemPrompt);
    private static readonly string[] _reservedTags = ["<buyer_message>", "</buyer_message>", "<facts>", "</facts>"];

    public static string SystemPrompt => _systemPrompt.Value;

    public async Task<AdvisorResult> AnalyzeAsync(RefundFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var model = SafeModel();
        if (!options.Value.Enabled)
        {
            return new AdvisorResult(AdvisorStatus.Skipped, null, null, model, PromptVersion, null, null, 0, "AI suggestions are disabled.");
        }

        var request = new LlmRequest(SystemPrompt, UserMessage(facts), options.Value.MaxOutputTokens);
        var started = timeProvider.GetTimestamp();
        int? inputTokens = null;
        int? outputTokens = null;
        string? raw = null;
        string? error = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            LlmResponse response;
            try
            {
                response = await llm.CompleteAsync(request, cancellationToken);
            }
            catch (Exception exception) when (exception is LlmUnavailableException or InvalidOperationException or HttpRequestException)
            {
                // FM-16: the request stays reviewable without a suggestion.
                LogProviderError(logger, facts.RequestId, exception);
                return new AdvisorResult(
                    AdvisorStatus.ProviderError, null, raw, model, PromptVersion, inputTokens, outputTokens, Elapsed(started), exception.Message);
            }

            inputTokens = (inputTokens ?? 0) + (response.InputTokens ?? 0);
            outputTokens = (outputTokens ?? 0) + (response.OutputTokens ?? 0);
            raw = response.Text;
            var suggestion = SuggestionContract.Parse(raw, out error);
            if (suggestion is not null)
            {
                return new AdvisorResult(
                    AdvisorStatus.Valid, SuggestionContract.MergeFlags(suggestion, facts), raw, model, PromptVersion, inputTokens, outputTokens,
                    Elapsed(started), null);
            }

            LogInvalidOutput(logger, facts.RequestId, attempt, response.StopReason, error);
        }

        // FM-17: two invalid answers are stored as InvalidOutput with the last raw output.
        return new AdvisorResult(AdvisorStatus.InvalidOutput, null, raw, model, PromptVersion, inputTokens, outputTokens, Elapsed(started), error);
    }

    // Section 12.5: the facts as JSON, then the buyer text. The request id is for logging only and the buyer text
    // is not repeated inside the facts. Tags that would close or open a section are replaced with a visible
    // placeholder, so buyer text cannot escape <buyer_message>.
    public static string UserMessage(RefundFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var promptFacts = new
        {
            facts.ProductTitle,
            facts.CategorySlug,
            facts.Price,
            facts.DaysSincePurchase,
            facts.WindowDays,
            facts.WithinWindow,
            facts.DownloadCount,
            facts.BuyerRefundsLast180Days,
            facts.SellerRefundRateLast90Days,
            facts.PolicyVerdict,
        };
        return new StringBuilder()
            .Append("<facts>").Append(JsonSerializer.Serialize(promptFacts, _json)).Append("</facts>\n")
            .Append("<buyer_message>").Append(Neutralize(facts.BuyerMessage)).Append("</buyer_message>")
            .ToString();
    }

    public static string Neutralize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var result = text;
        foreach (var tag in _reservedTags)
        {
            result = result.Replace(tag, $"[{tag.Trim('<', '>')} tag removed]", StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string LoadSystemPrompt()
    {
        using var stream = typeof(LlmRefundAdvisor).Assembly.GetManifestResourceStream($"Marketplace.Prompts.{PromptVersion}.txt")
            ?? throw new InvalidOperationException($"The prompt {PromptVersion} is not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().TrimEnd() + "\n" + SuggestionContract.SchemaText();
    }

    private string SafeModel()
    {
        try
        {
            return llm.Model;
        }
        catch (InvalidOperationException)
        {
            return "unconfigured";
        }
    }

    private int Elapsed(long started) => (int)timeProvider.GetElapsedTime(started).TotalMilliseconds;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refund advisor provider error for request {RequestId}")]
    private static partial void LogProviderError(ILogger logger, Guid requestId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refund advisor output invalid for request {RequestId} on attempt {Attempt} (stop reason {StopReason}): {ValidationError}")]
    private static partial void LogInvalidOutput(ILogger logger, Guid requestId, int attempt, string? stopReason, string? validationError);
}
