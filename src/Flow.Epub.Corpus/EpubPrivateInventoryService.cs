using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Flow.Epub;

namespace Flow.Epub.Corpus;

/// <summary>Performs bounded, read-only discovery and structural inspection of a private EPUB directory.</summary>
public sealed class EpubPrivateInventoryService : IEpubPrivateInventoryService
{
    private const string EncryptionPath = "META-INF/encryption.xml";
    private const string RightsPath = "META-INF/rights.xml";
    private const string IdpfFontObfuscation = "http://www.idpf.org/2008/embedding";
    private const string AdobeFontObfuscation = "http://ns.adobe.com/pdf/enc#RC";
    private const string XhtmlMediaType = "application/xhtml+xml";

    private readonly IEpubPublicationInspector inspector;
    private readonly EpubImportLimits limits;

    public EpubPrivateInventoryService(
        IEpubPublicationInspector? inspector = null,
        EpubImportLimits? limits = null)
    {
        this.limits = limits ?? new EpubImportLimits();
        this.inspector = inspector ?? new EpubPublicationInspector(this.limits);
    }

    public async Task<EpubPrivateInventoryReport> InventoryAsync(
        string sourceDirectory,
        EpubPrivateInventoryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        options ??= new EpubPrivateInventoryOptions();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory));
        var rootInfo = new DirectoryInfo(root);
        if (!rootInfo.Exists)
        {
            throw new DirectoryNotFoundException("The EPUB inventory source directory does not exist.");
        }

        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The EPUB inventory source directory cannot be a symbolic link or reparse point.");
        }

        var reportDiagnostics = new List<EpubPrivateInventoryDiagnostic>();
        var paths = Discover(root, options, reportDiagnostics, cancellationToken);
        var hashed = new List<HashedCandidate>(paths.Count);
        var unreadable = 0;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var file = new FileInfo(path);
                await using var source = OpenRead(path);
                var hash = new EpubCorpusSha256(Convert.ToHexString(
                    await SHA256.HashDataAsync(source, cancellationToken).ConfigureAwait(false)));
                hashed.Add(new HashedCandidate(path, file.Length, hash));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                unreadable++;
            }
        }

        var analyzed = new List<AnalyzedCandidate>();
        foreach (var group in hashed
                     .GroupBy(static item => item.Hash)
                     .OrderBy(static group => group.Key.Value, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = group.ToArray();
            analyzed.Add(await AnalyzeAsync(candidates[0], candidates.Length, cancellationToken).ConfigureAwait(false));
        }

        if (unreadable > 0)
        {
            analyzed.Add(AnalyzedCandidate.Unreadable(unreadable));
        }

        var publications = analyzed
            .OrderBy(static item => item.Sha256?.Value ?? new string('Z', 64), StringComparer.Ordinal)
            .ThenBy(static item => item.Status)
            .ThenBy(static item => item.FileBytes)
            .Select((item, index) => item.ToPublic(new EpubCorpusPublicationId($"candidate-{index + 1:0000}")))
            .ToArray();
        return new EpubPrivateInventoryReport(paths.Count, publications, reportDiagnostics);
    }

    private static List<string> Discover(
        string root,
        EpubPrivateInventoryOptions options,
        List<EpubPrivateInventoryDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var paths = new List<string>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(current.Path, "*", SearchOption.TopDirectoryOnly)
                    .Order(StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Diagnostic(
                    EpubPrivateInventoryDiagnosticCodes.CandidateUnreadable,
                    EpubPrivateInventoryDiagnosticSeverity.Warning));
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    diagnostics.Add(Diagnostic(
                        EpubPrivateInventoryDiagnosticCodes.CandidateUnreadable,
                        EpubPrivateInventoryDiagnosticSeverity.Warning));
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    diagnostics.Add(Diagnostic(
                        EpubPrivateInventoryDiagnosticCodes.UnsafeFileSystemEntry,
                        EpubPrivateInventoryDiagnosticSeverity.Warning));
                    continue;
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (current.Depth < options.MaximumRecursionDepth)
                    {
                        pending.Push((entry, current.Depth + 1));
                    }

                    continue;
                }

                if (!string.Equals(Path.GetExtension(entry), ".epub", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (paths.Count == options.MaximumCandidateFiles)
                {
                    diagnostics.Add(Diagnostic(
                        EpubPrivateInventoryDiagnosticCodes.SearchLimitExceeded,
                        EpubPrivateInventoryDiagnosticSeverity.Error));
                    return paths;
                }

                paths.Add(entry);
            }
        }

        return paths;
    }

    private async Task<AnalyzedCandidate> AnalyzeAsync(
        HashedCandidate candidate,
        int copyCount,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<EpubPrivateInventoryDiagnostic>();
        if (copyCount > 1)
        {
            diagnostics.Add(Diagnostic(
                EpubPrivateInventoryDiagnosticCodes.DuplicateContent,
                EpubPrivateInventoryDiagnosticSeverity.Information));
        }

        if (candidate.FileBytes > limits.MaximumArchiveBytes)
        {
            diagnostics.Add(Diagnostic(
                EpubPrivateInventoryDiagnosticCodes.ArchiveLimitExceeded,
                EpubPrivateInventoryDiagnosticSeverity.Error));
            return AnalyzedCandidate.Empty(
                candidate.Hash,
                EpubPrivateInventoryStatus.Unsuitable,
                copyCount,
                candidate.FileBytes,
                diagnostics);
        }

        try
        {
            EpubPublicationInspection inspection;
            await using (var source = OpenRead(candidate.Path))
            {
                inspection = await inspector.InspectAsync(source, cancellationToken).ConfigureAwait(false);
            }

            diagnostics.AddRange(inspection.Diagnostics.Select(static item => new EpubPrivateInventoryDiagnostic(
                item.Code,
                item.Severity switch
                {
                    EpubDiagnosticSeverity.Error => EpubPrivateInventoryDiagnosticSeverity.Error,
                    EpubDiagnosticSeverity.Warning => EpubPrivateInventoryDiagnosticSeverity.Warning,
                    _ => EpubPrivateInventoryDiagnosticSeverity.Information,
                })));
            var protection = await InspectProtectionAsync(candidate.Path, inspection.Manifest, cancellationToken)
                .ConfigureAwait(false);
            diagnostics.AddRange(protection.Diagnostics);
            if (!inspection.IsSuccess)
            {
                diagnostics.Add(Diagnostic(
                    EpubPrivateInventoryDiagnosticCodes.InspectionFailed,
                    EpubPrivateInventoryDiagnosticSeverity.Error));
            }

            var version = inspection.Package?.VersionFamily ?? EpubVersionFamily.Unknown;
            if (version == EpubVersionFamily.Unknown)
            {
                diagnostics.Add(Diagnostic(
                    EpubPrivateInventoryDiagnosticCodes.UnknownEpubVersion,
                    EpubPrivateInventoryDiagnosticSeverity.Warning));
            }

            var linearSpine = inspection.Spine.Count(static item => item.IsLinear);
            var supportedLinearXhtml = inspection.Spine.Count(static item => item is
            {
                IsLinear: true,
                IsSupported: true,
                ExistsInArchive: true,
                MediaType: XhtmlMediaType,
            });
            if (supportedLinearXhtml == 0)
            {
                diagnostics.Add(Diagnostic(
                    EpubPrivateInventoryDiagnosticCodes.MissingReadingOrder,
                    EpubPrivateInventoryDiagnosticSeverity.Warning));
            }

            if (inspection.Resources.UnsupportedManifestItemCount > 0)
            {
                diagnostics.Add(Diagnostic(
                    EpubPrivateInventoryDiagnosticCodes.UnsupportedResources,
                    EpubPrivateInventoryDiagnosticSeverity.Warning));
            }

            var status = protection.Status == EpubPrivateInventoryProtectionStatus.UnsupportedEncryption
                ? EpubPrivateInventoryStatus.Protected
                : !inspection.IsSuccess
                    ? EpubPrivateInventoryStatus.Corrupt
                    : version == EpubVersionFamily.Unknown || supportedLinearXhtml == 0
                        ? EpubPrivateInventoryStatus.Unsuitable
                        : protection.Status != EpubPrivateInventoryProtectionStatus.None
                          || diagnostics.Any(static item => item.Severity == EpubPrivateInventoryDiagnosticSeverity.Warning)
                            ? EpubPrivateInventoryStatus.ReviewRequired
                            : EpubPrivateInventoryStatus.Ready;
            return new AnalyzedCandidate(
                candidate.Hash,
                status,
                protection.Status,
                copyCount,
                candidate.FileBytes,
                version,
                inspection.Package?.Languages ?? [],
                inspection.Spine.Length,
                linearSpine,
                inspection.Spine.Length - linearSpine,
                CountResources(inspection),
                diagnostics);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or XmlException)
        {
            diagnostics.Add(Diagnostic(
                EpubPrivateInventoryDiagnosticCodes.CandidateUnreadable,
                EpubPrivateInventoryDiagnosticSeverity.Error));
            return AnalyzedCandidate.Empty(
                candidate.Hash,
                EpubPrivateInventoryStatus.Corrupt,
                copyCount,
                candidate.FileBytes,
                diagnostics);
        }
    }

    private async Task<ProtectionInspection> InspectProtectionAsync(
        string path,
        IReadOnlyCollection<EpubManifestItemInfo> manifest,
        CancellationToken cancellationToken)
    {
        await using var source = OpenRead(path);
        using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: false);
        var encryptionEntries = archive.Entries
            .Where(static entry => string.Equals(entry.FullName, EncryptionPath, StringComparison.Ordinal))
            .ToArray();
        var rightsPresent = archive.Entries.Any(static entry =>
            string.Equals(entry.FullName, RightsPath, StringComparison.Ordinal));
        var diagnostics = new List<EpubPrivateInventoryDiagnostic>();
        if (encryptionEntries.Length > 1)
        {
            diagnostics.Add(Diagnostic(
                EpubPrivateInventoryDiagnosticCodes.InvalidProtectionMetadata,
                EpubPrivateInventoryDiagnosticSeverity.Error));
            return new ProtectionInspection(EpubPrivateInventoryProtectionStatus.UnsupportedEncryption, diagnostics);
        }

        var obfuscated = 0;
        var unsupported = 0;
        var fontPaths = manifest
            .Where(static item => EpubMediaTypeClassifier.IsEmbeddedFont(item.MediaType))
            .Select(static item => item.Path)
            .ToHashSet(StringComparer.Ordinal);
        if (encryptionEntries.Length == 1)
        {
            var entry = encryptionEntries[0];
            if (entry.Length > limits.MaximumEntryBytes
                || entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > limits.MaximumCompressionRatio)
            {
                diagnostics.Add(Diagnostic(
                    EpubPrivateInventoryDiagnosticCodes.InvalidProtectionMetadata,
                    EpubPrivateInventoryDiagnosticSeverity.Error));
                return new ProtectionInspection(EpubPrivateInventoryProtectionStatus.UnsupportedEncryption, diagnostics);
            }

            try
            {
                var settings = new XmlReaderSettings
                {
                    Async = true,
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = limits.MaximumXmlCharacters,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                };
                await using var entryStream = entry.Open();
                using var reader = XmlReader.Create(entryStream, settings);
                var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
                foreach (var encryptedData in document.Descendants().Where(static element =>
                             element.Name.LocalName == "EncryptedData"))
                {
                    var algorithm = encryptedData.Descendants().FirstOrDefault(static element =>
                            element.Name.LocalName == "EncryptionMethod")
                        ?.Attribute("Algorithm")?.Value;
                    var reference = encryptedData.Descendants().FirstOrDefault(static element =>
                            element.Name.LocalName == "CipherReference")
                        ?.Attribute("URI")?.Value;
                    if ((string.Equals(algorithm, IdpfFontObfuscation, StringComparison.Ordinal)
                            || string.Equals(algorithm, AdobeFontObfuscation, StringComparison.Ordinal))
                        && TryNormalizeRootPath(reference, out var referencedPath)
                        && fontPaths.Contains(referencedPath))
                    {
                        obfuscated++;
                    }
                    else
                    {
                        unsupported++;
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or IOException or XmlException)
            {
                diagnostics.Add(Diagnostic(
                    EpubPrivateInventoryDiagnosticCodes.InvalidProtectionMetadata,
                    EpubPrivateInventoryDiagnosticSeverity.Error));
                return new ProtectionInspection(EpubPrivateInventoryProtectionStatus.UnsupportedEncryption, diagnostics);
            }
        }

        if (unsupported > 0)
        {
            diagnostics.Add(Diagnostic(
                EpubPrivateInventoryDiagnosticCodes.UnsupportedEncryption,
                EpubPrivateInventoryDiagnosticSeverity.Error));
            return new ProtectionInspection(EpubPrivateInventoryProtectionStatus.UnsupportedEncryption, diagnostics);
        }

        if (rightsPresent)
        {
            diagnostics.Add(Diagnostic(
                EpubPrivateInventoryDiagnosticCodes.RightsMetadataPresent,
                EpubPrivateInventoryDiagnosticSeverity.Warning));
            if (obfuscated > 0)
            {
                diagnostics.Add(Diagnostic(
                    EpubPrivateInventoryDiagnosticCodes.FontObfuscationPresent,
                    EpubPrivateInventoryDiagnosticSeverity.Information));
            }

            return new ProtectionInspection(EpubPrivateInventoryProtectionStatus.RightsMetadataPresent, diagnostics);
        }

        if (obfuscated > 0)
        {
            diagnostics.Add(Diagnostic(
                EpubPrivateInventoryDiagnosticCodes.FontObfuscationPresent,
                EpubPrivateInventoryDiagnosticSeverity.Warning));
            return new ProtectionInspection(EpubPrivateInventoryProtectionStatus.FontObfuscationOnly, diagnostics);
        }

        return new ProtectionInspection(EpubPrivateInventoryProtectionStatus.None, diagnostics);
    }

    private static bool TryNormalizeRootPath(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains('\0', StringComparison.Ordinal)
            || Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(value);
        }
        catch (UriFormatException)
        {
            return false;
        }

        var segments = new List<string>();
        foreach (var segment in decoded.Split('/', StringSplitOptions.RemoveEmptyEntries))
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

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }

    private static EpubPrivateInventoryResourceCounts CountResources(EpubPublicationInspection inspection)
    {
        var xhtml = 0;
        var css = 0;
        var raster = 0;
        var svg = 0;
        var fonts = 0;
        var audio = 0;
        var other = 0;
        foreach (var item in inspection.Manifest)
        {
            if (EpubMediaTypeClassifier.IsEmbeddedFont(item.MediaType))
            {
                fonts++;
                continue;
            }

            switch (item.MediaType)
            {
                case XhtmlMediaType:
                    xhtml++;
                    break;
                case "text/css":
                    css++;
                    break;
                case "image/jpeg" or "image/png" or "image/gif" or "image/webp":
                    raster++;
                    break;
                case "image/svg+xml":
                    svg++;
                    break;
                default:
                    if (item.MediaType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
                    {
                        audio++;
                    }
                    else
                    {
                        other++;
                    }

                    break;
            }
        }

        return new EpubPrivateInventoryResourceCounts(
            inspection.Resources.ArchiveEntryCount,
            inspection.Resources.ManifestItemCount,
            xhtml,
            css,
            raster,
            svg,
            fonts,
            audio,
            other);
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        64 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static EpubPrivateInventoryDiagnostic Diagnostic(
        string code,
        EpubPrivateInventoryDiagnosticSeverity severity) => new(code, severity);

    private sealed record HashedCandidate(string Path, long FileBytes, EpubCorpusSha256 Hash);

    private sealed record ProtectionInspection(
        EpubPrivateInventoryProtectionStatus Status,
        IReadOnlyCollection<EpubPrivateInventoryDiagnostic> Diagnostics);

    private sealed record AnalyzedCandidate(
        EpubCorpusSha256? Sha256,
        EpubPrivateInventoryStatus Status,
        EpubPrivateInventoryProtectionStatus Protection,
        int CopyCount,
        long FileBytes,
        EpubVersionFamily EpubVersion,
        IReadOnlyCollection<string> Languages,
        int SpineItemCount,
        int LinearSpineItemCount,
        int NonLinearSpineItemCount,
        EpubPrivateInventoryResourceCounts Resources,
        IReadOnlyCollection<EpubPrivateInventoryDiagnostic> Diagnostics)
    {
        internal static AnalyzedCandidate Empty(
            EpubCorpusSha256? sha256,
            EpubPrivateInventoryStatus status,
            int copyCount,
            long fileBytes,
            IReadOnlyCollection<EpubPrivateInventoryDiagnostic> diagnostics) => new(
            sha256,
            status,
            EpubPrivateInventoryProtectionStatus.None,
            copyCount,
            fileBytes,
            EpubVersionFamily.Unknown,
            [],
            0,
            0,
            0,
            EpubPrivateInventoryResourceCounts.Empty,
            diagnostics);

        internal static AnalyzedCandidate Unreadable(int copyCount) => Empty(
            null,
            EpubPrivateInventoryStatus.Corrupt,
            copyCount,
            0,
            [Diagnostic(
                EpubPrivateInventoryDiagnosticCodes.CandidateUnreadable,
                EpubPrivateInventoryDiagnosticSeverity.Error)]);

        internal EpubPrivateInventoryItem ToPublic(EpubCorpusPublicationId id) => new(
            id,
            Sha256,
            Status,
            Protection,
            CopyCount,
            FileBytes,
            EpubVersion,
            Languages,
            SpineItemCount,
            LinearSpineItemCount,
            NonLinearSpineItemCount,
            Resources,
            Diagnostics);
    }
}
