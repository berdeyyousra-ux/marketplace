using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Marketplace.Application.Refunds;

// Section 12.4: the only shape the model's answer may take. The prompt's schema text is generated from these
// lists, so the prompt and the validator cannot drift apart.
public static class SuggestionContract
{
    public const int SummaryMaxLength = 400;
    public const int MinReasons = 1;
    public const int MaxReasons = 4;
    public const int ReasonMaxLength = 200;

    public const string OutsideWindow = "outside_window";
    public const string Downloaded = "downloaded";
    public const string PolicyConflict = "policy_conflict";
    public const string PromptInjectionSuspected = "prompt_injection_suspected";
    public const string Approve = "approve";

    public static readonly IReadOnlyList<string> Categories =
        ["not_as_described", "file_defective", "duplicate_purchase", "changed_mind", "accidental_purchase", "does_not_work_for_me", "other"];

    public static readonly IReadOnlyList<string> Actions = [Approve, "reject", "needs_more_info"];

    public static readonly IReadOnlyList<string> Flags =
        ["possible_abuse", OutsideWindow, Downloaded, PolicyConflict, "insufficient_detail", PromptInjectionSuspected];

    private static readonly string[] _properties = ["category", "summary", "recommendedAction", "confidence", "reasons", "flags"];

    // The allowed values and limits, appended to the system prompt verbatim (section 12.5).
    public static string SchemaText()
    {
        static string List(IEnumerable<string> values) => string.Join(", ", values.Select(value => $"\"{value}\""));

        return new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"- category: one of {List(Categories)}")
            .AppendLine(CultureInfo.InvariantCulture, $"- summary: string, 1 to {SummaryMaxLength} characters")
            .AppendLine(CultureInfo.InvariantCulture, $"- recommendedAction: one of {List(Actions)}")
            .AppendLine("- confidence: number from 0 to 1")
            .AppendLine(CultureInfo.InvariantCulture, $"- reasons: array of {MinReasons} to {MaxReasons} strings, each 1 to {ReasonMaxLength} characters")
            .AppendLine(CultureInfo.InvariantCulture, $"- flags: array of zero or more of {List(Flags)}")
            .Append("No other properties are allowed.")
            .ToString();
    }

    // Strict parse: one JSON object and nothing else (no fences, no trailing text), exactly the six properties,
    // every value in range. Returns null and the reason when the output is invalid.
    public static RefundSuggestion? Parse(string? raw, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "The output is empty.";
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException exception)
        {
            error = $"The output is not a single JSON object: {exception.Message}";
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "The output is not a JSON object.";
                return null;
            }

            var names = root.EnumerateObject().Select(property => property.Name).ToList();
            if (names.Count != names.Distinct(StringComparer.Ordinal).Count())
            {
                error = "A property appears more than once.";
                return null;
            }

            var unknown = names.Except(_properties, StringComparer.Ordinal).ToList();
            var missing = _properties.Except(names, StringComparer.Ordinal).ToList();
            if (unknown.Count > 0 || missing.Count > 0)
            {
                error = $"Unknown properties: [{string.Join(", ", unknown)}]; missing properties: [{string.Join(", ", missing)}].";
                return null;
            }

            var category = OneOf(root.GetProperty("category"), Categories);
            var summary = Text(root.GetProperty("summary"), SummaryMaxLength);
            var action = OneOf(root.GetProperty("recommendedAction"), Actions);
            var confidence = root.GetProperty("confidence") is { ValueKind: JsonValueKind.Number } number && number.TryGetDecimal(out var value) && value is >= 0 and <= 1
                ? value
                : (decimal?)null;
            var reasons = Array(root.GetProperty("reasons"), item => Text(item, ReasonMaxLength));
            var flags = Array(root.GetProperty("flags"), item => OneOf(item, Flags));

            error = (category, summary, action, confidence, reasons, flags) switch
            {
                (null, _, _, _, _, _) => "category is not an allowed value.",
                (_, null, _, _, _, _) => $"summary must be 1 to {SummaryMaxLength} characters.",
                (_, _, null, _, _, _) => "recommendedAction is not an allowed value.",
                (_, _, _, null, _, _) => "confidence must be a number from 0 to 1.",
                (_, _, _, _, null, _) => $"reasons must be {MinReasons} to {MaxReasons} strings of 1 to {ReasonMaxLength} characters.",
                (_, _, _, _, { Count: < MinReasons or > MaxReasons }, _) => $"reasons must have {MinReasons} to {MaxReasons} items.",
                (_, _, _, _, _, null) => "flags contains a value that is not allowed.",
                _ => null,
            };

            return error is null
                ? new RefundSuggestion(category!, summary!, action!, confidence!.Value, reasons!, [.. flags!.Distinct(StringComparer.Ordinal)])
                : null;
        }
    }

    // Section 12.4: flags code computes itself are added whatever the model said.
    public static RefundSuggestion MergeFlags(RefundSuggestion suggestion, RefundFacts facts)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        ArgumentNullException.ThrowIfNull(facts);

        var flags = new List<string>(suggestion.Flags);
        void Add(string flag)
        {
            if (!flags.Contains(flag, StringComparer.Ordinal))
            {
                flags.Add(flag);
            }
        }

        if (!facts.WithinWindow)
        {
            Add(OutsideWindow);
        }

        if (facts.DownloadCount > 0)
        {
            Add(Downloaded);
        }

        if (suggestion.RecommendedAction == Approve && facts.PolicyVerdict == "IneligibleOutsideWindow")
        {
            Add(PolicyConflict);
        }

        return suggestion with { Flags = flags };
    }

    private static string? OneOf(JsonElement element, IReadOnlyList<string> allowed) =>
        element.ValueKind == JsonValueKind.String && element.GetString() is { } value && allowed.Contains(value, StringComparer.Ordinal) ? value : null;

    private static string? Text(JsonElement element, int maxLength) =>
        element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } value && value.Length <= maxLength && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static List<string>? Array(JsonElement element, Func<JsonElement, string?> item)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<string>();
        foreach (var entry in element.EnumerateArray())
        {
            if (item(entry) is not { } value)
            {
                return null;
            }

            values.Add(value);
        }

        return values;
    }
}
