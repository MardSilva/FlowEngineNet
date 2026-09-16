using System.Buffers;
using System.Text.Json;

namespace Flow.Epub.Corpus;

/// <summary>Writes deterministic corpus evidence and optionally separated environment observations.</summary>
public static class EpubCorpusExecutionReportJsonSerializer
{
    public static byte[] Serialize(
        EpubCorpusExecutionReport report,
        bool includeNonDeterministicEnvironment = false)
    {
        ArgumentNullException.ThrowIfNull(report);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", EpubCorpusExecutionReport.CurrentFormat);
            writer.WritePropertyName("summary");
            WriteSummary(writer, report.Summary);
            writer.WritePropertyName("publications");
            writer.WriteStartArray();
            foreach (var publication in report.Publications)
            {
                WritePublication(writer, publication);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("nonDeterministicEnvironment");
            writer.WriteStartObject();
            writer.WriteBoolean("included", includeNonDeterministicEnvironment);
            if (includeNonDeterministicEnvironment)
            {
                writer.WriteString("comparisonPolicy", "excluded-from-deterministic-comparison");
                writer.WritePropertyName("publications");
                writer.WriteStartArray();
                foreach (var publication in report.Publications.Where(static item =>
                             item.EnvironmentMetrics is not null || item.EpubCheckEvidence is not null))
                {
                    WriteEnvironment(writer, publication);
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.Flush();
        }

        var normalized = new ArrayBufferWriter<byte>(buffer.WrittenCount + 1);
        foreach (var value in buffer.WrittenSpan)
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

    public static async Task WriteAtomicallyAsync(
        EpubCorpusExecutionReport report,
        string outputPath,
        bool includeNonDeterministicEnvironment = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The report path must have a parent directory.", nameof(outputPath));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(
                    temporaryPath,
                    Serialize(report, includeNonDeterministicEnvironment),
                    cancellationToken)
                .ConfigureAwait(false);
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

    private static void WriteSummary(Utf8JsonWriter writer, EpubCorpusExecutionSummary summary)
    {
        writer.WriteStartObject();
        writer.WriteNumber("total", summary.Total);
        writer.WriteNumber("passed", summary.Passed);
        writer.WriteNumber("failed", summary.Failed);
        writer.WriteNumber("skipped", summary.Skipped);
        writer.WriteNumber("inconclusive", summary.Inconclusive);
        writer.WriteEndObject();
    }

    private static void WritePublication(Utf8JsonWriter writer, EpubCorpusPublicationExecutionResult publication)
    {
        writer.WriteStartObject();
        writer.WriteString("id", publication.Id.Value);
        writer.WriteString("status", ToToken(publication.Status));
        writer.WritePropertyName("completedPhases");
        writer.WriteStartArray();
        foreach (var phase in publication.CompletedPhases)
        {
            writer.WriteStringValue(ToToken(phase));
        }

        writer.WriteEndArray();
        writer.WritePropertyName("evidence");
        WriteEvidence(writer, publication.Evidence);
        writer.WritePropertyName("externalConformance");
        WriteExternalConformance(writer, publication);
        writer.WritePropertyName("diagnostics");
        writer.WriteStartArray();
        foreach (var diagnostic in publication.Diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("code", diagnostic.Code);
            writer.WriteString("severity", ToToken(diagnostic.Severity));
            writer.WriteString("phase", ToToken(diagnostic.Phase));
            writer.WriteString("message", diagnostic.Message);
            WriteOptionalString(writer, "sourceCode", diagnostic.SourceCode);
            WriteOptionalString(writer, "resource", diagnostic.Resource);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteEvidence(Utf8JsonWriter writer, EpubCorpusPublicationEvidence evidence)
    {
        writer.WriteStartObject();
        WriteOptionalString(writer, "epubVersion", evidence.EpubVersion);
        writer.WriteNumber("manifestItemCount", evidence.ManifestItemCount);
        writer.WriteNumber("spineItemCount", evidence.SpineItemCount);
        writer.WriteNumber("importedNodeCount", evidence.ImportedNodeCount);
        writer.WriteNumber("importedAssetCount", evidence.ImportedAssetCount);
        writer.WriteNumber("validationDiagnosticCount", evidence.ValidationDiagnosticCount);
        writer.WriteNumber("fidelitySourceUnitCount", evidence.FidelitySourceUnitCount);
        writer.WriteNumber("fidelityLostUnitCount", evidence.FidelityLostUnitCount);
        WriteOptionalString(writer, "documentId", evidence.DocumentId);
        WriteOptionalString(writer, "canonicalHash", evidence.CanonicalHash);
        writer.WriteNumber("flowJsonBytes", evidence.FlowJsonBytes);
        writer.WriteNumber("mobileLayoutNodeCount", evidence.MobileLayoutNodeCount);
        writer.WriteNumber("desktopLayoutNodeCount", evidence.DesktopLayoutNodeCount);
        writer.WriteNumber("htmlPackageCount", evidence.HtmlPackageCount);
        writer.WriteNumber("htmlFileCount", evidence.HtmlFileCount);
        writer.WriteNumber("htmlBytes", evidence.HtmlBytes);
        writer.WriteEndObject();
    }

    private static void WriteEnvironment(Utf8JsonWriter writer, EpubCorpusPublicationExecutionResult publication)
    {
        writer.WriteStartObject();
        writer.WriteString("id", publication.Id.Value);
        if (publication.EnvironmentMetrics is { } metrics)
        {
            writer.WriteNumber("totalDurationTicks", metrics.TotalDurationTicks);
            writer.WriteNumber("approximatePeakManagedBytes", metrics.ApproximatePeakManagedBytes);
            writer.WriteNumber("archiveEntryCount", metrics.ArchiveEntryCount);
            writer.WriteNumber("compressedBytes", metrics.CompressedBytes);
            writer.WriteNumber("uncompressedBytes", metrics.UncompressedBytes);
            writer.WriteNumber("assetBytes", metrics.AssetBytes);
            writer.WriteNumber("spineDocumentsProcessed", metrics.SpineDocumentsProcessed);
            writer.WriteNumber("nodesProduced", metrics.NodesProduced);
            writer.WriteNumber("charactersProduced", metrics.CharactersProduced);
        }
        if (publication.EpubCheckEvidence is { } external)
        {
            writer.WriteString("epubCheckStandardOutput", external.StandardOutput);
            writer.WriteString("epubCheckStandardError", external.StandardError);
        }

        writer.WriteEndObject();
    }

    private static void WriteExternalConformance(
        Utf8JsonWriter writer,
        EpubCorpusPublicationExecutionResult publication)
    {
        writer.WriteStartObject();
        writer.WriteString("relationship", ToToken(publication.EvidenceRelationship));
        if (publication.EpubCheckEvidence is not { } evidence)
        {
            writer.WriteNull("epubCheck");
            writer.WriteEndObject();
            return;
        }

        writer.WritePropertyName("epubCheck");
        writer.WriteStartObject();
        writer.WriteString("status", ToToken(evidence.Status));
        WriteOptionalString(writer, "toolVersion", evidence.ToolVersion);
        if (evidence.ExitCode is { } exitCode)
        {
            writer.WriteNumber("exitCode", exitCode);
        }
        else
        {
            writer.WriteNull("exitCode");
        }

        writer.WriteNumber("fatalCount", evidence.FatalCount);
        writer.WriteNumber("errorCount", evidence.ErrorCount);
        writer.WriteNumber("warningCount", evidence.WarningCount);
        writer.WriteNumber("usageCount", evidence.UsageCount);
        writer.WritePropertyName("messages");
        writer.WriteStartArray();
        foreach (var message in evidence.Messages)
        {
            writer.WriteStartObject();
            writer.WriteString("code", message.Code);
            writer.WriteString("severity", message.Severity);
            writer.WriteString("message", message.Message);
            WriteOptionalString(writer, "resource", message.Resource);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value);
        }
    }

    private static string ToToken<T>(T value)
        where T : struct, Enum => string.Concat(value.ToString().Select((character, index) =>
            char.IsUpper(character) && index > 0 ? $"-{char.ToLowerInvariant(character)}" : char.ToLowerInvariant(character).ToString()));
}
