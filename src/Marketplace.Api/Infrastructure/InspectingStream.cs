using System.Security.Cryptography;

namespace Marketplace.Api.Infrastructure;

public sealed class FileTooLargeException : Exception
{
    public FileTooLargeException()
        : base("The upload exceeds the size limit.")
    {
    }

    public FileTooLargeException(string message)
        : base(message)
    {
    }

    public FileTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

// Reads an already-sniffed head buffer and then the rest of the upload, counting bytes against the limit and
// computing SHA-256 as the data passes through. Nothing beyond the head is buffered (section 9.2).
public sealed class InspectingStream(byte[] head, int headLength, Stream rest, long maxBytes) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private int _headPosition;

    public long BytesRead { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    public string Sha256Hex() => Convert.ToHexStringLower(_hash.GetCurrentHash());

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int read;
        if (_headPosition < headLength)
        {
            read = Math.Min(buffer.Length, headLength - _headPosition);
            head.AsMemory(_headPosition, read).CopyTo(buffer);
            _headPosition += read;
        }
        else
        {
            read = await rest.ReadAsync(buffer, cancellationToken);
        }

        BytesRead += read;
        if (BytesRead > maxBytes)
        {
            throw new FileTooLargeException();
        }

        _hash.AppendData(buffer.Span[..read]);
        return read;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }
}
