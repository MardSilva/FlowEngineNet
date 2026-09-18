using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Flow.Epub;

internal static class EpubArchiveUtilities
{
    private static readonly Regex Html5Doctype = new(
        "^<!DOCTYPE\\s+html\\s*>$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex XhtmlPublicDoctype = new(
        "^<!DOCTYPE\\s+html\\s+PUBLIC\\s+\"(?<public>[^\"]+)\"\\s+\"(?<system>[^\"]+)\"\\s*>$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly Regex NcxPublicDoctype = new(
        "^<!DOCTYPE\\s+ncx\\s+PUBLIC\\s+\"-//NISO//DTD ncx 2005-1//EN\"\\s+\"http://www.daisy.org/z3986/2005/ncx-2005-1.dtd\"\\s*>$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    private static readonly IReadOnlyDictionary<string, string> KnownXhtmlPublicDoctypes =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["-//W3C//DTD XHTML 1.0 Strict//EN"] = "http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd",
            ["-//W3C//DTD XHTML 1.0 Transitional//EN"] = "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd",
            ["-//W3C//DTD XHTML 1.0 Frameset//EN"] = "http://www.w3.org/TR/xhtml1/DTD/xhtml1-frameset.dtd",
            ["-//W3C//DTD XHTML 1.1//EN"] = "http://www.w3.org/TR/xhtml11/DTD/xhtml11.dtd",
        };

    internal static async Task<MemoryStream> CopyWithLimitAsync(
        Stream source,
        long maximumBytes,
        CancellationToken cancellationToken,
        Action<long>? progress = null)
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
            progress?.Invoke(total);
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
        CancellationToken cancellationToken,
        EpubXmlDoctypeProfile doctypeProfile = EpubXmlDoctypeProfile.None)
    {
        try
        {
            await using var source = entry.Open();
            await using var buffer = await CopyWithLimitAsync(
                source,
                limits.MaximumEntryBytes,
                cancellationToken).ConfigureAwait(false);
            var doctypeDisposition = doctypeProfile != EpubXmlDoctypeProfile.None
                ? InspectDoctype(buffer, doctypeProfile)
                : DoctypeDisposition.None;
            if (doctypeDisposition == DoctypeDisposition.Unsupported)
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.InvalidXml,
                    $"XML resource '{resource}' declares an unsupported or unsafe DOCTYPE for its resource type. "
                    + "Only exact known public declarations without an internal subset are accepted; DTD content is never loaded.",
                    resource));
                return null;
            }

            buffer.Position = 0;
            var settings = new XmlReaderSettings
            {
                Async = false,
                DtdProcessing = doctypeDisposition == DoctypeDisposition.Known
                    ? DtdProcessing.Ignore
                    : DtdProcessing.Prohibit,
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

    private static DoctypeDisposition InspectDoctype(
        MemoryStream buffer,
        EpubXmlDoctypeProfile profile)
    {
        var source = DecodeForDoctypeInspection(buffer);
        var start = source.IndexOf("<!DOCTYPE", StringComparison.Ordinal);
        if (start < 0)
        {
            return DoctypeDisposition.None;
        }

        var end = FindDoctypeEnd(source, start + "<!DOCTYPE".Length);
        if (end < 0)
        {
            return DoctypeDisposition.Unsupported;
        }

        var declaration = source[start..(end + 1)];
        if (declaration.Contains('[', StringComparison.Ordinal)
            || source.IndexOf("<!DOCTYPE", end + 1, StringComparison.Ordinal) >= 0)
        {
            return DoctypeDisposition.Unsupported;
        }

        if (profile == EpubXmlDoctypeProfile.Xhtml && Html5Doctype.IsMatch(declaration))
        {
            return DoctypeDisposition.Known;
        }

        var match = XhtmlPublicDoctype.Match(declaration);
        var knownXhtml = profile == EpubXmlDoctypeProfile.Xhtml
            && match.Success
            && KnownXhtmlPublicDoctypes.TryGetValue(match.Groups["public"].Value, out var expectedSystemId)
            && string.Equals(match.Groups["system"].Value, expectedSystemId, StringComparison.Ordinal);
        var knownNcx = profile == EpubXmlDoctypeProfile.Ncx
            && NcxPublicDoctype.IsMatch(declaration);
        return knownXhtml || knownNcx
                ? DoctypeDisposition.Known
                : DoctypeDisposition.Unsupported;
    }

    private static string DecodeForDoctypeInspection(MemoryStream buffer)
    {
        var bytes = buffer.TryGetBuffer(out var segment)
            ? segment.AsSpan(0, checked((int)buffer.Length))
            : buffer.ToArray().AsSpan();
        if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            return Encoding.UTF8.GetString(bytes[Encoding.UTF8.Preamble.Length..]);
        }

        if (bytes.StartsWith(Encoding.UTF32.Preamble))
        {
            return Encoding.UTF32.GetString(bytes[Encoding.UTF32.Preamble.Length..]);
        }

        if (bytes.StartsWith(Encoding.BigEndianUnicode.Preamble))
        {
            return Encoding.BigEndianUnicode.GetString(bytes[Encoding.BigEndianUnicode.Preamble.Length..]);
        }

        if (bytes.StartsWith(Encoding.Unicode.Preamble))
        {
            return Encoding.Unicode.GetString(bytes[Encoding.Unicode.Preamble.Length..]);
        }

        // XML markup is ASCII-compatible for the encodings commonly used by EPUB.
        // Latin-1 preserves byte positions while inspecting only the declaration syntax.
        return Encoding.Latin1.GetString(bytes);
    }

    private static int FindDoctypeEnd(string source, int offset)
    {
        char quote = '\0';
        var subsetDepth = 0;
        for (var index = offset; index < source.Length; index++)
        {
            var character = source[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
            }
            else if (character == '[')
            {
                subsetDepth++;
            }
            else if (character == ']')
            {
                subsetDepth--;
            }
            else if (character == '>' && subsetDepth == 0)
            {
                return index;
            }
        }

        return -1;
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

    private enum DoctypeDisposition
    {
        None,
        Known,
        Unsupported,
    }
}

internal enum EpubXmlDoctypeProfile
{
    None,
    Xhtml,
    Ncx,
}

internal sealed class EpubLimitExceededException : Exception
{
    internal EpubLimitExceededException(string message)
        : base(message)
    {
    }
}
