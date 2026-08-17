using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Flow.Epub;

internal static class EpubArchiveUtilities
{
    internal static async Task<MemoryStream> CopyWithLimitAsync(
        Stream source,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var result = new MemoryStream();
        var buffer = new byte[81_920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maximumBytes)
            {
                await result.DisposeAsync().ConfigureAwait(false);
                throw new EpubLimitExceededException($"Input exceeds the configured limit of {maximumBytes} bytes.");
            }

            await result.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        result.Position = 0;
        return result;
    }

    internal static Dictionary<string, ZipArchiveEntry> IndexArchive(
        ZipArchive archive,
        EpubImportLimits limits,
        List<EpubDiagnostic> diagnostics)
    {
        if (archive.Entries.Count > limits.MaximumEntries)
        {
            diagnostics.Add(Error(
                EpubDiagnosticCodes.ArchiveLimitExceeded,
                $"The archive contains {archive.Entries.Count} entries; the limit is {limits.MaximumEntries}."));
            return new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        }

        long totalLength = 0;
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryNormalizeArchivePath(null, entry.FullName, out var normalizedPath))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.UnsafePath,
                    $"Archive entry '{entry.FullName}' has an unsafe path.",
                    entry.FullName));
                continue;
            }

            if (entry.Length > limits.MaximumTotalUncompressedBytes
                || totalLength > limits.MaximumTotalUncompressedBytes - entry.Length)
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.ArchiveLimitExceeded,
                    "The archive exceeds the configured total uncompressed size limit.",
                    normalizedPath));
                continue;
            }

            totalLength += entry.Length;
            var excessiveRatio = entry.Length > 1_024 * 1_024
                && entry.CompressedLength == 0
                || entry.CompressedLength > 0
                && entry.Length / entry.CompressedLength > limits.MaximumCompressionRatio;
            if (entry.Length > limits.MaximumEntryBytes || excessiveRatio)
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.ArchiveLimitExceeded,
                    $"Archive entry '{normalizedPath}' exceeds the configured size or compression limits.",
                    normalizedPath));
                continue;
            }

            if (!entries.TryAdd(normalizedPath, entry))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.InvalidArchive,
                    $"Archive path '{normalizedPath}' occurs more than once.",
                    normalizedPath));
            }
        }

        return entries;
    }

    internal static async Task<XDocument?> LoadXmlAsync(
        ZipArchiveEntry entry,
        string resource,
        EpubImportLimits limits,
        List<EpubDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var source = entry.Open();
            await using var buffer = await CopyWithLimitAsync(
                source,
                limits.MaximumEntryBytes,
                cancellationToken).ConfigureAwait(false);
            var settings = new XmlReaderSettings
            {
                Async = false,
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = limits.MaximumXmlCharacters,
                MaxCharactersFromEntities = 0,
                IgnoreComments = true,
                CloseInput = false,
            };
            using var reader = XmlReader.Create(buffer, settings);
            return XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (EpubLimitExceededException exception)
        {
            diagnostics.Add(Error(EpubDiagnosticCodes.ArchiveLimitExceeded, exception.Message, resource));
            return null;
        }
        catch (Exception exception) when (exception is XmlException or InvalidDataException or IOException)
        {
            diagnostics.Add(Error(
                EpubDiagnosticCodes.InvalidXml,
                $"XML resource '{resource}' is invalid or unsafe: {exception.Message}",
                resource));
            return null;
        }
    }

    internal static bool TryNormalizeArchivePath(string? baseDirectory, string value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith("/", StringComparison.Ordinal)
            || value.Contains('\\')
            || value.Contains('\0')
            || Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return false;
        }

        var pathOnly = value.Split(['#', '?'], 2)[0];
        try
        {
            pathOnly = Uri.UnescapeDataString(pathOnly);
        }
        catch (UriFormatException)
        {
            return false;
        }

        var segments = new List<string>();
        if (!string.IsNullOrEmpty(baseDirectory))
        {
            segments.AddRange(baseDirectory.Split('/', StringSplitOptions.RemoveEmptyEntries));
        }

        foreach (var segment in pathOnly.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    return false;
                }

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            if (segment.Contains(':', StringComparison.Ordinal) || segment.Any(char.IsControl))
            {
                return false;
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }

    internal static string GetDirectory(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? string.Empty : path[..separator];
    }

    private static EpubDiagnostic Error(string code, string message, string? resource = null) =>
        new(code, EpubDiagnosticSeverity.Error, message, resource);
}

internal sealed class EpubLimitExceededException : Exception
{
    internal EpubLimitExceededException(string message)
        : base(message)
    {
    }
}
