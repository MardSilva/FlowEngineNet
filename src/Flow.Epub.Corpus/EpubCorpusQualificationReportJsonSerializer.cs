using System.Buffers;
using System.Text.Json;

namespace Flow.Epub.Corpus;

/// <summary>Writes repeated-run qualification evidence and the detailed local execution report.</summary>
public static class EpubCorpusQualificationReportJsonSerializer
{
    public const string CurrentFormat = "flow-epub-corpus-qualification-0.1";

    public static byte[] Serialize(EpubCorpusQualificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        using var execution = JsonDocument.Parse(EpubCorpusExecutionReportJsonSerializer.Serialize(
            result.Report,
            includeNonDeterministicEnvironment: true));
        using var baseline = JsonDocument.Parse(EpubCorpusBaselineJsonSerializer.Serialize(result.ObservedBaseline));
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", CurrentFormat);
            writer.WriteBoolean("deterministicAcrossRepeatedRuns", result.IsDeterministic);
            WriteBaselineComparison(writer, result.AcceptedBaselineComparison);
            writer.WriteStartArray("publications");
            foreach (var publication in result.Publications)
            {
                WritePublication(writer, result, publication);
            }

            writer.WriteEndArray();
            writer.WritePropertyName("observedBaseline");
            baseline.RootElement.WriteTo(writer);
            writer.WritePropertyName("execution");
            execution.RootElement.WriteTo(writer);
            writer.WriteEndObject();
            writer.Flush();
        }

        return Normalize(buffer.WrittenSpan);
    }

    public static async Task WriteAtomicallyAsync(
        EpubCorpusQualificationResult result,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The qualification report path must have a parent directory.", nameof(outputPath));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, Serialize(result), cancellationToken).ConfigureAwait(false);
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

    private static void WriteBaselineComparison(
        Utf8JsonWriter writer,
        EpubCorpusBaselineComparison? comparison)
    {
        writer.WritePropertyName("acceptedBaseline");
        writer.WriteStartObject();
        writer.WriteString("status", comparison is null ? "not-provided" : comparison.IsMatch ? "matched" : "mismatched");
        writer.WriteStartArray("differences");
        foreach (var difference in comparison?.Differences ?? [])
        {
            writer.WriteStartObject();
            writer.WriteString("id", difference.Id.Value);
            writer.WriteString("field", difference.Field);
            writer.WriteString("expected", difference.Expected);
            writer.WriteString("actual", difference.Actual);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static void WritePublication(
        Utf8JsonWriter writer,
        EpubCorpusQualificationResult result,
        EpubCorpusQualificationEntry publication)
    {
        var execution = result.Report.Publications.Single(item => item.Id == publication.Id);
        var chapters = execution.Evidence.Semantic?.OrderedChapterIds ?? [];
        writer.WriteStartObject();
        writer.WriteString("id", publication.Id.Value);
        writer.WriteString("flowStatus", Token(publication.FlowStatus));
        writer.WriteBoolean("stableAcrossRepeatedRuns", result.IsDeterministic);
        writer.WriteNumber("fidelityLostUnitCount", publication.FidelityLostUnitCount);
        writer.WriteString("epubCheckStatus", Token(publication.EpubCheckStatus));
        writer.WriteString("evidenceRelationship", Token(publication.EvidenceRelationship));
        writer.WriteNumber("expectationFailureCount", publication.ExpectationFailureCount);
        writer.WritePropertyName("readingOrderSample");
        writer.WriteStartObject();
        writer.WriteNumber("chapterCount", chapters.Length);
        WriteOptionalString(writer, "firstChapterId", chapters.FirstOrDefault());
        WriteOptionalString(writer, "middleChapterId", chapters.IsEmpty ? null : chapters[chapters.Length / 2]);
        WriteOptionalString(writer, "lastChapterId", chapters.LastOrDefault());
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

    private static string Token<T>(T value)
        where T : struct, Enum => string.Concat(value.ToString().Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? $"-{char.ToLowerInvariant(character)}"
                : char.ToLowerInvariant(character).ToString()));

    private static byte[] Normalize(ReadOnlySpan<byte> bytes)
    {
        var result = new ArrayBufferWriter<byte>(bytes.Length + 1);
        foreach (var value in bytes)
        {
            if (value != (byte)'\r')
            {
                result.GetSpan(1)[0] = value;
                result.Advance(1);
            }
        }

        result.GetSpan(1)[0] = (byte)'\n';
        result.Advance(1);
        return result.WrittenSpan.ToArray();
    }
}
