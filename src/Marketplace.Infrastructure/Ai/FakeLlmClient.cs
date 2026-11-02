using System.Collections.Concurrent;
using Marketplace.Application.Refunds;

namespace Marketplace.Infrastructure.Ai;

// Development and tests (section 14.2). Returns queued responses in order, then the default. Queued exceptions
// are thrown instead, to simulate an unavailable provider (FM-16). Thread-safe.
public sealed class FakeLlmClient : ILlmClient
{
    public const string ModelName = "fake-llm";

    public const string DefaultOutput =
        """{"category":"other","summary":"The buyer asks for a refund.","recommendedAction":"needs_more_info","confidence":0.5,"reasons":["The message gives little detail."],"flags":["insufficient_detail"]}""";

    private readonly ConcurrentQueue<Func<LlmRequest, LlmResponse>> _queue = new();
    private readonly ConcurrentQueue<LlmRequest> _requests = new();

    public string Model => ModelName;

    public IReadOnlyCollection<LlmRequest> Requests => _requests;

    public void Enqueue(string text, string stopReason = "end_turn") =>
        _queue.Enqueue(_ => new LlmResponse(text, 100, 50, stopReason));

    public void EnqueueFailure(Exception exception) => _queue.Enqueue(_ => throw exception);

    public void Reset()
    {
        _queue.Clear();
        _requests.Clear();
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        _requests.Enqueue(request);
        return Task.FromResult(_queue.TryDequeue(out var next) ? next(request) : new LlmResponse(DefaultOutput, 100, 50, "end_turn"));
    }
}
