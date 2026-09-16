using System.Buffers;
using System.Text.Json;

namespace Flow.Epub.Corpus;

/// <summary>Writes versioned gate evidence without paths, book text, or rendered HTML.</summary>
public static class EpubLargePublicationGateReportJsonSerializer
{
    public static byte[] Serialize(
        EpubLargePublicationGateReport report,
        bool includeNonDeterministicEnvironment = false)
    {
        ArgumentNullException.ThrowIfNull(report);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", EpubLargePublicationGateReport.CurrentFormat);
            WriteOptions(writer, report.Options);
            WriteResult(writer, report.Result);
            WriteEnvironment(writer, report, includeNonDeterministicEnvironment);
            writer.WriteEndObject();
            writer.Flush();
        }

        return Normalize(buffer.WrittenSpan);
    }

    public static async Task WriteAtomicallyAsync(
        EpubLargePublicationGateReport report,
        string outputPath,
        bool includeNonDeterministicEnvironment = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The gate report path must have a parent directory.", nameof(outputPath));
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

    private static void WriteOptions(Utf8JsonWriter writer, EpubLargePublicationGateOptions options)
    {
        writer.WriteStartObject("options");
        writer.WriteString("candidateId", options.CandidateId.Value);
        writer.WriteString("expectedSourceSha256", options.ExpectedSourceSha256.Value);
        writer.WriteNumber("repetitionCount", options.RepetitionCount);
        writer.WriteBoolean("requireHumanReview", options.RequireHumanReview);
        writer.WriteEndObject();
    }

    private static void WriteResult(Utf8JsonWriter writer, EpubLargePublicationGateResult result)
    {
        writer.WriteStartObject("result");
        writer.WriteString("status", Token(result.Status));
        WriteEvidence(writer, result.Evidence);
        writer.WriteStartArray("phases");
        foreach (var phase in result.Phases)
        {
            writer.WriteStartObject();
            writer.WriteString("phase", Token(phase.Kind));
            writer.WriteString("status", Token(phase.Status));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        WriteChecks(writer, "automaticChecks", result.AutomaticChecks);
        WriteChecks(writer, "humanReviewItems", result.HumanReviewItems);
        writer.WriteStartArray("diagnostics");
        foreach (var diagnostic in result.Diagnostics)
        {
            writer.WriteStartObject();
            writer.WriteString("code", diagnostic.Code);
            writer.WriteString("severity", Token(diagnostic.Severity));
            writer.WriteString("phase", Token(diagnostic.Phase));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WriteEvidence(Utf8JsonWriter writer, EpubLargePublicationGateEvidence evidence)
    {
        writer.WriteStartObject("deterministicEvidence");
        if (evidence.EpubVersion is { } epubVersion)
        {
            writer.WriteString("epubVersion", Token(epubVersion));
        }
        else
        {
            writer.WriteNull("epubVersion");
        }

        writer.WriteNumber("sourceEpubBytes", evidence.SourceEpubBytes);
        writer.WriteNumber("manifestItemCount", evidence.ManifestItemCount);
        writer.WriteNumber("spineItemCount", evidence.SpineItemCount);
        writer.WriteNumber("chapterCount", evidence.ChapterCount);
        writer.WriteNumber("importedNodeCount", evidence.ImportedNodeCount);
        writer.WriteNumber("importedAssetCount", evidence.ImportedAssetCount);
        writer.WriteNumber("importedCharacterCount", evidence.ImportedCharacterCount);
        writer.WriteNumber("validationDiagnosticCount", evidence.ValidationDiagnosticCount);
        writer.WriteNumber("fidelitySourceUnitCount", evidence.FidelitySourceUnitCount);
        writer.WriteNumber("fidelityLostUnitCount", evidence.FidelityLostUnitCount);
        writer.WriteNumber("flowJsonBytes", evidence.FlowJsonBytes);
        WriteOptionalHash(writer, "canonicalHash", evidence.CanonicalHash);
        WriteOptionalHash(writer, "readingOrderHash", evidence.ReadingOrderHash);
        WriteOptionalHash(writer, "anchorSetHash", evidence.AnchorSetHash);
        writer.WriteNumber("mobileLayoutNodeCount", evidence.MobileLayoutNodeCount);
        writer.WriteNumber("mobileHtmlFileCount", evidence.MobileHtmlFileCount);
        writer.WriteNumber("mobileHtmlBytes", evidence.MobileHtmlBytes);
        writer.WriteNumber("desktopLayoutNodeCount", evidence.DesktopLayoutNodeCount);
        writer.WriteNumber("desktopHtmlFileCount", evidence.DesktopHtmlFileCount);
        writer.WriteNumber("desktopHtmlBytes", evidence.DesktopHtmlBytes);
        writer.WriteEndObject();
    }

    private static void WriteChecks(
        Utf8JsonWriter writer,
        string propertyName,
        IEnumerable<EpubLargePublicationGateCheck> checks)
    {
        writer.WriteStartArray(propertyName);
        foreach (var check in checks)
        {
            writer.WriteStartObject();
            writer.WriteString("id", check.Id);
            writer.WriteString("phase", Token(check.Phase));
            writer.WriteString("status", Token(check.Status));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteEnvironment(
        Utf8JsonWriter writer,
        EpubLargePublicationGateReport report,
        bool include)
    {
        writer.WriteStartObject("nonDeterministicEnvironment");
        writer.WriteBoolean("included", include);
        writer.WriteString("comparisonPolicy", "excluded-from-deterministic-comparison");
        if (include && report.Result.EnvironmentObservations is { } observations)
        {
            writer.WriteNumber("totalDurationTicks", observations.TotalDurationTicks);
            writer.WriteNumber("approximatePeakManagedBytes", observations.ApproximatePeakManagedBytes);
            writer.WriteNumber("approximatePeakWorkingSetBytes", observations.ApproximatePeakWorkingSetBytes);
            writer.WriteStartArray("phases");
            foreach (var phase in observations.Phases)
            {
                writer.WriteStartObject();
                writer.WriteString("phase", Token(phase.Phase));
                writer.WriteNumber("durationTicks", phase.DurationTicks);
                writer.WriteNumber("approximateManagedBytes", phase.ApproximateManagedBytes);
                writer.WriteNumber("approximateWorkingSetBytes", phase.ApproximateWorkingSetBytes);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    private static void WriteOptionalHash(Utf8JsonWriter writer, string propertyName, Flow.Epub.EpubCorpusSha256? value)
    {
        if (value is { } hash)
        {
            writer.WriteString(propertyName, hash.Value);
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
