using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Flow.Epub;

namespace Flow.Cli;

internal static class EpubImportDiagnosticsJsonWriter
{
    internal static async Task WriteAsync(
        EpubImportResult import,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(destination);

        await using var buffer = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(
                         buffer,
                         new JsonWriterOptions
                         {
                             Indented = true,
                             Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                         }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", "flow-epub-import-diagnostics-0.1");
            writer.WriteBoolean("success", import.IsSuccess && import.Document is not null);
            WriteDocument(writer, import);
            WriteSummary(writer, import.Diagnostics);
            WriteDiagnostics(writer, import.Diagnostics);
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var bytes = buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length));
        await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static void WriteDocument(Utf8JsonWriter writer, EpubImportResult import)
    {
        writer.WritePropertyName("document");
        if (import.Document is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("id", import.Document.Identity.Id.Value);
        writer.WriteString("title", import.Document.Metadata.Title);
        writer.WriteNumber("nodeCount", import.Document.Index.NodeCount);
        writer.WriteNumber("assetCount", import.Document.Assets.Count);
        writer.WriteEndObject();
    }

    private static void WriteSummary(Utf8JsonWriter writer, IEnumerable<EpubDiagnostic> diagnostics)
    {
        var counts = diagnostics
            .GroupBy(static diagnostic => diagnostic.Severity)
            .ToDictionary(static group => group.Key, static group => group.Sum(static item => item.Count));
        writer.WritePropertyName("summary");
        writer.WriteStartObject();
        writer.WriteNumber("information", counts.GetValueOrDefault(EpubDiagnosticSeverity.Information));
        writer.WriteNumber("warnings", counts.GetValueOrDefault(EpubDiagnosticSeverity.Warning));
        writer.WriteNumber("errors", counts.GetValueOrDefault(EpubDiagnosticSeverity.Error));
        writer.WriteEndObject();
    }

    private static void WriteDiagnostics(Utf8JsonWriter writer, IEnumerable<EpubDiagnostic> diagnostics)
    {
        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();
        foreach (var diagnostic in diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("code", diagnostic.Code);
            writer.WriteString("severity", diagnostic.Severity.ToString().ToLowerInvariant());
            writer.WriteString("message", diagnostic.Message);
            writer.WriteNumber("count", diagnostic.Count);
            if (diagnostic.Resource is null)
            {
                writer.WriteNull("resource");
            }
            else
            {
                writer.WriteString("resource", diagnostic.Resource);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
