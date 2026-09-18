using System.Buffers;
using System.Text.Json;

namespace Flow.Epub.Corpus;

/// <summary>Writes deterministic private qualification evidence without editorial identity or paths.</summary>
public static partial class EpubPrivateQualificationReportJsonSerializer
{
    public static byte[] Serialize(EpubPrivateQualificationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", EpubPrivateQualificationReport.CurrentFormat);
            writer.WriteBoolean("deterministicAcrossRepeatedRuns", report.DeterministicAcrossRepeatedRuns);
            WriteSummary(writer, report.Summary);
            writer.WriteStartArray("publications");
            foreach (var publication in report.Publications)
            {
                WritePublication(writer, publication);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return Normalize(buffer.WrittenSpan);
    }

    public static async Task WriteAtomicallyAsync(
        EpubPrivateQualificationReport report,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The qualification report path must have a parent directory.", nameof(outputPath));
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

    private static void WriteSummary(Utf8JsonWriter writer, EpubPrivateQualificationSummary summary)
    {
        writer.WriteStartObject("summary");
        writer.WriteNumber("total", summary.Total);
        writer.WriteNumber("eligible", summary.Eligible);
        writer.WriteNumber("passed", summary.Passed);
        writer.WriteNumber("failed", summary.Failed);
        writer.WriteNumber("inconclusive", summary.Inconclusive);
        writer.WriteNumber("nondeterministic", summary.Nondeterministic);
        writer.WriteNumber("skipped", summary.Skipped);
        writer.WriteEndObject();
    }

    private static void WritePublication(Utf8JsonWriter writer, EpubPrivateQualificationItem publication)
    {
        writer.WriteStartObject();
        writer.WriteString("id", publication.Id.Value);
        if (publication.SourceSha256 is { } sha256)
        {
            writer.WriteString("sourceSha256", sha256.Value);
        }
        else
        {
            writer.WriteNull("sourceSha256");
        }

        writer.WriteString("inventoryStatus", Token(publication.InventoryStatus));
        writer.WriteString("status", Token(publication.Status));
        writer.WriteBoolean("eligible", publication.Eligible);
        writer.WriteBoolean("stableAcrossRepeatedRuns", publication.StableAcrossRepeatedRuns);
        writer.WriteStartArray("completedPhases");
        foreach (var phase in publication.CompletedPhases)
        {
            writer.WriteStringValue(Token(phase));
        }

        writer.WriteEndArray();
        WriteEvidence(writer, publication.Evidence);
        writer.WriteStartArray("diagnostics");
        foreach (var diagnostic in publication.Diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("code", diagnostic.Code);
            writer.WriteString("severity", Token(diagnostic.Severity));
            if (diagnostic.Phase is { } phase)
            {
                writer.WriteString("phase", Token(phase));
            }
            else
            {
                writer.WriteString("phase", "inventory");
            }

            writer.WriteNumber("count", diagnostic.Count);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteEvidence(Utf8JsonWriter writer, EpubPrivateQualificationEvidence evidence)
    {
        writer.WriteStartObject("evidence");
        writer.WriteNumber("manifestItemCount", evidence.ManifestItemCount);
        writer.WriteNumber("spineItemCount", evidence.SpineItemCount);
        writer.WriteNumber("importedNodeCount", evidence.ImportedNodeCount);
        writer.WriteNumber("importedAssetCount", evidence.ImportedAssetCount);
        writer.WriteNumber("validationDiagnosticCount", evidence.ValidationDiagnosticCount);
        writer.WriteNumber("fidelitySourceUnitCount", evidence.FidelitySourceUnitCount);
        writer.WriteNumber("fidelityLostUnitCount", evidence.FidelityLostUnitCount);
        if (evidence.CanonicalHash is null)
        {
            writer.WriteNull("canonicalHash");
        }
        else
        {
            writer.WriteString("canonicalHash", evidence.CanonicalHash);
        }

        writer.WriteNumber("flowJsonBytes", evidence.FlowJsonBytes);
        writer.WriteNumber("mobileLayoutNodeCount", evidence.MobileLayoutNodeCount);
        writer.WriteNumber("desktopLayoutNodeCount", evidence.DesktopLayoutNodeCount);
        writer.WriteNumber("htmlPackageCount", evidence.HtmlPackageCount);
        writer.WriteNumber("htmlFileCount", evidence.HtmlFileCount);
        writer.WriteNumber("htmlBytes", evidence.HtmlBytes);
        writer.WriteNumber("chapterCount", evidence.ChapterCount);
        writer.WriteNumber("headingCount", evidence.HeadingCount);
        writer.WriteNumber("paragraphCount", evidence.ParagraphCount);
        writer.WriteNumber("tableOfContentsEntryCount", evidence.TableOfContentsEntryCount);
        writer.WriteNumber("internalLinkCount", evidence.InternalLinkCount);
        writer.WriteNumber("figureCount", evidence.FigureCount);
        writer.WriteNumber("footnoteCount", evidence.FootnoteCount);
        writer.WriteNumber("footnoteReferenceCount", evidence.FootnoteReferenceCount);
        writer.WriteNumber("tableCount", evidence.TableCount);
        writer.WriteNumber("tableCellCount", evidence.TableCellCount);
        writer.WriteEndObject();
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
