using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Flow.Epub;

/// <summary>Writes deterministic, path-free EPUB corpus discovery reports.</summary>
public sealed class EpubCorpusDiscoveryReportJsonSerializer
{
    public void Write(EpubCorpusDiscoveryReport report, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The discovery report stream must be writable.", nameof(destination));
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            Indented = true,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", EpubCorpusDiscoveryReport.CurrentFormat);
            writer.WriteStartArray("publications");
            foreach (var item in report.Items.OrderBy(static item => item.Id.Value, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("id", item.Id.Value);
                writer.WriteString("status", WriteStatus(item.Status));
                if (item.Source is { } source)
                {
                    writer.WriteString("source", WriteSource(source));
                }

                if (item.MatchKind is { } matchKind)
                {
                    writer.WriteString("match", WriteMatchKind(matchKind));
                }

                writer.WriteStartArray("diagnostics");
                foreach (var diagnostic in item.Diagnostics)
                {
                    writer.WriteStartObject();
                    writer.WriteString("code", diagnostic.Code);
                    writer.WriteString("severity", diagnostic.Severity == EpubCorpusDiagnosticSeverity.Error ? "error" : "warning");
                    writer.WriteString("message", diagnostic.Message);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        foreach (var value in buffer.WrittenSpan)
        {
            if (value != (byte)'\r')
            {
                destination.WriteByte(value);
            }
        }

        destination.WriteByte((byte)'\n');
    }

    private static string WriteStatus(EpubCorpusDiscoveryStatus status) => status switch
    {
        EpubCorpusDiscoveryStatus.Available => "available",
        EpubCorpusDiscoveryStatus.Missing => "missing",
        EpubCorpusDiscoveryStatus.HashMismatch => "hashMismatch",
        EpubCorpusDiscoveryStatus.LicenseRejected => "licenseRejected",
        EpubCorpusDiscoveryStatus.UnsafePath => "unsafePath",
        EpubCorpusDiscoveryStatus.SearchLimitExceeded => "searchLimitExceeded",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown discovery status."),
    };

    private static string WriteSource(EpubCorpusDiscoverySource source) => source switch
    {
        EpubCorpusDiscoverySource.RepositoryFixture => "repositoryFixture",
        EpubCorpusDiscoverySource.ExternalCorpus => "externalCorpus",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown discovery source."),
    };

    private static string WriteMatchKind(EpubCorpusMatchKind matchKind) => matchKind switch
    {
        EpubCorpusMatchKind.RelativePath => "relativePath",
        EpubCorpusMatchKind.PublicationId => "publicationId",
        EpubCorpusMatchKind.Sha256 => "sha256",
        _ => throw new ArgumentOutOfRangeException(nameof(matchKind), matchKind, "Unknown match kind."),
    };
}
