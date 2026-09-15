using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Flow.Documents;

/// <summary>Reads and writes the deterministic, experimental <c>flow-json-0.1</c> representation.</summary>
public sealed class FlowJsonDocumentSerializer : IFlowDocumentSerializer
{
    /// <inheritdoc />
    public async Task SerializeAsync(
        FlowDocument document,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(destination);

        await using var normalizedDestination = new LfNormalizingWriteStream(destination);
        await using (var writer = new Utf8JsonWriter(
                         normalizedDestination,
                         new JsonWriterOptions
                         {
                             Indented = true,
                             Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                         }))
        {
            FlowJsonWriter.Write(writer, document, cancellationToken);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<FlowDocument> DeserializeAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        try
        {
            using var json = await JsonDocument.ParseAsync(source, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return FlowJsonReader.Read(json.RootElement);
        }
        catch (JsonException exception)
        {
            throw new FlowSerializationException(
                FlowSerializationDiagnosticCodes.InvalidJson,
                $"Invalid .flow.json at line {exception.LineNumber}, byte {exception.BytePositionInLine}: {exception.Message}",
                exception.Path,
                exception.LineNumber,
                exception.BytePositionInLine,
                exception);
        }
        catch (FlowSerializationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            throw new FlowSerializationException(
                FlowSerializationDiagnosticCodes.InvalidDocument,
                $"The .flow.json document is semantically invalid: {exception.Message}",
                innerException: exception);
        }
    }

    private sealed class LfNormalizingWriteStream(Stream destination) : Stream
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
            var firstCarriageReturn = buffer.IndexOf((byte)'\r');
            if (firstCarriageReturn < 0)
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
            // The serializer never owns the caller's destination stream.
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

        private static int RemoveCarriageReturns(ReadOnlySpan<byte> source, Span<byte> destination)
        {
            var written = 0;
            foreach (var value in source)
            {
                if (value != (byte)'\r')
                {
                    destination[written++] = value;
                }
            }

            return written;
        }
    }

}
