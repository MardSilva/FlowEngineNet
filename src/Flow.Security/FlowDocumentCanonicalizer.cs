using System.Buffers;
using System.Text.Json;
using Flow.Documents;

namespace Flow.Security;

/// <summary>Implements the deterministic <c>flow-c14n-0.1</c> semantic projection.</summary>
public sealed class FlowDocumentCanonicalizer : IDocumentCanonicalizer
{
    /// <summary>The canonicalization profile implemented by this writer.</summary>
    public const string Version = "flow-c14n-0.1";

    /// <inheritdoc />
    public string CanonicalizationVersion => Version;

    /// <inheritdoc />
    public byte[] Canonicalize(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = false });
        CanonicalDocumentWriter.Write(writer, document);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }
}
