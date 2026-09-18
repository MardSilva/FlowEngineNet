using System.Buffers;
using System.Text.Json;
using Flow.Documents;

namespace Flow.Security;

/// <summary>Implements the legacy <c>flow-c14n-0.1</c> projection for compatibility checks.</summary>
public sealed class FlowDocumentCanonicalizerV01 : IDocumentCanonicalizer
{
    /// <summary>The legacy profile name.</summary>
    public const string Version = "flow-c14n-0.1";

    /// <inheritdoc />
    public string CanonicalizationVersion => Version;

    /// <inheritdoc />
    public byte[] Canonicalize(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false });
        CanonicalDocumentWriter.Write(writer, document, Version, includeFigureLinks: false);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }
}
