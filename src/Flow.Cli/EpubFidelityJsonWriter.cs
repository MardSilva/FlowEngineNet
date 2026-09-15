using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Flow.Epub;

namespace Flow.Cli;

internal static class EpubFidelityJsonWriter
{
    internal static async Task WriteAsync(
        EpubFidelityReport report,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(report);
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
            writer.WriteString("format", EpubFidelityReport.Format);
            writer.WriteBoolean("importSucceeded", report.ImportSucceeded);
            writer.WriteString(
                "scope",
                "Informative EPUB-to-Flow measurement; this is not a claim of EPUB conformance or visual equivalence.");
            WriteSummary(writer, report.Summary);
            WriteMeasurements(writer, report.Measurements);
            WriteFindings(writer, report.Findings);
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        var source = buffer.GetBuffer().AsMemory(0, checked((int)buffer.Length));
        if (source.Span.IndexOf((byte)'\r') < 0)
        {
            await destination.WriteAsync(source, cancellationToken).ConfigureAwait(false);
            return;
        }

        var normalized = new byte[source.Length];
        var written = 0;
        var bytes = source.Span;
        for (var index = 0; index < bytes.Length; index++)
        {
            if (bytes[index] == '\r' && index + 1 < bytes.Length && bytes[index + 1] == '\n')
            {
                normalized[written++] = (byte)'\n';
                index++;
            }
            else
            {
                normalized[written++] = bytes[index];
            }
        }

        await destination.WriteAsync(normalized.AsMemory(0, written), cancellationToken).ConfigureAwait(false);
    }

    private static void WriteSummary(Utf8JsonWriter writer, EpubFidelitySummary summary)
    {
        writer.WritePropertyName("summary");
        writer.WriteStartObject();
        writer.WriteNumber("sourceUnitCount", summary.SourceUnitCount);
        writer.WriteNumber("preserved", summary.PreservedCount);
        writer.WriteNumber("transformed", summary.TransformedCount);
        writer.WriteNumber("approximated", summary.ApproximatedCount);
        writer.WriteNumber("unsupported", summary.UnsupportedCount);
        writer.WriteNumber("lost", summary.LostCount);
        WriteNullableDecimal(writer, "preservationPercentage", summary.PreservationPercentage);
        writer.WriteBoolean("partial", summary.IsPartial);
        writer.WriteEndObject();
    }

    private static void WriteMeasurements(Utf8JsonWriter writer, IEnumerable<EpubFidelityMeasurement> measurements)
    {
        writer.WritePropertyName("measurements");
        writer.WriteStartArray();
        foreach (var measurement in measurements)
        {
            writer.WriteStartObject();
            writer.WriteString("metric", CamelCase(measurement.Metric.ToString()));
            writer.WriteNumber("source", measurement.SourceCount);
            writer.WriteNumber("destination", measurement.DestinationCount);
            writer.WriteNumber("preserved", measurement.PreservedCount);
            writer.WriteNumber("transformed", measurement.TransformedCount);
            writer.WriteNumber("approximated", measurement.ApproximatedCount);
            writer.WriteNumber("unsupported", measurement.UnsupportedCount);
            writer.WriteNumber("lost", measurement.LostCount);
            WriteNullableDecimal(writer, "preservationPercentage", measurement.PreservationPercentage);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteFindings(Utf8JsonWriter writer, IEnumerable<EpubFidelityFinding> findings)
    {
        writer.WritePropertyName("findings");
        writer.WriteStartArray();
        foreach (var finding in findings)
        {
            writer.WriteStartObject();
            writer.WriteString("code", finding.Code);
            writer.WriteString("status", CamelCase(finding.Status.ToString()));
            writer.WriteString("impact", CamelCase(finding.Impact.ToString()));
            writer.WriteNumber("count", finding.Count);
            WriteNullableString(writer, "resourcePath", finding.ResourcePath);
            WriteNullableString(writer, "fragment", finding.Fragment);
            WriteNullableString(writer, "nodeId", finding.NodeId?.Value);
            WriteNullableString(writer, "assetId", finding.AssetId?.Value);
            WriteNullableString(writer, "relatedDiagnosticCode", finding.RelatedDiagnosticCode);
            writer.WriteString("explanation", finding.Explanation);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteNullableDecimal(Utf8JsonWriter writer, string name, decimal? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteNumber(name, value.Value);
        }
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value)
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

    private static string CamelCase(string value) => char.ToLowerInvariant(value[0]) + value[1..];
}
