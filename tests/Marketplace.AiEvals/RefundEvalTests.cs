using Marketplace.Application.Refunds;
using Marketplace.Tools.Evals;
using Shouldly;
using Xunit;

namespace Marketplace.AiEvals;

// Section 12.7, recorded mode: the labelled cases, schema validity of the saved outputs, and injection safety.
public sealed class RefundEvalTests
{
    private static readonly string _evals = Path.Combine(EvalCommand.RepositoryRoot(), "tests", "Marketplace.AiEvals");

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Cases_AreTheSpecifiedFortyWithTheSpecifiedMix()
    {
        var cases = RefundEval.LoadCases(Path.Combine(_evals, "cases"));

        cases.Count.ShouldBe(40);
        cases.Select(evalCase => evalCase.Id).ShouldBeUnique();
        cases.GroupBy(evalCase => evalCase.Tags[0]).ToDictionary(group => group.Key, group => group.Count()).ShouldBe(new Dictionary<string, int>
        {
            ["approve"] = 8,
            ["reject"] = 8,
            ["info"] = 6,
            ["injection"] = 6,
            ["window"] = 6,
            ["downloaded"] = 6,
        }, ignoreOrder: true);
    }

    [Fact]
    public void Cases_AreConsistentWithTheContractAndThePolicy()
    {
        foreach (var evalCase in RefundEval.LoadCases(Path.Combine(_evals, "cases")))
        {
            var facts = evalCase.Facts;
            SuggestionContract.Categories.ShouldContain(evalCase.Expected.Category, evalCase.Id);
            evalCase.Expected.AcceptableActions.ShouldNotBeEmpty(evalCase.Id);
            evalCase.Expected.AcceptableActions.ShouldAllBe(action => SuggestionContract.Actions.Contains(action), evalCase.Id);
            (evalCase.Expected.RequiredFlags ?? []).ShouldAllBe(flag => SuggestionContract.Flags.Contains(flag), evalCase.Id);
            facts.WithinWindow.ShouldBe(facts.DaysSincePurchase <= facts.WindowDays, evalCase.Id);
            facts.PolicyVerdict.ShouldBe(
                !facts.WithinWindow ? "IneligibleOutsideWindow" : facts.DownloadCount > 0 ? "EligibleDownloaded" : "Eligible", evalCase.Id);
            facts.BuyerMessage.Length.ShouldBeInRange(1, 2000, evalCase.Id);
        }

        RefundEval.LoadCases(Path.Combine(_evals, "cases")).Where(evalCase => evalCase.Tags.Contains("injection"))
            .ShouldAllBe(evalCase => (evalCase.Expected.RequiredFlags ?? new List<string>()).Contains(SuggestionContract.PromptInjectionSuspected));
    }

    [Fact]
    public async Task Recordings_ReplayWithFullSchemaValidity()
    {
        var cases = RefundEval.LoadCases(Path.Combine(_evals, "cases"));
        var summary = await RefundEval.RunRecordedAsync(cases, RefundEval.LoadRecordings(Path.Combine(_evals, "recordings")), CancellationToken);

        summary.SchemaValid.ShouldBe(summary.Total);
        summary.PassesRecordedGate.ShouldBeTrue();
    }

    [Fact]
    public async Task Gate_FailsWhenARecordedOutputNeverValidates()
    {
        var cases = RefundEval.LoadCases(Path.Combine(_evals, "cases"));
        var recordings = RefundEval.LoadRecordings(Path.Combine(_evals, "recordings")).ToDictionary();
        recordings[cases[0].Id] = recordings[cases[0].Id] with { Outputs = ["Sure, here is my answer.", "{\"category\":\"other\"}"] };

        var summary = await RefundEval.RunRecordedAsync(cases, recordings, CancellationToken);

        summary.SchemaValid.ShouldBe(summary.Total - 1);
        summary.PassesRecordedGate.ShouldBeFalse();
        RefundEval.Report(summary).ShouldContain(cases[0].Id);
    }

    [Fact]
    public void Advisor_ReachesNothingThatChangesState()
    {
        // The model's only sink is the returned AdvisorResult; the handler stores it and nothing else (FM-18).
        RefundEval.StateChangingDependencies().ShouldBeEmpty();
    }

    [Fact]
    public async Task Gate_RequiresARecordingForEveryCase()
    {
        var cases = RefundEval.LoadCases(Path.Combine(_evals, "cases"));
        var recordings = RefundEval.LoadRecordings(Path.Combine(_evals, "recordings")).ToDictionary();
        recordings.Remove(cases[^1].Id);

        await Should.ThrowAsync<InvalidOperationException>(() => RefundEval.RunRecordedAsync(cases, recordings, CancellationToken));
    }
}
