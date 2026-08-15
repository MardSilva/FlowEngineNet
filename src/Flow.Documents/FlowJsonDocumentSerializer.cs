using System.Text.Json;

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

        await using var buffer = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(
                         buffer,
                         new JsonWriterOptions { Indented = true }))
        {
            FlowJsonWriter.Write(writer, document);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var json = buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length));
        await WriteWithLfLineEndingsAsync(json, destination, cancellationToken).ConfigureAwait(false);
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

    private static async Task WriteWithLfLineEndingsAsync(
        ReadOnlyMemory<byte> source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        if (source.Span.IndexOf((byte)'\r') < 0)
        {
            await destination.WriteAsync(source, cancellationToken).ConfigureAwait(false);
            return;
        }

        var normalized = NormalizeLineEndings(source.Span);
        await destination.WriteAsync(normalized, cancellationToken).ConfigureAwait(false);
    }

    private static byte[] NormalizeLineEndings(ReadOnlySpan<byte> source)
    {
        var normalized = new byte[source.Length];
        var written = 0;
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] == '\r' && index + 1 < source.Length && source[index + 1] == '\n')
            {
                normalized[written++] = (byte)'\n';
                index++;
                continue;
            }

            normalized[written++] = source[index];
        }

        Array.Resize(ref normalized, written);
        return normalized;
    }
}
