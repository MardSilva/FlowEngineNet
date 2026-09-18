using System.Buffers;
using System.Text.Json;

namespace Flow.Epub.Corpus;

/// <summary>Writes deterministic, path-free private visual-review evidence.</summary>
public static class EpubPrivateVisualReviewReportJsonSerializer
{
    public static byte[] Serialize(EpubPrivateVisualReviewReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", EpubPrivateVisualReviewReport.CurrentFormat);
            writer.WriteString("qualificationReportSha256", report.QualificationReportSha256.Value);
            writer.WriteStartObject("summary");
            writer.WriteNumber("total", report.Summary.Total);
            writer.WriteNumber("generated", report.Summary.Generated);
            writer.WriteNumber("missing", report.Summary.Missing);
            writer.WriteNumber("skipped", report.Summary.Skipped);
            writer.WriteNumber("failed", report.Summary.Failed);
            writer.WriteEndObject();
            writer.WriteStartArray("candidates");
            foreach (var candidate in report.Candidates)
            {
                writer.WriteStartObject();
                writer.WriteString("id", candidate.Id.Value);
                writer.WriteString("status", Token(candidate.Status));
                WriteOptionalHash(writer, "sourceSha256", candidate.SourceSha256);
                WriteOptionalHash(writer, "canonicalDocumentSha256", candidate.CanonicalDocumentSha256);
                WriteOptionalString(writer, "relativeDirectory", candidate.RelativeDirectory);
                WriteStrings(writer, "samples", candidate.Samples);
                WriteStrings(writer, "targets", candidate.Targets);
                WriteOptionalString(writer, "failureCode", candidate.FailureCode);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return Normalize(buffer.WrittenSpan);
    }

    private static void WriteOptionalHash(Utf8JsonWriter writer, string name, EpubCorpusSha256? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value.Value.Value);
        }
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    private static string Token(EpubPrivateVisualReviewStatus value) => value switch
    {
        EpubPrivateVisualReviewStatus.Generated => "generated",
        EpubPrivateVisualReviewStatus.MissingSource => "missing-source",
        EpubPrivateVisualReviewStatus.SkippedQualification => "skipped-qualification",
        EpubPrivateVisualReviewStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static byte[] Normalize(ReadOnlySpan<byte> bytes)
    {
        var result = new ArrayBufferWriter<byte>(bytes.Length + 1);
        foreach (var value in bytes)
        {
            if (value == (byte)'\r')
            {
                continue;
            }

            result.GetSpan(1)[0] = value;
            result.Advance(1);
        }

        result.GetSpan(1)[0] = (byte)'\n';
        result.Advance(1);
        return result.WrittenSpan.ToArray();
    }
}
