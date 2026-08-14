using System.Text.Json;

namespace Flow.Documents;

public sealed class FlowJsonDocumentSerializer : IFlowDocumentSerializer
{
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

        buffer.Position = 0;
        await buffer.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

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
}
