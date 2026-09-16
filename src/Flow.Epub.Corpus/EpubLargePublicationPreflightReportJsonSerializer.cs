using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Flow.Epub.Corpus;

/// <summary>Writes deterministic large-publication preflight evidence without physical paths or book metadata.</summary>
public static class EpubLargePublicationPreflightReportJsonSerializer
{
    public static byte[] Serialize(EpubLargePublicationPreflightReport report)
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
            writer.WriteString("format", EpubLargePublicationPreflightReport.CurrentFormat);
            writer.WriteString("status", Token(report.Status));
            WriteOptionalId(writer, "selectedCandidateId", report.SelectedCandidateId);
            writer.WriteStartObject("criteria");
            writer.WriteNumber("minimumLinearSpineItems", report.Criteria.MinimumLinearSpineItems);
            writer.WriteNumber("minimumXhtmlDocuments", report.Criteria.MinimumXhtmlDocuments);
            writer.WriteNumber("minimumXhtmlBytes", report.Criteria.MinimumXhtmlBytes);
            writer.WriteNumber("minimumSpineItemsForByteEvidence", report.Criteria.MinimumSpineItemsForByteEvidence);
            writer.WriteNumber("minimumResourceClasses", report.Criteria.MinimumResourceClasses);
            writer.WriteEndObject();
            writer.WriteStartArray("diagnostics");
            foreach (var diagnostic in report.Diagnostics)
            {
                WriteDiagnostic(writer, diagnostic);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("candidates");
            foreach (var candidate in report.Candidates)
            {
                writer.WriteStartObject();
                writer.WriteString("id", candidate.Id.Value);
                writer.WriteString("status", Token(candidate.Status));
                if (candidate.Sha256 is { } sha256)
                {
                    writer.WriteString("sha256", sha256.Value);
                }
                else
                {
                    writer.WriteNull("sha256");
                }

                writer.WriteString("epubVersion", Token(candidate.EpubVersion));
                writer.WriteNumber("manifestItemCount", candidate.ManifestItemCount);
                writer.WriteNumber("spineItemCount", candidate.SpineItemCount);
                writer.WriteNumber("linearSpineItemCount", candidate.LinearSpineItemCount);
                writer.WriteNumber("nonLinearSpineItemCount", candidate.NonLinearSpineItemCount);
                writer.WriteNumber("compressedBytes", candidate.CompressedBytes);
                writer.WriteNumber("uncompressedBytes", candidate.UncompressedBytes);
                WriteResources(writer, candidate.Resources);
                WriteFeatures(writer, candidate.Features);
                writer.WriteStartArray("diagnostics");
                foreach (var diagnostic in candidate.Diagnostics)
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
        EpubLargePublicationPreflightReport report,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The preflight report path must have a parent directory.", nameof(outputPath));
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

    private static void WriteResources(Utf8JsonWriter writer, EpubLargePublicationResourceCounts resources)
    {
        writer.WriteStartObject("resources");
        writer.WriteNumber("xhtml", resources.Xhtml);
        writer.WriteNumber("css", resources.Css);
        writer.WriteNumber("rasterImages", resources.RasterImages);
        writer.WriteNumber("svg", resources.Svg);
        writer.WriteNumber("fonts", resources.Fonts);
        writer.WriteNumber("audio", resources.Audio);
        writer.WriteNumber("other", resources.Other);
        writer.WriteNumber("xhtmlBytes", resources.XhtmlBytes);
        writer.WriteNumber("presentClassCount", resources.PresentClassCount);
        writer.WriteEndObject();
    }

    private static void WriteFeatures(Utf8JsonWriter writer, EpubLargePublicationFeatureEvidence features)
    {
        writer.WriteStartObject("features");
        writer.WriteString("tableOfContents", Token(features.TableOfContents));
        writer.WriteString("links", Token(features.Links));
        writer.WriteString("images", Token(features.Images));
        writer.WriteString("notes", Token(features.Notes));
        writer.WriteString("tables", Token(features.Tables));
        writer.WriteEndObject();
    }

    private static void WriteDiagnostic(
        Utf8JsonWriter writer,
        EpubLargePublicationPreflightDiagnostic diagnostic)
    {
        writer.WriteStartObject();
        writer.WriteString("code", diagnostic.Code);
        writer.WriteString("severity", Token(diagnostic.Severity));
        writer.WriteEndObject();
    }

    private static void WriteOptionalId(
        Utf8JsonWriter writer,
        string propertyName,
        Flow.Epub.EpubCorpusPublicationId? id)
    {
        if (id is { } value)
        {
            writer.WriteString(propertyName, value.Value);
        }
        else
        {
            writer.WriteNull(propertyName);
        }
    }

    private static string Token<T>(T value)
        where T : struct, Enum => string.Concat(value.ToString().Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? $"-{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));

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
