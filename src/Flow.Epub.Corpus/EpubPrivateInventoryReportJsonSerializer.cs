using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Flow.Epub;

namespace Flow.Epub.Corpus;

/// <summary>Writes deterministic private inventory evidence without source paths or editorial metadata.</summary>
public static class EpubPrivateInventoryReportJsonSerializer
{
    public static byte[] Serialize(EpubPrivateInventoryReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            Indented = true,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", EpubPrivateInventoryReport.CurrentFormat);
            WriteSummary(writer, report.Summary);
            writer.WriteStartArray("diagnostics");
            foreach (var diagnostic in report.Diagnostics)
            {
                WriteDiagnostic(writer, diagnostic);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("publications");
            foreach (var publication in report.Publications)
            {
                writer.WriteStartObject();
                writer.WriteString("id", publication.Id.Value);
                if (publication.Sha256 is { } sha256)
                {
                    writer.WriteString("sha256", sha256.Value);
                }
                else
                {
                    writer.WriteNull("sha256");
                }

                writer.WriteString("status", Token(publication.Status));
                writer.WriteString("protection", Token(publication.Protection));
                writer.WriteNumber("copyCount", publication.CopyCount);
                writer.WriteNumber("fileBytes", publication.FileBytes);
                writer.WriteString("epubVersion", Token(publication.EpubVersion));
                writer.WriteStartArray("languages");
                foreach (var language in publication.Languages)
                {
                    writer.WriteStringValue(language);
                }

                writer.WriteEndArray();
                writer.WriteNumber("spineItemCount", publication.SpineItemCount);
                writer.WriteNumber("linearSpineItemCount", publication.LinearSpineItemCount);
                writer.WriteNumber("nonLinearSpineItemCount", publication.NonLinearSpineItemCount);
                WriteResources(writer, publication.Resources);
                writer.WriteStartArray("diagnostics");
                foreach (var diagnostic in publication.Diagnostics)
                {
                    WriteDiagnostic(writer, diagnostic);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return Normalize(buffer.WrittenSpan);
    }

    public static async Task WriteAtomicallyAsync(
        EpubPrivateInventoryReport report,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The inventory report path must have a parent directory.", nameof(outputPath));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, Serialize(report), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void WriteSummary(Utf8JsonWriter writer, EpubPrivateInventorySummary summary)
    {
        writer.WriteStartObject("summary");
        writer.WriteNumber("discoveredFiles", summary.DiscoveredFiles);
        writer.WriteNumber("distinctPublications", summary.DistinctPublications);
        writer.WriteNumber("ready", summary.Ready);
        writer.WriteNumber("reviewRequired", summary.ReviewRequired);
        writer.WriteNumber("protected", summary.Protected);
        writer.WriteNumber("corrupt", summary.Corrupt);
        writer.WriteNumber("unsuitable", summary.Unsuitable);
        writer.WriteEndObject();
    }

    private static void WriteResources(Utf8JsonWriter writer, EpubPrivateInventoryResourceCounts resources)
    {
        writer.WriteStartObject("resources");
        writer.WriteNumber("archiveEntries", resources.ArchiveEntries);
        writer.WriteNumber("manifestItems", resources.ManifestItems);
        writer.WriteNumber("xhtml", resources.Xhtml);
        writer.WriteNumber("css", resources.Css);
        writer.WriteNumber("rasterImages", resources.RasterImages);
        writer.WriteNumber("svg", resources.Svg);
        writer.WriteNumber("fonts", resources.Fonts);
        writer.WriteNumber("audio", resources.Audio);
        writer.WriteNumber("other", resources.Other);
        writer.WriteEndObject();
    }

    private static void WriteDiagnostic(Utf8JsonWriter writer, EpubPrivateInventoryDiagnostic diagnostic)
    {
        writer.WriteStartObject();
        writer.WriteString("code", diagnostic.Code);
        writer.WriteString("severity", Token(diagnostic.Severity));
        writer.WriteEndObject();
    }

    private static string Token<T>(T value)
        where T : struct, Enum => value switch
        {
            EpubVersionFamily.Epub2 => "epub2",
            EpubVersionFamily.Epub3 => "epub3",
            _ => string.Concat(value.ToString().Select((character, index) =>
                char.IsUpper(character) && index > 0
                    ? $"-{char.ToLowerInvariant(character)}"
                    : char.ToLowerInvariant(character).ToString())),
        };

    private static byte[] Normalize(ReadOnlySpan<byte> bytes)
    {
        var normalized = new ArrayBufferWriter<byte>(bytes.Length + 1);
        foreach (var value in bytes)
        {
            if (value != (byte)'\r')
            {
                normalized.GetSpan(1)[0] = value;
                normalized.Advance(1);
            }
        }

        normalized.GetSpan(1)[0] = (byte)'\n';
        normalized.Advance(1);
        return normalized.WrittenSpan.ToArray();
    }
}
