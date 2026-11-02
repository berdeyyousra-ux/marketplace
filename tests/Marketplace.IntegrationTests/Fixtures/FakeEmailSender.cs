using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Marketplace.Application.Abstractions;

namespace Marketplace.IntegrationTests.Fixtures;

// Section 14.2: captures email instead of sending it. Shared by the collection, so tests filter by recipient.
public sealed partial class FakeEmailSender : IEmailSender
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> To(string address) =>
        Sent.Where(message => string.Equals(message.To, address, StringComparison.OrdinalIgnoreCase)).ToList();

    // The raw token from the most recent link sent to this address.
    public string LatestToken(string address, string path)
    {
        var message = To(address).LastOrDefault(candidate => candidate.TextBody.Contains($"/{path}?token=", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"No {path} email was sent to {address}.");
        return TokenPattern().Match(message.TextBody).Groups[1].Value;
    }

    [GeneratedRegex(@"\?token=([A-Za-z0-9_-]+)")]
    private static partial Regex TokenPattern();
}
