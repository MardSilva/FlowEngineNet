using System.Buffers;

namespace Flow.Cli;

internal sealed class LfNormalizingWriteStream(Stream destination) : Stream
{
    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => destination.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => destination.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        destination.FlushAsync(cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) =>
        Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IndexOf((byte)'\r') < 0)
        {
            destination.Write(buffer);
            return;
        }

        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var written = RemoveCarriageReturns(buffer, rented);
            destination.Write(rented.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (buffer.Span.IndexOf((byte)'\r') < 0)
        {
            return destination.WriteAsync(buffer, cancellationToken);
        }

        return WriteNormalizedAsync(buffer, cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        // The report writer does not own the caller's destination stream.
        base.Dispose(disposing);
    }

    private async ValueTask WriteNormalizedAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var written = RemoveCarriageReturns(buffer.Span, rented);
            await destination.WriteAsync(rented.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static int RemoveCarriageReturns(ReadOnlySpan<byte> source, Span<byte> destinationBuffer)
    {
        var written = 0;
        foreach (var value in source)
        {
            if (value != (byte)'\r')
            {
                destinationBuffer[written++] = value;
            }
        }

        return written;
    }
}
