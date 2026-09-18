using System.Buffers;
using System.Text.Json;

namespace Flow.Epub.Corpus;

/// <summary>Writes deterministic private difference evidence without editorial identity or paths.</summary>
public static class EpubPrivateDifferenceMatrixJsonSerializer
{
    public static byte[] Serialize(EpubPrivateDifferenceMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", EpubPrivateDifferenceMatrix.CurrentFormat);
            writer.WriteString("qualificationReportSha256", matrix.QualificationReportSha256.Value);
            WriteSummary(writer, matrix.Summary);
            writer.WriteStartArray("candidates");
            foreach (var candidate in matrix.Candidates)
            {
                writer.WriteStartObject();
                writer.WriteString("id", candidate.Id.Value);
                if (candidate.SourceSha256 is { } sourceHash)
                {
                    writer.WriteString("sourceSha256", sourceHash.Value);
                }
                else
                {
                    writer.WriteNull("sourceSha256");
                }

                writer.WriteString("qualificationStatus", Token(candidate.QualificationStatus));
                writer.WriteString("overallCategory", Token(candidate.OverallCategory));
                writer.WriteString("humanReview", candidate.HumanReviewPending ? "pending" : "completed");
                writer.WriteStartArray("differences");
                foreach (var difference in candidate.Differences)
                {
                    writer.WriteStartObject();
                    writer.WriteString("code", difference.Code);
                    writer.WriteString("category", Token(difference.Category));
                    writer.WriteString("cause", Token(difference.Cause));
                    writer.WriteString("severity", Token(difference.Severity));
                    writer.WriteNumber("count", difference.Count);
                    writer.WriteString("metric", Token(difference.Metric));
                    if (difference.Phase is { } phase)
                    {
                        writer.WriteString("phase", Token(phase));
                    }
                    else
                    {
                        writer.WriteString("phase", "inventory");
                    }

                    if (difference.NeutralLocation is null)
                    {
                        writer.WriteNull("location");
                    }
                    else
                    {
                        writer.WriteString("location", difference.NeutralLocation);
                    }

                    writer.WriteEndObject();
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
        EpubPrivateDifferenceMatrix matrix,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)
                        ?? throw new ArgumentException("The matrix path must have a parent directory.", nameof(outputPath));
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, Serialize(matrix), cancellationToken).ConfigureAwait(false);
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

    private static void WriteSummary(Utf8JsonWriter writer, EpubPrivateDifferenceMatrixSummary summary)
    {
        writer.WriteStartObject("summary");
        writer.WriteNumber("totalCandidates", summary.TotalCandidates);
        writer.WriteNumber("approved", summary.Approved);
        writer.WriteNumber("approvedWithApproximations", summary.ApprovedWithApproximations);
        writer.WriteNumber("unsupportedContent", summary.UnsupportedContent);
        writer.WriteNumber("contentLoss", summary.ContentLoss);
        writer.WriteNumber("brokenSourceReference", summary.BrokenSourceReference);
        writer.WriteNumber("flowError", summary.FlowError);
        writer.WriteNumber("humanReviewRequired", summary.HumanReviewRequired);
        writer.WriteNumber("totalDifferences", summary.TotalDifferences);
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
