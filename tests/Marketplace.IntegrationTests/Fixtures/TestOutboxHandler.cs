using System.Collections.Concurrent;
using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Application.Common;

namespace Marketplace.IntegrationTests.Fixtures;

public sealed record TestOutboxPayload(string Key, string Mode);

// Registered by MarketplaceWebFactory. Each message's payload says how the handler should behave.
public sealed class TestOutboxHandler : IOutboxHandler
{
    public const string Type = "Test.Message";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public string MessageType => Type;

    public ConcurrentDictionary<string, int> Calls { get; } = new();

    public ConcurrentDictionary<string, string> DeadLettered { get; } = new();

    public Task HandleAsync(string payload, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<TestOutboxPayload>(payload, _json)!;
        Calls.AddOrUpdate(message.Key, 1, (_, count) => count + 1);

        return message.Mode switch
        {
            "fail" => throw new InvalidOperationException($"Simulated failure for {message.Key}"),
            "nonretryable" => throw new NonRetryableException("Simulated permanent failure"),
            "longerror" => throw new InvalidOperationException(new string('x', 5000)),
            _ => Task.CompletedTask,
        };
    }

    public Task OnDeadLetterAsync(string payload, string reason, CancellationToken cancellationToken)
    {
        var message = JsonSerializer.Deserialize<TestOutboxPayload>(payload, _json)!;
        DeadLettered[message.Key] = reason;
        return Task.CompletedTask;
    }
}
