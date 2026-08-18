using System.Collections.Immutable;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Flow.Epub;

/// <summary>Inspects EPUB ZIP, container, and OPF structure without converting publication content.</summary>
public sealed class EpubPublicationInspector : IEpubPublicationInspector
{
    private const string ContainerPath = "META-INF/container.xml";
    private const string NcxMediaType = "application/x-dtbncx+xml";
    private static readonly XNamespace ContainerNamespace = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace OpfNamespace = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace DcNamespace = "http://purl.org/dc/elements/1.1/";

    private readonly EpubImportLimits limits;

    /// <summary>Creates an inspector with the same default resource limits as the EPUB importer.</summary>
    public EpubPublicationInspector(EpubImportLimits? limits = null)
    {
        this.limits = limits ?? new EpubImportLimits();
    }

    /// <inheritdoc />
    public Task<EpubPublicationInspection> InspectAsync(
        Stream source,
        CancellationToken cancellationToken = default) => InspectAsync(source, null, cancellationToken);

    /// <inheritdoc />
    public async Task<EpubPublicationInspection> InspectAsync(
        Stream source,
        IProgress<EpubImportProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The EPUB source stream must be readable.", nameof(source));
        }

        var diagnostics = new List<EpubDiagnostic>();
        var telemetry = new EpubImportTelemetryCollector(progress);
        EpubPublicationInspection Finish(EpubPublicationInspection inspection)
        {
            telemetry.Start(EpubImportPhase.Completed, 1);
            telemetry.Advance(1, 1);
            _ = telemetry.Finish(null);
            return inspection;
        }

        try
        {
            long? archiveTotal = null;
            if (source.CanSeek)
            {
                try
                {
                    archiveTotal = Math.Max(0, source.Length - source.Position);
                }
                catch (Exception exception) when (exception is IOException or NotSupportedException)
                {
                    archiveTotal = null;
                }
            }

            telemetry.Start(EpubImportPhase.CopyingArchive, archiveTotal);
            await using var archiveBuffer = await EpubArchiveUtilities.CopyWithLimitAsync(
                source,
                limits.MaximumArchiveBytes,
                cancellationToken,
                copied => telemetry.Advance(copied, archiveTotal)).ConfigureAwait(false);
            using var archive = new ZipArchive(archiveBuffer, ZipArchiveMode.Read, leaveOpen: false);
            var archiveEntries = archive.Entries
                .Where(static entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
                .ToArray();
            var compressedBytes = SaturatingSum(archiveEntries.Select(static entry => entry.CompressedLength));
            var uncompressedBytes = SaturatingSum(archiveEntries.Select(static entry => entry.Length));
            telemetry.Start(EpubImportPhase.IndexingArchive, archive.Entries.Count);
            var entries = EpubArchiveUtilities.IndexArchive(archive, limits, diagnostics);
            telemetry.Advance(archive.Entries.Count, archive.Entries.Count);
            var archiveOnlySummary = CreateSummary(
                archiveEntries.Length,
                compressedBytes,
                uncompressedBytes,
                []);

            if (HasErrors(diagnostics))
            {
                return Finish(EmptyInspection(archiveOnlySummary, diagnostics));
            }

            telemetry.Start(EpubImportPhase.ReadingContainer, 1, ContainerPath);
            if (!entries.TryGetValue(ContainerPath, out var containerEntry))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.MissingContainer,
                    $"Required resource '{ContainerPath}' was not found."));
                return Finish(EmptyInspection(archiveOnlySummary, diagnostics));
            }

            var container = await EpubArchiveUtilities.LoadXmlAsync(
                containerEntry,
                ContainerPath,
                limits,
                diagnostics,
                cancellationToken).ConfigureAwait(false);
            telemetry.Advance(1, 1, ContainerPath);
            var packagePath = ReadPackagePath(container, diagnostics);
            if (packagePath is null || !entries.TryGetValue(packagePath, out var packageEntry))
            {
                if (packagePath is not null)
                {
                    diagnostics.Add(Error(
                        EpubDiagnosticCodes.MissingPackage,
                        $"The package document '{packagePath}' declared by container.xml was not found.",
                        packagePath));
                }

                return Finish(EmptyInspection(archiveOnlySummary, diagnostics));
            }

            telemetry.Start(EpubImportPhase.ReadingPackage, 1, packagePath);
            var packageDocument = await EpubArchiveUtilities.LoadXmlAsync(
                packageEntry,
                packagePath,
                limits,
                diagnostics,
                cancellationToken).ConfigureAwait(false);
            telemetry.Advance(1, 1, packagePath);
            if (packageDocument is null)
            {
                return Finish(EmptyInspection(archiveOnlySummary, diagnostics));
            }

            telemetry.Start(EpubImportPhase.ProcessingManifest, 1, packagePath);
            var parsed = ReadPackage(packageDocument, packagePath, entries, diagnostics);
            telemetry.Advance(1, 1, packagePath);
            if (parsed is null)
            {
                return Finish(EmptyInspection(archiveOnlySummary, diagnostics));
            }

            var summary = CreateSummary(
                archiveEntries.Length,
                compressedBytes,
                uncompressedBytes,
                parsed.Manifest);
            return Finish(new EpubPublicationInspection(
                ContainerPath,
                parsed.Package,
                parsed.Manifest,
                parsed.Spine,
                parsed.NavigationDocumentPaths,
                summary,
                diagnostics));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EpubLimitExceededException exception)
        {
            diagnostics.Add(Error(EpubDiagnosticCodes.ArchiveLimitExceeded, exception.Message));
            return Finish(EmptyInspection(EpubResourceSummary.Empty, diagnostics));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or XmlException)
        {
            diagnostics.Add(Error(
                EpubDiagnosticCodes.InvalidArchive,
                $"The EPUB could not be inspected safely: {exception.Message}"));
            return Finish(EmptyInspection(EpubResourceSummary.Empty, diagnostics));
        }
    }

    private static ParsedPackage? ReadPackage(
        XDocument document,
        string packagePath,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        List<EpubDiagnostic> diagnostics)
    {
        var root = document.Root;
        if (root?.Name != OpfNamespace + "package")
        {
            diagnostics.Add(Error(
                EpubDiagnosticCodes.InvalidPackage,
                "The package document does not contain an OPF package root.",
                packagePath));
            return null;
        }

        var declaredVersion = NormalizedOptional((string?)root.Attribute("version"));
        var versionFamily = IdentifyVersion(declaredVersion);
        if (versionFamily == EpubVersionFamily.Unknown)
        {
            diagnostics.Add(Warning(
                EpubDiagnosticCodes.UnsupportedVersion,
                $"Package version '{declaredVersion ?? "(missing)"}' is not recognized as EPUB 2 or EPUB 3.",
                packagePath));
        }

        var metadata = root.Element(OpfNamespace + "metadata");
        var uniqueIdentifierId = NormalizedOptional((string?)root.Attribute("unique-identifier"));
        var identifiers = metadata?.Elements(DcNamespace + "identifier").ToArray() ?? [];
        var identifier = identifiers
            .FirstOrDefault(element => string.Equals(
                (string?)element.Attribute("id"),
                uniqueIdentifierId,
                StringComparison.Ordinal));
        identifier ??= identifiers.FirstOrDefault();

        var title = FirstMetadataValue(metadata, DcNamespace + "title");
        if (title is null)
        {
            diagnostics.Add(Warning(
                EpubDiagnosticCodes.MetadataFallback,
                "The EPUB package has no dc:title.",
                packagePath));
        }

        var creators = metadata?.Elements(DcNamespace + "creator")
            .Select(static element => NormalizedText(element.Value))
            .Where(static value => value.Length > 0)
            .ToImmutableArray() ?? [];
        var modified = metadata?.Elements(OpfNamespace + "meta")
            .FirstOrDefault(static element => string.Equals(
                (string?)element.Attribute("property"),
                "dcterms:modified",
                StringComparison.Ordinal))
            ?.Value;

        var package = new EpubPackageInfo(
            packagePath,
            declaredVersion,
            versionFamily,
            uniqueIdentifierId,
            identifier is null ? null : NormalizedOptional(identifier.Value),
            title,
            FirstMetadataValue(metadata, DcNamespace + "language"),
            creators,
            FirstMetadataValue(metadata, DcNamespace + "publisher"),
            FirstMetadataValue(metadata, DcNamespace + "description"),
            NormalizedOptional(modified));

        var manifest = new List<EpubManifestItemInfo>();
        var manifestById = new Dictionary<string, EpubManifestItemInfo>(StringComparer.Ordinal);
        foreach (var element in root.Element(OpfNamespace + "manifest")?.Elements(OpfNamespace + "item") ?? [])
        {
            var id = NormalizedOptional((string?)element.Attribute("id"));
            var href = NormalizedOptional((string?)element.Attribute("href"));
            var mediaType = NormalizedOptional((string?)element.Attribute("media-type"));
            if (id is null || href is null || mediaType is null)
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.InvalidPackage,
                    "A manifest item is missing id, href, or media-type.",
                    packagePath));
                continue;
            }

            if (!EpubArchiveUtilities.TryNormalizeArchivePath(
                    EpubArchiveUtilities.GetDirectory(packagePath),
                    href,
                    out var resourcePath))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.UnsafePath,
                    $"Manifest item '{id}' has unsafe href '{href}'.",
                    packagePath));
                continue;
            }

            var properties = ((string?)element.Attribute("properties") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var isNavigation = properties.Contains("nav", StringComparer.Ordinal)
                || string.Equals(mediaType, NcxMediaType, StringComparison.OrdinalIgnoreCase);
            var exists = entries.ContainsKey(resourcePath);
            var supported = IsStructurallySupported(mediaType, isNavigation);
            var item = new EpubManifestItemInfo(
                id,
                href,
                resourcePath,
                mediaType,
                properties,
                exists,
                isNavigation,
                supported,
                NormalizedOptional((string?)element.Attribute("fallback")),
                NormalizedOptional((string?)element.Attribute("media-overlay")));
            if (!manifestById.TryAdd(id, item))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.InvalidPackage,
                    $"Manifest ID '{id}' occurs more than once.",
                    packagePath));
                continue;
            }

            manifest.Add(item);
            if (manifest.Take(manifest.Count - 1).Any(existing =>
                    string.Equals(existing.Path, resourcePath, StringComparison.Ordinal)))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.DuplicateManifestResource,
                    $"Manifest item '{id}' repeats resource path '{resourcePath}' under another ID.",
                    packagePath));
            }

            if (!exists)
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.MissingResource,
                    $"Manifest resource '{resourcePath}' is missing from the archive.",
                    resourcePath));
            }

            if (!supported)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.UnsupportedResource,
                    $"Manifest resource type '{mediaType}' is not structurally supported.",
                    resourcePath));
            }

            if (item.MediaOverlayId is not null)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.UnsupportedMediaOverlay,
                    $"Manifest item '{id}' declares media overlay '{item.MediaOverlayId}'; timing and audio are not inspected.",
                    resourcePath));
            }

            foreach (var property in item.Properties.Where(static property =>
                         property is not "nav" and not "cover-image"))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.UnsupportedResource,
                    $"Manifest property '{property}' on item '{id}' is retained but is not interpreted.",
                    resourcePath));
            }
        }

        foreach (var item in manifest)
        {
            if (item.FallbackId is not null && !manifestById.ContainsKey(item.FallbackId))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.BrokenFallback,
                    $"Manifest item '{item.Id}' references missing fallback item '{item.FallbackId}'.",
                    item.Path));
            }

            if (item.MediaOverlayId is not null && !manifestById.ContainsKey(item.MediaOverlayId))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.UnsupportedMediaOverlay,
                    $"Media overlay reference '{item.MediaOverlayId}' from item '{item.Id}' does not resolve in the manifest.",
                    item.Path));
            }
        }

        ReportCircularFallbackChains(manifest, manifestById, packagePath, diagnostics);

        var navigationPaths = manifest
            .Where(static item => item.IsNavigationDocument)
            .Select(static item => item.Path)
            .ToHashSet(StringComparer.Ordinal);
        var spine = new List<EpubSpineItemInfo>();
        var spineElement = root.Element(OpfNamespace + "spine");
        var ncxId = NormalizedOptional((string?)spineElement?.Attribute("toc"));
        if (ncxId is not null && manifestById.TryGetValue(ncxId, out var ncxItem))
        {
            navigationPaths.Add(ncxItem.Path);
        }

        var position = 0;
        var spineOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var reference in spineElement?.Elements(OpfNamespace + "itemref") ?? [])
        {
            var idref = NormalizedOptional((string?)reference.Attribute("idref"));
            var linearValue = NormalizedOptional((string?)reference.Attribute("linear"));
            var isLinear = !string.Equals(linearValue, "no", StringComparison.OrdinalIgnoreCase);
            if (linearValue is not null
                && !string.Equals(linearValue, "yes", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(linearValue, "no", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidSpineLinearity,
                    $"Spine reference '{idref ?? "(missing)"}' has invalid linear value '{linearValue}'; it is treated as linear='yes'.",
                    packagePath));
                isLinear = true;
            }

            var occurrence = idref is null
                ? 1
                : spineOccurrences.TryGetValue(idref, out var previous) ? previous + 1 : 1;
            if (idref is not null)
            {
                spineOccurrences[idref] = occurrence;
            }

            var repeated = occurrence > 1;
            if (repeated)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.RepeatedSpineItem,
                    $"Spine item '{idref}' is repeated at position {position}; the occurrence remains in declared order.",
                    packagePath));
            }

            if (idref is null || !manifestById.TryGetValue(idref, out var item))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.InvalidPackage,
                    $"Spine reference '{idref ?? "(missing)"}' does not resolve to a manifest item.",
                    packagePath));
                spine.Add(new EpubSpineItemInfo(position++, idref, isLinear, null, null, false, false, repeated));
                continue;
            }

            if (!isLinear)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.NonLinearSpineItem,
                    $"Spine item '{idref}' is explicitly non-linear.",
                    item.Path));
            }

            spine.Add(new EpubSpineItemInfo(
                position++,
                idref,
                isLinear,
                item.Path,
                item.MediaType,
                item.ExistsInArchive,
                item.IsSupported,
                repeated));
        }

        if (spine.Count == 0)
        {
            diagnostics.Add(Error(
                EpubDiagnosticCodes.InvalidPackage,
                "The OPF spine contains no items.",
                packagePath));
        }

        return new ParsedPackage(package, manifest, spine, navigationPaths);
    }

    private static void ReportCircularFallbackChains(
        IReadOnlyList<EpubManifestItemInfo> manifest,
        IReadOnlyDictionary<string, EpubManifestItemInfo> manifestById,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in manifest)
        {
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            var chain = new List<string>();
            var current = start;
            while (true)
            {
                if (positions.TryGetValue(current.Id, out var cycleStart))
                {
                    var cycle = chain.Skip(cycleStart).Append(current.Id).ToArray();
                    if (cycle.Any(reported.Add))
                    {
                        diagnostics.Add(Warning(
                            EpubDiagnosticCodes.CircularFallback,
                            $"Manifest fallback cycle detected: {string.Join(" -> ", cycle)}.",
                            packagePath));
                    }

                    break;
                }

                positions[current.Id] = chain.Count;
                chain.Add(current.Id);
                if (current.FallbackId is null || !manifestById.TryGetValue(current.FallbackId, out current))
                {
                    break;
                }
            }
        }
    }

    private static string? ReadPackagePath(XDocument? container, List<EpubDiagnostic> diagnostics)
    {
        var declaredPath = container?
            .Root?
            .Element(ContainerNamespace + "rootfiles")?
            .Elements(ContainerNamespace + "rootfile")
            .Select(static element => (string?)element.Attribute("full-path"))
            .FirstOrDefault(static path => !string.IsNullOrWhiteSpace(path));
        if (declaredPath is null)
        {
            diagnostics.Add(Error(
                EpubDiagnosticCodes.MissingPackage,
                "container.xml does not declare a package rootfile."));
            return null;
        }

        if (!EpubArchiveUtilities.TryNormalizeArchivePath(null, declaredPath, out var normalizedPath))
        {
            diagnostics.Add(Error(
                EpubDiagnosticCodes.UnsafePath,
                $"The package path '{declaredPath}' is unsafe.",
                declaredPath));
            return null;
        }

        return normalizedPath;
    }

    private static EpubResourceSummary CreateSummary(
        int archiveEntryCount,
        long compressedBytes,
        long uncompressedBytes,
        IReadOnlyCollection<EpubManifestItemInfo> manifest)
    {
        var mediaTypes = manifest
            .GroupBy(static item => item.MediaType, StringComparer.Ordinal)
            .OrderBy(static group => group.Key, StringComparer.Ordinal)
            .Select(static group => KeyValuePair.Create(group.Key, group.Count()));
        return new EpubResourceSummary(
            archiveEntryCount,
            manifest.Count,
            manifest.Count(static item => item.ExistsInArchive),
            manifest.Count(static item => !item.ExistsInArchive),
            manifest.Count(static item => !item.IsSupported),
            manifest.Count(static item => item.IsNavigationDocument),
            compressedBytes,
            uncompressedBytes,
            mediaTypes);
    }

    private static EpubPublicationInspection EmptyInspection(
        EpubResourceSummary resources,
        IEnumerable<EpubDiagnostic> diagnostics) =>
        new(ContainerPath, null, [], [], [], resources, diagnostics);

    private static EpubVersionFamily IdentifyVersion(string? version) => version switch
    {
        not null when version.StartsWith("2", StringComparison.Ordinal) => EpubVersionFamily.Epub2,
        not null when version.StartsWith("3", StringComparison.Ordinal) => EpubVersionFamily.Epub3,
        _ => EpubVersionFamily.Unknown,
    };

    private static bool IsStructurallySupported(string mediaType, bool isNavigation) =>
        isNavigation
        || string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase)
        || mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static string? FirstMetadataValue(XElement? metadata, XName name) =>
        metadata?.Elements(name)
            .Select(static element => NormalizedOptional(element.Value))
            .FirstOrDefault(static value => value is not null);

    private static string NormalizedText(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? NormalizedOptional(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = NormalizedText(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static long SaturatingSum(IEnumerable<long> values)
    {
        long total = 0;
        foreach (var value in values)
        {
            total = value > long.MaxValue - total ? long.MaxValue : total + value;
        }

        return total;
    }

    private static bool HasErrors(IEnumerable<EpubDiagnostic> diagnostics) =>
        diagnostics.Any(static diagnostic => diagnostic.Severity == EpubDiagnosticSeverity.Error);

    private static EpubDiagnostic Error(string code, string message, string? resource = null) =>
        new(code, EpubDiagnosticSeverity.Error, message, resource);

    private static EpubDiagnostic Warning(string code, string message, string? resource = null) =>
        new(code, EpubDiagnosticSeverity.Warning, message, resource);

    private sealed record ParsedPackage(
        EpubPackageInfo Package,
        IReadOnlyList<EpubManifestItemInfo> Manifest,
        IReadOnlyList<EpubSpineItemInfo> Spine,
        IReadOnlySet<string> NavigationDocumentPaths);
}
