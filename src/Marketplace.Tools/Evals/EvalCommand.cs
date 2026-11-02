using Marketplace.Application.Refunds;
using Marketplace.Infrastructure.Ai;
using Marketplace.Infrastructure.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marketplace.Tools.Evals;

// eval-refunds (section 12.7).
//   --mode recorded (default): replays tests/Marketplace.AiEvals/recordings; exit 1 unless every output validates and the
//     advisor reaches nothing that changes state. Runs in CI.
//   --mode live: asks the provider in Ai:* (environment or user secrets), saves its outputs as the new recordings and
//     writes docs/ai-eval-report.md. Costs money: run it deliberately.
public static class EvalCommand
{
    public static async Task<int> RunAsync(Dictionary<string, string> options, TextWriter log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(log);

        var root = RepositoryRoot();
        var mode = options.GetValueOrDefault("mode") ?? "recorded";
        var cases = RefundEval.LoadCases(options.GetValueOrDefault("cases") ?? Path.Combine(root, "tests", "Marketplace.AiEvals", "cases"));
        var recordings = options.GetValueOrDefault("recordings") ?? Path.Combine(root, "tests", "Marketplace.AiEvals", "recordings");

        EvalSummary summary;
        if (mode == "recorded")
        {
            summary = await RefundEval.RunRecordedAsync(cases, RefundEval.LoadRecordings(recordings), cancellationToken);
        }
        else if (mode == "live")
        {
            var (llm, provider) = LiveClient();
            summary = await RefundEval.RunLiveAsync(cases, llm, provider, recordings, log, cancellationToken);
            var reportPath = options.GetValueOrDefault("report") ?? Path.Combine(root, "docs", "ai-eval-report.md");
            await File.WriteAllTextAsync(reportPath, RefundEval.Report(summary), cancellationToken);
            await log.WriteLineAsync($"report written to {reportPath}");
        }
        else
        {
            throw new ArgumentException("--mode must be recorded or live.");
        }

        await log.WriteLineAsync(RefundEval.Report(summary));
        var passed = mode == "recorded" ? summary.PassesRecordedGate : summary.MeetsLiveTargets;
        await log.WriteLineAsync(passed ? "PASSED" : "FAILED");
        return passed ? 0 : 1;
    }

    private static (ILlmClient Client, string Provider) LiveClient()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(EvalCommand).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();
        var ai = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>() ?? new AiOptions();
        if (string.IsNullOrWhiteSpace(ai.Model) || string.IsNullOrWhiteSpace(ai.ApiKey))
        {
            throw new InvalidOperationException("Live mode needs Ai:Provider, Ai:Model and Ai:ApiKey (environment variables or user secrets).");
        }

        var services = new ServiceCollection();
        services.AddOptions<ResilienceSettings>(ResilienceSettings.Llm).Configure(settings =>
        {
            var defaults = ResilienceSettings.LlmDefaults();
            settings.AttemptTimeout = defaults.AttemptTimeout;
            settings.MaxRetries = defaults.MaxRetries;
            settings.TotalTimeout = defaults.TotalTimeout;
        });
        var resilience = services.BuildServiceProvider().GetRequiredService<IOptionsMonitor<ResilienceSettings>>();
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        ILlmClient client = ai.Provider switch
        {
            AiOptions.OpenAiName => new OpenAiLlmClient(http, Options.Create(ai), resilience),
            AiOptions.AnthropicName => new AnthropicLlmClient(http, Options.Create(ai), resilience),
            _ => throw new InvalidOperationException("Live mode needs Ai:Provider set to OpenAi or Anthropic."),
        };
        return (client, ai.Provider);
    }

    public static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Marketplace.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Run eval-refunds inside the repository, or pass --cases and --recordings.");
    }
}
