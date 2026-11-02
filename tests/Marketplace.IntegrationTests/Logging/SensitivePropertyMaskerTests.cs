using Marketplace.Infrastructure.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Shouldly;

namespace Marketplace.IntegrationTests.Logging;

// Section 11.3. These need no containers; the Api's types are only reachable from this test project (section 5.2).
public sealed class SensitivePropertyMaskerTests
{
    [Theory]
    [InlineData("Password")]
    [InlineData("refreshToken")]
    [InlineData("ClientSecret")]
    [InlineData("Authorization")]
    [InlineData("apiKey")]
    public void Enrich_SensitiveTopLevelProperty_IsMasked(string name)
    {
        var logEvent = Capture(logger => logger.Information("Value {" + name + "}", "do-not-log-me"));

        Rendered(logEvent.Properties[name]).ShouldBe(SensitivePropertyMasker.Mask);
        logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture).ShouldNotContain("do-not-log-me");
    }

    [Fact]
    public void Enrich_NestedSensitiveProperty_IsMasked()
    {
        var logEvent = Capture(logger => logger.Information(
            "Login {@Request}", new { Email = "buyer@example.test", Password = "do-not-log-me", Session = new { AccessToken = "do-not-log-me" } }));

        var rendered = logEvent.Properties["Request"].ToString();

        rendered.ShouldContain("buyer@example.test");
        rendered.ShouldNotContain("do-not-log-me");
    }

    [Fact]
    public void Enrich_SensitiveDictionaryKey_IsMasked()
    {
        var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer do-not-log-me", ["Accept"] = "application/json" };

        var logEvent = Capture(logger => logger.Information("Headers {Headers}", headers));

        logEvent.Properties["Headers"].ToString().ShouldNotContain("do-not-log-me");
        logEvent.Properties["Headers"].ToString().ShouldContain("application/json");
    }

    [Fact]
    public void Enrich_OrdinaryProperty_IsUnchanged()
    {
        var logEvent = Capture(logger => logger.Information("Order {OrderId}", "order-123"));

        Rendered(logEvent.Properties["OrderId"]).ShouldBe("order-123");
    }

    private static string Rendered(LogEventPropertyValue value) => ((ScalarValue)value).Value?.ToString() ?? string.Empty;

    private static LogEvent Capture(Action<ILogger> write)
    {
        var sink = new CollectingSink();
        using var logger = new LoggerConfiguration().Enrich.With<SensitivePropertyMasker>().WriteTo.Sink(sink).CreateLogger();
        write(logger);
        return sink.Events.Single();
    }

    private sealed class CollectingSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}
