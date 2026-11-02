namespace Marketplace.Infrastructure.Ai;

// Section 16 "Ai" and section 12.6. Model names are never hard-coded: Ai:Model is required for a real provider.
public sealed class AiOptions
{
    public const string SectionName = "Ai";
    public const string FakeName = "Fake";
    public const string AnthropicName = "Anthropic";

    public string Provider { get; set; } = FakeName;

    public string? Model { get; set; }

    public string? ApiKey { get; set; }

    public Uri BaseUrl { get; set; } = new("https://api.anthropic.com");

    public bool Enabled { get; set; } = true;

    public int DailyRequestCap { get; set; } = 200;

    // Current models think before answering, and thinking counts toward this limit, so it is higher than the
    // spec's 500 (changelog 2026-11-02).
    public int MaxOutputTokens { get; set; } = 2000;

    // Sent as output_config.effort when set; low suits a short classification.
    public string? Effort { get; set; } = "low";
}
