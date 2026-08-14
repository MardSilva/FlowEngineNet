using System.Buffers;
using System.Text.Json;
using Flow.Documents;

namespace Flow.Security;

public sealed class FlowDocumentCanonicalizer : IDocumentCanonicalizer
{
    public const string Version = "flow-c14n-0.1";

    public string CanonicalizationVersion => Version;

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
