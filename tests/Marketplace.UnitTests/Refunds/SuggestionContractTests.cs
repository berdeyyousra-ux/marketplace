using Marketplace.Application.Refunds;
using Shouldly;
using Xunit;

namespace Marketplace.UnitTests.Refunds;

// T-071, section 12.4: strict parsing and code-computed flags.
public sealed class SuggestionContractTests
{
    private const string _valid =
        """{"category":"file_defective","summary":"The file will not open.","recommendedAction":"approve","confidence":0.8,"reasons":["The buyer reports a corrupt file."],"flags":[]}""";

    [Fact]
    public void Parse_ValidObject_ReturnsTheSuggestion()
    {
        var suggestion = SuggestionContract.Parse("  " + _valid + "\n", out var error);

        error.ShouldBeNull();
        suggestion.ShouldNotBeNull();
        (suggestion.Category, suggestion.RecommendedAction, suggestion.Confidence).ShouldBe(("file_defective", "approve", 0.8m));
        suggestion.Reasons.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("```json\n" + _valid + "\n```")]
    [InlineData(_valid + " Hope this helps!")]
    [InlineData(_valid + _valid)]
    [InlineData("[" + _valid + "]")]
    [InlineData("")]
    [InlineData("not json")]
    public void Parse_AnythingButOneObject_IsInvalid(string raw) =>
        SuggestionContract.Parse(raw, out _).ShouldBeNull();

    [Theory]
    [InlineData("\"category\":\"file_defective\"", "\"category\":\"refund_now\"")]
    [InlineData("\"recommendedAction\":\"approve\"", "\"recommendedAction\":\"refund\"")]
    [InlineData("\"confidence\":0.8", "\"confidence\":1.2")]
    [InlineData("\"confidence\":0.8", "\"confidence\":-0.1")]
    [InlineData("\"confidence\":0.8", "\"confidence\":\"0.8\"")]
    [InlineData("\"summary\":\"The file will not open.\"", "\"summary\":\"\"")]
    [InlineData("\"reasons\":[\"The buyer reports a corrupt file.\"]", "\"reasons\":[]")]
    [InlineData("\"reasons\":[\"The buyer reports a corrupt file.\"]", "\"reasons\":[\"a\",\"b\",\"c\",\"d\",\"e\"]")]
    [InlineData("\"reasons\":[\"The buyer reports a corrupt file.\"]", "\"reasons\":[1]")]
    [InlineData("\"flags\":[]", "\"flags\":[\"refund_immediately\"]")]
    [InlineData("\"flags\":[]", "\"flags\":[],\"admin\":true")]
    [InlineData(",\"flags\":[]", "")]
    [InlineData("\"flags\":[]", "\"flags\":[],\"flags\":[]")]
    public void Parse_OutOfContractValues_AreInvalid(string from, string to)
    {
        var raw = _valid.Replace(from, to, StringComparison.Ordinal);
        raw.ShouldNotBe(_valid);

        SuggestionContract.Parse(raw, out var error).ShouldBeNull();
        error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Parse_TooLongSummaryOrReason_IsInvalid()
    {
        SuggestionContract.Parse(_valid.Replace("The file will not open.", new string('x', 401), StringComparison.Ordinal), out _).ShouldBeNull();
        SuggestionContract.Parse(_valid.Replace("The buyer reports a corrupt file.", new string('x', 201), StringComparison.Ordinal), out _).ShouldBeNull();
    }

    [Fact]
    public void MergeFlags_AddsCodeComputedFlagsAndKeepsTheModelsOwn()
    {
        var suggestion = SuggestionContract.Parse(_valid.Replace("\"flags\":[]", "\"flags\":[\"possible_abuse\",\"possible_abuse\"]", StringComparison.Ordinal), out _)!;

        var outside = SuggestionContract.MergeFlags(suggestion, Facts(withinWindow: false, downloads: 2, verdict: "IneligibleOutsideWindow"));
        var inside = SuggestionContract.MergeFlags(suggestion, Facts(withinWindow: true, downloads: 0, verdict: "Eligible"));
        var rejectOutside = SuggestionContract.MergeFlags(suggestion with { RecommendedAction = "reject" }, Facts(false, 0, "IneligibleOutsideWindow"));

        outside.Flags.ShouldBe(["possible_abuse", "outside_window", "downloaded", "policy_conflict"]);
        inside.Flags.ShouldBe(["possible_abuse"]);
        rejectOutside.Flags.ShouldBe(["possible_abuse", "outside_window"]);
    }

    [Fact]
    public void SchemaText_ListsEveryAllowedValue()
    {
        var text = SuggestionContract.SchemaText();

        foreach (var value in SuggestionContract.Categories.Concat(SuggestionContract.Actions).Concat(SuggestionContract.Flags))
        {
            text.ShouldContain($"\"{value}\"");
        }

        text.ShouldContain("No other properties are allowed.");
    }

    private static RefundFacts Facts(bool withinWindow, int downloads, string verdict) =>
        new(Guid.NewGuid(), "It does not work.", "Budget Template", "templates", "19.99 EUR", 3, 14, withinWindow, downloads, 0, 0.02m, verdict);
}
