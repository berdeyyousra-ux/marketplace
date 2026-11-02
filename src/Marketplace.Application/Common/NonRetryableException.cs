namespace Marketplace.Application.Common;

// Thrown by an outbox handler when retrying cannot help, for example a payment amount mismatch (section 9.6).
// The message is dead-lettered immediately instead of retried (section 10.3).
public sealed class NonRetryableException : Exception
{
    public NonRetryableException()
    {
    }

    public NonRetryableException(string message)
        : base(message)
    {
    }

    public NonRetryableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
