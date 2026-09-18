using System.Collections.Immutable;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Flow.Core;
using Flow.Documents;

namespace Flow.Epub;

/// <summary>Security-bounded reference implementation of the experimental EPUB import profile.</summary>
public sealed class EpubImporter : IEpubImporter
{
    private const string ContainerPath = "META-INF/container.xml";
    private static readonly XNamespace ContainerNamespace = "urn:oasis:names:tc:opendocument:xmlns:container";
    private static readonly XNamespace OpfNamespace = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace XhtmlNamespace = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace EpubNamespace = "http://www.idpf.org/2007/ops";
    private static readonly XNamespace NcxNamespace = "http://www.daisy.org/z3986/2005/ncx/";
    private static readonly XNamespace MathMlNamespace = "http://www.w3.org/1998/Math/MathML";
    private static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";
    private static readonly XNamespace XLinkNamespace = "http://www.w3.org/1999/xlink";

    private readonly EpubImportLimits limits;

    /// <summary>Creates an importer with optional host-defined resource limits.</summary>
    /// <param name="limits">Limits to apply, or <see langword="null" /> for conservative defaults.</param>
    public EpubImporter(EpubImportLimits? limits = null)
    {
        this.limits = limits ?? new EpubImportLimits();
    }

    /// <inheritdoc />
    public Task<EpubImportResult> ImportAsync(
        Stream source,
        CancellationToken cancellationToken = default) => ImportAsync(source, null, cancellationToken);

    /// <inheritdoc />
    public async Task<EpubImportResult> ImportAsync(
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
        EpubImportResult Finish(
            FlowDocument? document,
            EpubMetadataReport? metadataReport = null,
            EpubPackageProcessingReport? processingReport = null,
            EpubSourceMap? sourceMap = null,
            EpubFidelitySourceSnapshot? fidelitySource = null)
        {
            telemetry.Start(EpubImportPhase.Completed, 1);
            telemetry.Advance(1, 1);
            var metrics = telemetry.Finish(document);
            return new EpubImportResult(
                document,
                diagnostics,
                metadataReport,
                processingReport,
                sourceMap,
                fidelitySource,
                metrics);
        }

        try
        {
            var archiveTotal = TryGetRemainingLength(source);
            telemetry.Start(EpubImportPhase.CopyingArchive, archiveTotal);
            await using var archiveBuffer = await EpubArchiveUtilities.CopyWithLimitAsync(
                source,
                limits.MaximumArchiveBytes,
                cancellationToken,
                copied => telemetry.Advance(copied, archiveTotal)).ConfigureAwait(false);
            using var archive = new ZipArchive(archiveBuffer, ZipArchiveMode.Read, leaveOpen: false);
            telemetry.ArchiveEntryCount = archive.Entries.Count(static entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal));
            telemetry.CompressedBytes = SaturatingSum(archive.Entries.Select(static entry => entry.CompressedLength));
            telemetry.UncompressedBytes = SaturatingSum(archive.Entries.Select(static entry => entry.Length));
            telemetry.Start(EpubImportPhase.IndexingArchive, archive.Entries.Count);
            var entries = IndexArchive(archive, diagnostics);
            telemetry.Advance(archive.Entries.Count, archive.Entries.Count);
            if (HasErrors(diagnostics))
            {
                return Finish(null);
            }

            telemetry.Start(EpubImportPhase.ReadingContainer, 1, ContainerPath);
            if (!entries.TryGetValue(ContainerPath, out var containerEntry))
            {
                diagnostics.Add(Error(EpubDiagnosticCodes.MissingContainer, $"Required resource '{ContainerPath}' was not found."));
                return Finish(null);
            }

            var container = await LoadXmlAsync(containerEntry, ContainerPath, diagnostics, cancellationToken)
                .ConfigureAwait(false);
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

                return Finish(null);
            }

            telemetry.Start(EpubImportPhase.ReadingPackage, 1, packagePath);
            var package = await LoadXmlAsync(packageEntry, packagePath, diagnostics, cancellationToken)
                .ConfigureAwait(false);
            telemetry.Advance(1, 1, packagePath);
            if (package is null)
            {
                return Finish(null);
            }

            telemetry.Start(EpubImportPhase.ProcessingManifest, 1, packagePath);
            var model = ReadPackage(package, packagePath, entries, diagnostics);
            telemetry.Advance(1, 1, packagePath);
            if (model is null)
            {
                return Finish(null);
            }

            var conversion = await ConvertPackageAsync(model, entries, diagnostics, telemetry, cancellationToken)
                .ConfigureAwait(false);
            return Finish(
                conversion.Document,
                conversion.MetadataReport ?? model.MetadataReport,
                conversion.ProcessingReport,
                conversion.SourceMap,
                conversion.FidelitySource);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EpubLimitExceededException exception)
        {
            diagnostics.Add(Error(EpubDiagnosticCodes.ArchiveLimitExceeded, exception.Message));
            return Finish(null);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or XmlException)
        {
            diagnostics.Add(Error(
                EpubDiagnosticCodes.InvalidArchive,
                $"The EPUB could not be processed safely: {exception.Message}"));
            return Finish(null);
        }
    }

    private static long? TryGetRemainingLength(Stream source)
    {
        if (!source.CanSeek)
        {
            return null;
        }

        try
        {
            return Math.Max(0, source.Length - source.Position);
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException)
        {
            return null;
        }
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

    private Dictionary<string, ZipArchiveEntry> IndexArchive(
        ZipArchive archive,
        List<EpubDiagnostic> diagnostics) =>
        EpubArchiveUtilities.IndexArchive(archive, limits, diagnostics);

    private Task<XDocument?> LoadXmlAsync(
        ZipArchiveEntry entry,
        string resource,
        List<EpubDiagnostic> diagnostics,
        CancellationToken cancellationToken) =>
        EpubArchiveUtilities.LoadXmlAsync(entry, resource, limits, diagnostics, cancellationToken);

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

        if (!TryNormalizeArchivePath(null, declaredPath, out var normalizedPath))
        {
            diagnostics.Add(Error(
                EpubDiagnosticCodes.UnsafePath,
                $"The package path '{declaredPath}' is unsafe.",
                declaredPath));
            return null;
        }

        return normalizedPath;
    }

    private static PackageModel? ReadPackage(
        XDocument package,
        string packagePath,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        List<EpubDiagnostic> diagnostics)
    {
        var root = package.Root;
        if (root?.Name != OpfNamespace + "package")
        {
            diagnostics.Add(Error(
                EpubDiagnosticCodes.InvalidPackage,
                "The package document does not contain an OPF package root.",
                packagePath));
            return null;
        }

        var metadata = EpubMetadataReader.Read(root, packagePath, diagnostics);

        var manifest = new Dictionary<string, ManifestItem>(StringComparer.Ordinal);
        var manifestOrder = new List<ManifestItem>();
        foreach (var item in root.Element(OpfNamespace + "manifest")?.Elements(OpfNamespace + "item") ?? [])
        {
            var id = (string?)item.Attribute("id");
            var href = (string?)item.Attribute("href");
            var mediaType = (string?)item.Attribute("media-type");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(mediaType))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.InvalidPackage,
                    "A manifest item is missing id, href, or media-type.",
                    packagePath));
                continue;
            }

            if (!TryNormalizeArchivePath(GetDirectory(packagePath), href, out var resourcePath))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.UnsafePath,
                    $"Manifest item '{id}' has unsafe href '{href}'.",
                    packagePath));
                continue;
            }

            var properties = ((string?)item.Attribute("properties") ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToImmutableHashSet(StringComparer.Ordinal);
            var manifestItem = new ManifestItem(
                id,
                resourcePath,
                mediaType,
                properties,
                NormalizeOptional((string?)item.Attribute("fallback")),
                NormalizeOptional((string?)item.Attribute("media-overlay")),
                entries.ContainsKey(resourcePath));
            if (!manifest.TryAdd(id, manifestItem))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.InvalidPackage,
                    $"Manifest ID '{id}' occurs more than once.",
                    packagePath));
            }
            else
            {
                if (manifestOrder.Any(existing => string.Equals(existing.Path, resourcePath, StringComparison.Ordinal)))
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.DuplicateManifestResource,
                        $"Manifest item '{id}' repeats resource path '{resourcePath}' under another ID.",
                        packagePath));
                }

                manifestOrder.Add(manifestItem);
                if (!manifestItem.ExistsInArchive)
                {
                    diagnostics.Add(Error(
                        EpubDiagnosticCodes.MissingResource,
                        $"Manifest resource '{resourcePath}' is missing from the archive.",
                        resourcePath));
                }

                if (manifestItem.MediaOverlayId is not null)
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.UnsupportedMediaOverlay,
                        $"Manifest item '{id}' declares media overlay '{manifestItem.MediaOverlayId}'; timing and audio are not imported.",
                        resourcePath));
                }

                foreach (var property in manifestItem.Properties.Where(static property =>
                             property is not "nav" and not "cover-image"))
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.UnsupportedManifestProperty,
                        $"Manifest property '{property}' on item '{id}' is retained in the processing report but is not interpreted.",
                        resourcePath));
                }
            }
        }

        foreach (var item in manifestOrder)
        {
            if (item.FallbackId is not null && !manifest.ContainsKey(item.FallbackId))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.BrokenFallback,
                    $"Manifest item '{item.Id}' references missing fallback item '{item.FallbackId}'.",
                    item.Path));
            }

            if (item.MediaOverlayId is not null && !manifest.ContainsKey(item.MediaOverlayId))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.UnsupportedMediaOverlay,
                    $"Media overlay reference '{item.MediaOverlayId}' from item '{item.Id}' does not resolve in the manifest.",
                    item.Path));
            }
        }

        ReportCircularFallbackChains(manifestOrder, manifest, packagePath, diagnostics);

        var spine = new List<SpineItem>();
        var spineElement = root.Element(OpfNamespace + "spine");
        var spineTocId = (string?)spineElement?.Attribute("toc");
        var spineOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var position = 0;
        foreach (var itemReference in spineElement?.Elements(OpfNamespace + "itemref") ?? [])
        {
            var idref = NormalizeOptional((string?)itemReference.Attribute("idref"));
            var linearValue = NormalizeOptional((string?)itemReference.Attribute("linear"));
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

            ManifestItem? item = null;
            if (idref is null || !manifest.TryGetValue(idref, out item))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.InvalidPackage,
                    $"Spine reference '{idref ?? "(missing)"}' does not resolve to a manifest item.",
                    packagePath));
            }
            else if (!isLinear)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.NonLinearSpineItem,
                    $"Non-linear spine item '{idref}' is imported in declared order so its content is not lost.",
                    item.Path));
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
                    item?.Path ?? packagePath));
            }

            var stableIdentity = idref is null
                ? $"missing-spine-{position + 1}"
                : occurrence == 1 ? idref : $"{idref}-occurrence-{occurrence}";
            spine.Add(new SpineItem(position++, idref, item, isLinear, repeated, stableIdentity));
        }

        if (spine.Count == 0)
        {
            diagnostics.Add(Error(EpubDiagnosticCodes.InvalidPackage, "The OPF spine contains no resolvable items.", packagePath));
            return null;
        }

        var cover = ResolveCoverMetadata(metadata.Epub2CoverItemId, manifestOrder, packagePath, diagnostics);
        var metadataReport = metadata.Report.WithCover(cover);

        return new PackageModel(
            packagePath,
            metadata.Title,
            metadata.Subtitle,
            metadata.Identifier,
            metadata.Language,
            metadata.Authors,
            metadata.Description,
            metadataReport,
            manifest,
            manifestOrder,
            spine,
            spineTocId);
    }

    private static void ReportCircularFallbackChains(
        IReadOnlyList<ManifestItem> manifestOrder,
        IReadOnlyDictionary<string, ManifestItem> manifest,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in manifestOrder)
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
                if (current.FallbackId is null || !manifest.TryGetValue(current.FallbackId, out current))
                {
                    break;
                }
            }
        }
    }

    private static EpubCoverMetadata? ResolveCoverMetadata(
        string? epub2CoverItemId,
        IReadOnlyList<ManifestItem> manifest,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        var epub3Candidates = manifest.Where(static item => item.Properties.Contains("cover-image")).ToArray();
        if (epub3Candidates.Length > 1)
        {
            diagnostics.Add(Warning(
                EpubDiagnosticCodes.MetadataConflict,
                "Multiple manifest items declare the EPUB 3 cover-image property; the first in manifest order is retained.",
                packagePath));
        }

        var epub2Candidate = epub2CoverItemId is null
            ? null
            : manifest.FirstOrDefault(item => string.Equals(item.Id, epub2CoverItemId, StringComparison.Ordinal));
        if (epub2CoverItemId is not null && epub2Candidate is null)
        {
            diagnostics.Add(Warning(
                EpubDiagnosticCodes.InvalidMetadata,
                $"EPUB 2 cover metadata references unknown manifest item '{epub2CoverItemId}'.",
                packagePath));
        }

        if (epub3Candidates.FirstOrDefault() is { } epub3Candidate
            && epub2Candidate is not null
            && !string.Equals(epub3Candidate.Id, epub2Candidate.Id, StringComparison.Ordinal))
        {
            diagnostics.Add(Warning(
                EpubDiagnosticCodes.MetadataConflict,
                $"EPUB 3 cover '{epub3Candidate.Id}' conflicts with EPUB 2 cover '{epub2Candidate.Id}'; EPUB 3 takes precedence.",
                packagePath));
        }

        var selected = epub3Candidates.FirstOrDefault() ?? epub2Candidate;
        if (selected is null)
        {
            return null;
        }

        if (!selected.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(Warning(
                EpubDiagnosticCodes.InvalidMetadata,
                $"Cover manifest item '{selected.Id}' has non-image media type '{selected.MediaType}'.",
                packagePath));
        }

        return new EpubCoverMetadata(
            selected.Id,
            selected.Path,
            selected.MediaType,
            epub3Candidates.Length > 0 ? "epub3-cover-image" : "epub2-meta-cover");
    }

    private async Task<PackageConversionResult> ConvertPackageAsync(
        PackageModel package,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        List<EpubDiagnostic> diagnostics,
        EpubImportTelemetryCollector telemetry,
        CancellationToken cancellationToken)
    {
        var xhtmlDocuments = new List<XhtmlModel>();
        var spineDecisions = new List<EpubSpineProcessingDecision>(package.Spine.Count);
        telemetry.Start(EpubImportPhase.ProcessingSpine, package.Spine.Count);
        for (var spineIndex = 0; spineIndex < package.Spine.Count; spineIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var spineItem = package.Spine[spineIndex];
            try
            {
                var resolution = ResolveSpineItem(spineItem, package.Manifest, diagnostics);
                var decision = resolution.Decision;
                if (resolution.Selected is null)
                {
                    spineDecisions.Add(decision);
                    continue;
                }

                var selected = resolution.Selected;
                if (!entries.TryGetValue(selected.Path, out var entry))
                {
                    spineDecisions.Add(decision with
                    {
                        Disposition = EpubSpineDisposition.Excluded,
                        Reason = EpubSpineDecisionReason.MissingArchiveResource,
                        SelectedItemId = null,
                        SelectedResourcePath = null,
                    });
                    continue;
                }

                var xhtml = await LoadXmlAsync(entry, selected.Path, diagnostics, cancellationToken)
                    .ConfigureAwait(false);
                if (xhtml?.Root?.Name != XhtmlNamespace + "html"
                    || xhtml.Root.Element(XhtmlNamespace + "body") is null)
                {
                    if (xhtml is not null)
                    {
                        diagnostics.Add(Error(
                            EpubDiagnosticCodes.InvalidXml,
                            "The selected spine resource must be XHTML in the XHTML namespace and contain a body.",
                            selected.Path));
                    }

                    spineDecisions.Add(decision with
                    {
                        Disposition = EpubSpineDisposition.Excluded,
                        Reason = EpubSpineDecisionReason.InvalidXhtml,
                        SelectedItemId = null,
                        SelectedResourcePath = null,
                    });
                    continue;
                }

                xhtmlDocuments.Add(new XhtmlModel(selected, xhtml, spineItem.StableIdentity));
                spineDecisions.Add(decision);
            }
            finally
            {
                telemetry.Advance(
                    spineIndex + 1,
                    package.Spine.Count,
                    spineItem.Item?.Path ?? spineItem.IdRef ?? package.PackagePath);
            }
        }

        telemetry.SpineDocumentsProcessed = xhtmlDocuments.Count;

        var processingReport = new EpubPackageProcessingReport(
            package.ManifestOrder.Select(static item => new EpubManifestResourceDecision(
                item.Id,
                item.Path,
                item.MediaType,
                item.Properties,
                item.FallbackId,
                item.MediaOverlayId,
                item.ExistsInArchive)),
            spineDecisions);

        if (xhtmlDocuments.Count == 0)
        {
            return new PackageConversionResult(
                null,
                processingReport,
                null,
                package.MetadataReport,
                CreateFidelitySource(package, processingReport, [], [], diagnostics));
        }

        telemetry.Start(EpubImportPhase.ConvertingContent, xhtmlDocuments.Count);
        var navigationDocuments = await LoadNavigationDocumentsAsync(
            package,
            entries,
            xhtmlDocuments,
            diagnostics,
            cancellationToken).ConfigureAwait(false);

        var context = new ConversionContext(
            entries,
            package.Manifest,
            diagnostics,
            limits,
            package.MetadataReport.Cover,
            package.Title);
        foreach (var xhtml in xhtmlDocuments)
        {
            context.ConsumedResourcePaths.Add(xhtml.Item.Path);
        }

        await context.LoadCssAsync(xhtmlDocuments, cancellationToken).ConfigureAwait(false);
        context.PrepareIds(xhtmlDocuments);
        context.PrepareFootnotes(xhtmlDocuments);
        var coverAssetId = await context.ImportCoverAsync(package.MetadataReport.Cover, cancellationToken)
            .ConfigureAwait(false);
        var content = new List<DocumentNode>();
        var tableOfContents = context.ConvertTableOfContents(navigationDocuments, package.SpineTocId);
        if (tableOfContents is not null)
        {
            content.Add(tableOfContents);
        }

        for (var xhtmlIndex = 0; xhtmlIndex < xhtmlDocuments.Count; xhtmlIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var xhtml = xhtmlDocuments[xhtmlIndex];
            content.Add(await context.ConvertChapterAsync(xhtml, cancellationToken).ConfigureAwait(false));
            telemetry.Advance(xhtmlIndex + 1, xhtmlDocuments.Count, xhtml.Item.Path);
        }

        telemetry.Start(EpubImportPhase.FinalizingDocument, 1, package.PackagePath);
        var metadataReport = coverAssetId is null || package.MetadataReport.Cover is null
            ? package.MetadataReport
            : package.MetadataReport.WithCover(package.MetadataReport.Cover with { AssetId = coverAssetId });

        context.FlushUnsupportedDiagnostics();

        foreach (var navigation in navigationDocuments)
        {
            context.ConsumedResourcePaths.Add(navigation.Item.Path);
        }

        if (package.MetadataReport.Cover is { } cover && !entries.ContainsKey(cover.Path))
        {
            diagnostics.Add(Error(
                EpubDiagnosticCodes.MissingResource,
                $"Cover resource '{cover.Path}' declared by OPF metadata is missing from the archive.",
                cover.Path));
        }

        ReportUnusedManifestResources(package, context, diagnostics);

        var documentId = CreateDocumentId(package.Identifier, package.PackagePath, diagnostics);
        var document = new FlowDocument(
            new DocumentIdentity(documentId),
            new DocumentMetadata(
                package.Title,
                package.Language,
                package.Authors,
                package.Subtitle,
                package.Description),
            new DocumentContent(content),
            context.Assets.Values,
            context.CreatePresentation());

        var validation = new DocumentValidator().Validate(document);
        foreach (var validationDiagnostic in validation.Diagnostics)
        {
            diagnostics.Add(new EpubDiagnostic(
                EpubDiagnosticCodes.DocumentValidation,
                validationDiagnostic.Severity == ValidationSeverity.Error
                    ? EpubDiagnosticSeverity.Error
                    : EpubDiagnosticSeverity.Warning,
                $"{validationDiagnostic.Code}: {validationDiagnostic.Message}"));
        }

        telemetry.Advance(1, 1, package.PackagePath);

        return new PackageConversionResult(
            document,
            processingReport,
            context.CreateSourceMap(document),
            metadataReport,
            CreateFidelitySource(package, processingReport, xhtmlDocuments, navigationDocuments, diagnostics));
    }

    private static EpubFidelitySourceSnapshot CreateFidelitySource(
        PackageModel package,
        EpubPackageProcessingReport processingReport,
        IEnumerable<XhtmlModel> xhtmlDocuments,
        IReadOnlyList<NavigationModel> navigationDocuments,
        IReadOnlyCollection<EpubDiagnostic> diagnostics)
    {
        var counts = new List<EpubFidelitySourceCount>();
        foreach (var spine in processingReport.Spine)
        {
            var status = spine.Disposition == EpubSpineDisposition.Excluded
                ? spine.Reason == EpubSpineDecisionReason.UnsupportedMediaType
                    ? EpubFidelityStatus.Unsupported
                    : EpubFidelityStatus.Lost
                : (EpubFidelityStatus?)null;
            Add(
                spine.ReadingRole == EpubSpineReadingRole.Linear
                    ? EpubFidelityMetric.LinearSpineItems
                    : EpubFidelityMetric.NonLinearSpineItems,
                spine.SelectedResourcePath ?? package.PackagePath,
                1,
                status);
        }

        foreach (var manifest in processingReport.Manifest)
        {
            var status = !manifest.ExistsInArchive
                ? EpubFidelityStatus.Lost
                : diagnostics.Any(diagnostic => diagnostic.Code == EpubDiagnosticCodes.UnsupportedResource
                                                && string.Equals(
                                                    diagnostic.Resource,
                                                    manifest.Path,
                                                    StringComparison.Ordinal))
                    ? EpubFidelityStatus.Unsupported
                    : diagnostics.Any(diagnostic =>
                        diagnostic.Code == EpubDiagnosticCodes.EmbeddedFontBytesNotPreserved
                        && string.Equals(diagnostic.Resource, manifest.Path, StringComparison.Ordinal))
                        ? EpubFidelityStatus.Approximated
                    : (EpubFidelityStatus?)null;
            Add(EpubFidelityMetric.ManifestResources, manifest.Path, 1, status);
        }
        Add(EpubFidelityMetric.Covers, package.MetadataReport.Cover?.Path, package.MetadataReport.Cover is null ? 0 : 1);

        foreach (var xhtml in xhtmlDocuments)
        {
            var body = xhtml.Document.Root?.Element(XhtmlNamespace + "body");
            if (body is null)
            {
                continue;
            }

            var elements = body.Descendants().ToArray();
            Add(
                EpubFidelityMetric.SignificantCharacters,
                xhtml.Item.Path,
                body.DescendantNodes().OfType<XText>().Sum(static text => EpubFidelityAnalyzer.CountSignificant(text.Value)));
            Add(EpubFidelityMetric.Headings, xhtml.Item.Path, elements.LongCount(IsHeading));
            Add(EpubFidelityMetric.Paragraphs, xhtml.Item.Path, elements.LongCount(static element => IsXhtml(element, "p")));
            var ordinaryLinks = elements
                .Where(static element => IsXhtml(element, "a")
                                         && HasHref(element)
                                         && !IsTableOfContentsLink(element)
                                         && !IsNoteReference(element))
                .ToArray();
            AddLinkCounts(EpubFidelityMetric.InternalLinks, ordinaryLinks.Where(static element => !IsExternalHref(element)));
            AddLinkCounts(EpubFidelityMetric.ExternalLinks, ordinaryLinks.Where(IsExternalHref));
            Add(
                EpubFidelityMetric.Images,
                xhtml.Item.Path,
                elements.LongCount(static element => IsXhtml(element, "img") || element.Name == SvgNamespace + "image"));
            Add(EpubFidelityMetric.Notes, xhtml.Item.Path, elements.LongCount(IsNote));
            Add(EpubFidelityMetric.NoteReferences, xhtml.Item.Path, elements.LongCount(IsNoteReference));
            Add(EpubFidelityMetric.Tables, xhtml.Item.Path, elements.LongCount(static element => IsXhtml(element, "table")));
            Add(EpubFidelityMetric.TableRows, xhtml.Item.Path, elements.LongCount(static element => IsXhtml(element, "tr")));
            Add(
                EpubFidelityMetric.TableCells,
                xhtml.Item.Path,
                elements.LongCount(static element => IsXhtml(element, "td") || IsXhtml(element, "th")));
            Add(
                EpubFidelityMetric.UnknownOrUnrepresentableElements,
                xhtml.Item.Path,
                elements.LongCount(static element => element.Name.Namespace == XhtmlNamespace
                                                    && (!SupportedXhtmlElements.Contains(element.Name.LocalName)
                                                        || KnownUnrepresentableXhtmlElements.Contains(
                                                            element.Name.LocalName))),
                EpubFidelityStatus.Approximated);

            void AddLinkCounts(EpubFidelityMetric metric, IEnumerable<XElement> links)
            {
                var items = links.ToArray();
                Add(metric, xhtml.Item.Path, items.LongLength);
            }
        }

        var epub3Tocs = navigationDocuments
            .Where(static item => item.Kind == NavigationKind.Epub3)
            .SelectMany(static item => item.Document.Descendants(XhtmlNamespace + "nav"))
            .Where(static nav => HasAttributeToken((string?)nav.Attribute(EpubNamespace + "type"), "toc"))
            .ToArray();
        if (epub3Tocs.Length > 0)
        {
            foreach (var navigation in navigationDocuments.Where(static item => item.Kind == NavigationKind.Epub3))
            {
                var total = navigation.Document.Descendants(XhtmlNamespace + "nav")
                    .Where(static nav => HasAttributeToken((string?)nav.Attribute(EpubNamespace + "type"), "toc"))
                    .Sum(static nav => nav.Descendants(XhtmlNamespace + "a").LongCount());
                Add(EpubFidelityMetric.TableOfContentsEntries, navigation.Item.Path, total);
            }
        }
        else
        {
            foreach (var navigation in navigationDocuments.Where(static item => item.Kind == NavigationKind.Ncx))
            {
                Add(
                    EpubFidelityMetric.TableOfContentsEntries,
                    navigation.Item.Path,
                    navigation.Document.Descendants(NcxNamespace + "navPoint").LongCount());
            }
        }

        return new EpubFidelitySourceSnapshot(counts);

        void Add(
            EpubFidelityMetric metric,
            string? resource,
            long count,
            EpubFidelityStatus? status = null)
        {
            if (count > 0)
            {
                counts.Add(new EpubFidelitySourceCount(metric, resource, count, status));
            }
        }
    }

    private static bool IsXhtml(XElement element, string localName) =>
        element.Name == XhtmlNamespace + localName;

    private static bool IsHeading(XElement element) =>
        element.Name.Namespace == XhtmlNamespace
        && element.Name.LocalName.Length == 2
        && element.Name.LocalName[0] == 'h'
        && element.Name.LocalName[1] is >= '1' and <= '6';

    private static bool IsExternalHref(XElement element)
    {
        var href = (string?)element.Attribute("href");
        if (href?.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) == true)
        {
            return IsSafeMailtoHref(href);
        }

        return href is not null && Uri.TryCreate(href, UriKind.Absolute, out var uri)
                                && uri.Scheme is "http" or "https" or "tel";
    }

    private static bool HasHref(XElement element) =>
        !string.IsNullOrWhiteSpace((string?)element.Attribute("href"));

    private static bool IsSafeMailtoHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)
            || !href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || href.Length == "mailto:".Length
            || href.Any(static character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            return false;
        }

        try
        {
            return !Uri.UnescapeDataString(href).Any(char.IsControl);
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private static bool IsTableOfContentsLink(XElement element) => element
        .Ancestors(XhtmlNamespace + "nav")
        .Any(static nav => HasAttributeToken((string?)nav.Attribute(EpubNamespace + "type"), "toc"));

    private static bool IsNote(XElement element) =>
        HasAttributeToken((string?)element.Attribute(EpubNamespace + "type"), "footnote")
        || HasAttributeToken((string?)element.Attribute(EpubNamespace + "type"), "endnote")
        || HasAttributeToken((string?)element.Attribute("role"), "doc-footnote");

    private static bool IsNoteReference(XElement element) =>
        HasAttributeToken((string?)element.Attribute(EpubNamespace + "type"), "noteref")
        || HasAttributeToken((string?)element.Attribute("role"), "doc-noteref");

    private static bool HasAttributeToken(string? values, string token) =>
        values?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Contains(token, StringComparer.OrdinalIgnoreCase) == true;

    private static readonly HashSet<string> SupportedXhtmlElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "abbr", "address", "article", "aside", "b", "bdi", "bdo", "blockquote", "body", "br",
        "caption", "cite", "code", "dd", "details", "div", "dl", "dt", "em", "figcaption", "figure",
        "footer", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr", "i", "img", "li", "main",
        "mark", "nav", "ol", "p", "picture", "pre", "q", "rp", "rt", "ruby", "s", "section", "small",
        "source", "span", "strong", "sub", "summary", "sup", "table", "tbody", "td", "tfoot", "th",
        "thead", "time", "tr", "u", "ul",
    };

    private static readonly HashSet<string> KnownUnrepresentableXhtmlElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "abbr", "address", "article", "aside", "cite", "dd", "details", "div", "dl", "dt", "footer",
        "header", "main", "mark", "q", "small", "span", "sub", "summary", "sup", "time",
    };

    private static SpineResolution ResolveSpineItem(
        SpineItem spineItem,
        IReadOnlyDictionary<string, ManifestItem> manifest,
        List<EpubDiagnostic> diagnostics)
    {
        var readingRole = spineItem.IsLinear
            ? EpubSpineReadingRole.Linear
            : EpubSpineReadingRole.Supplemental;
        if (spineItem.Item is null)
        {
            return Excluded(EpubSpineDecisionReason.MissingManifestItem, []);
        }

        var source = spineItem.Item;
        var current = source;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var chain = new List<string>();
        while (true)
        {
            chain.Add(current.Id);
            if (!visited.Add(current.Id))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.CircularFallback,
                    $"Fallback chain for spine item '{spineItem.IdRef}' is circular: {string.Join(" -> ", chain)}.",
                    source.Path));
                return Excluded(EpubSpineDecisionReason.CircularFallback, chain);
            }

            if (current.ExistsInArchive
                && string.Equals(current.MediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
            {
                var substituted = !string.Equals(current.Id, source.Id, StringComparison.Ordinal);
                return new SpineResolution(
                    current,
                    new EpubSpineProcessingDecision(
                        spineItem.Position,
                        spineItem.IdRef,
                        readingRole,
                        spineItem.IsRepeated,
                        substituted ? EpubSpineDisposition.Substituted : EpubSpineDisposition.Included,
                        substituted ? EpubSpineDecisionReason.XhtmlFallback : EpubSpineDecisionReason.DirectXhtml,
                        current.Id,
                        current.Path,
                        chain.ToImmutableArray()));
            }

            if (current.FallbackId is null)
            {
                if (current.ExistsInArchive)
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.UnsupportedResource,
                        $"Spine item '{spineItem.IdRef}' ends at unsupported media type '{current.MediaType}' without an XHTML fallback.",
                        current.Path));
                    return Excluded(EpubSpineDecisionReason.UnsupportedMediaType, chain);
                }

                return Excluded(EpubSpineDecisionReason.MissingArchiveResource, chain);
            }

            if (!manifest.TryGetValue(current.FallbackId, out var fallback))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.BrokenFallback,
                    $"Fallback chain for spine item '{spineItem.IdRef}' references missing item '{current.FallbackId}'.",
                    current.Path));
                chain.Add(current.FallbackId);
                return Excluded(EpubSpineDecisionReason.BrokenFallback, chain);
            }

            current = fallback;
        }

        SpineResolution Excluded(EpubSpineDecisionReason reason, IEnumerable<string> fallbackChain) => new(
            null,
            new EpubSpineProcessingDecision(
                spineItem.Position,
                spineItem.IdRef,
                readingRole,
                spineItem.IsRepeated,
                EpubSpineDisposition.Excluded,
                reason,
                null,
                null,
                fallbackChain.ToImmutableArray()));
    }

    private async Task<IReadOnlyList<NavigationModel>> LoadNavigationDocumentsAsync(
        PackageModel package,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        IReadOnlyList<XhtmlModel> xhtmlDocuments,
        List<EpubDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var xhtmlByPath = xhtmlDocuments
            .GroupBy(static item => item.Item.Path, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        var candidates = package.ManifestOrder
            .Where(static item => item.Properties.Contains("nav")
                || string.Equals(item.MediaType, "application/x-dtbncx+xml", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var result = new List<NavigationModel>(candidates.Length);
        foreach (var item in candidates)
        {
            XDocument? document;
            if (xhtmlByPath.TryGetValue(item.Path, out var loaded))
            {
                document = loaded.Document;
            }
            else if (entries.TryGetValue(item.Path, out var entry))
            {
                document = await LoadXmlAsync(entry, item.Path, diagnostics, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.MissingResource,
                    "A navigation resource declared by the package is missing from the archive.",
                    item.Path));
                continue;
            }

            if (document is not null)
            {
                result.Add(new NavigationModel(
                    item,
                    document,
                    item.Properties.Contains("nav") ? NavigationKind.Epub3 : NavigationKind.Ncx));
            }
        }

        return result;
    }

    private static void ReportUnusedManifestResources(
        PackageModel package,
        ConversionContext context,
        List<EpubDiagnostic> diagnostics)
    {
        var spinePaths = package.Spine
            .Where(static item => item.Item is not null)
            .Select(static item => item.Item!.Path)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var item in package.ManifestOrder)
        {
            if (spinePaths.Contains(item.Path) || context.ConsumedResourcePaths.Contains(item.Path))
            {
                continue;
            }

            if (!item.ExistsInArchive)
            {
                continue;
            }

            if (EpubMediaTypeClassifier.IsEmbeddedFont(item.MediaType))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.EmbeddedFontBytesNotPreserved,
                    $"Embedded font resource '{item.Id}' ({item.MediaType}) is recognized, but its bytes are not retained by Flow; rendering may use an installed font family or a renderer fallback.",
                    item.Path));
                continue;
            }

            diagnostics.Add(Warning(
                EpubDiagnosticCodes.UnsupportedResource,
                $"Manifest resource '{item.Id}' ({item.MediaType}) is not part of the imported reading content.",
                item.Path));
        }
    }

    private static DocumentId CreateDocumentId(
        string? identifier,
        string packagePath,
        List<EpubDiagnostic> diagnostics)
    {
        if (identifier is not null && Uri.TryCreate(identifier, UriKind.Absolute, out _))
        {
            return new DocumentId(identifier);
        }

        var source = identifier ?? packagePath;
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
        diagnostics.Add(Warning(
            EpubDiagnosticCodes.MetadataFallback,
            "The EPUB identifier is absent or is not an absolute URI; a deterministic Flow URN was generated.",
            packagePath));
        return new DocumentId($"urn:flow:epub:{digest}");
    }

    private static Task<MemoryStream> CopyWithLimitAsync(
        Stream source,
        long maximumBytes,
        CancellationToken cancellationToken) =>
        EpubArchiveUtilities.CopyWithLimitAsync(source, maximumBytes, cancellationToken);

    private static bool TryNormalizeArchivePath(string? baseDirectory, string value, out string normalized) =>
        EpubArchiveUtilities.TryNormalizeArchivePath(baseDirectory, value, out normalized);

    private static string GetDirectory(string path) => EpubArchiveUtilities.GetDirectory(path);

    private static string NormalizedText(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? NormalizeOptional(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = NormalizedText(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool HasErrors(IEnumerable<EpubDiagnostic> diagnostics) =>
        diagnostics.Any(static diagnostic => diagnostic.Severity == EpubDiagnosticSeverity.Error);

    private static EpubDiagnostic Error(string code, string message, string? resource = null) =>
        new(code, EpubDiagnosticSeverity.Error, message, resource);

    private static EpubDiagnostic Warning(string code, string message, string? resource = null, int count = 1) =>
        new(code, EpubDiagnosticSeverity.Warning, message, resource, count);

    private sealed record ManifestItem(
        string Id,
        string Path,
        string MediaType,
        ImmutableHashSet<string> Properties,
        string? FallbackId,
        string? MediaOverlayId,
        bool ExistsInArchive);

    private sealed record SpineItem(
        int Position,
        string? IdRef,
        ManifestItem? Item,
        bool IsLinear,
        bool IsRepeated,
        string StableIdentity);

    private sealed record PackageModel(
        string PackagePath,
        string Title,
        string? Subtitle,
        string? Identifier,
        string? Language,
        ImmutableArray<string> Authors,
        string? Description,
        EpubMetadataReport MetadataReport,
        IReadOnlyDictionary<string, ManifestItem> Manifest,
        IReadOnlyList<ManifestItem> ManifestOrder,
        IReadOnlyList<SpineItem> Spine,
        string? SpineTocId);

    private sealed record XhtmlModel(ManifestItem Item, XDocument Document, string StableIdentity);

    private sealed record PackageConversionResult(
        FlowDocument? Document,
        EpubPackageProcessingReport ProcessingReport,
        EpubSourceMap? SourceMap,
        EpubMetadataReport? MetadataReport,
        EpubFidelitySourceSnapshot FidelitySource);

    private sealed record SpineResolution(
        ManifestItem? Selected,
        EpubSpineProcessingDecision Decision);

    private enum NavigationKind
    {
        Epub3,
        Ncx,
    }

    private sealed record NavigationModel(ManifestItem Item, XDocument Document, NavigationKind Kind);

    private sealed record NavigationEntryModel(
        ImmutableArray<InlineNode> Label,
        string Href,
        int Level,
        string? LogicalId);

    private sealed record ParsedNavigation(
        NavigationModel Source,
        ImmutableArray<InlineNode> Title,
        ImmutableArray<NavigationEntryModel> Entries);

    private sealed class ConversionContext
    {
        private readonly IReadOnlyDictionary<string, ZipArchiveEntry> entries;
        private readonly IReadOnlyDictionary<string, ManifestItem> manifest;
        private readonly List<EpubDiagnostic> diagnostics;
        private readonly EpubImportLimits limits;
        private readonly EpubCoverMetadata? coverMetadata;
        private readonly string publicationTitle;
        private readonly Dictionary<XElement, NodeId> elementIds = [];
        private readonly Dictionary<string, NodeId> anchors = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AssetId> assetIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AssetId> assetHashes = new(StringComparer.Ordinal);
        private readonly HashSet<string> allocatedIds = new(StringComparer.Ordinal);
        private readonly List<EpubSourceLocation> sourceLocations = [];
        private readonly Dictionary<SourceLocationKey, int> sourceOccurrences = [];
        private readonly Dictionary<UnsupportedElementKey, int> unsupportedElements = [];
        private readonly Dictionary<string, int> approximatedAnchors = new(StringComparer.Ordinal);
        private readonly Dictionary<UnsupportedElementKey, int> mathLosses = [];
        private readonly Dictionary<SvgImageIssueKey, int> svgImageIssues = [];
        private readonly Dictionary<HeadingLevelNormalizationKey, int> headingLevelNormalizations = [];
        private readonly Dictionary<string, int> noteResourceFallbacks = new(StringComparer.Ordinal);
        private readonly HashSet<XElement> footnoteElements = [];
        private readonly Dictionary<XElement, NodeId> footnoteReferenceTargets = [];
        private readonly HashSet<XElement> invalidFootnoteReferences = [];
        private IReadOnlyDictionary<XElement, TypographyStyle> cssStyles = new Dictionary<XElement, TypographyStyle>();
        private AssetId? coverAssetId;
        private int? previousHeadingLevel;
        private int generatedId;

        internal ConversionContext(
            IReadOnlyDictionary<string, ZipArchiveEntry> entries,
            IReadOnlyDictionary<string, ManifestItem> manifest,
            List<EpubDiagnostic> diagnostics,
            EpubImportLimits limits,
            EpubCoverMetadata? coverMetadata,
            string publicationTitle)
        {
            this.entries = entries;
            this.manifest = manifest;
            this.diagnostics = diagnostics;
            this.limits = limits;
            this.coverMetadata = coverMetadata;
            this.publicationTitle = publicationTitle;
        }

        internal Dictionary<AssetId, FlowAsset> Assets { get; } = [];

        internal Dictionary<NodeId, TypographyStyle> NodeTypography { get; } = [];

        internal NodeId? CoverFigureId { get; private set; }

        internal HashSet<string> ConsumedResourcePaths { get; } = new(StringComparer.Ordinal);

        internal async Task LoadCssAsync(
            IEnumerable<XhtmlModel> xhtmlDocuments,
            CancellationToken cancellationToken)
        {
            var mediaTypes = manifest.Values
                .GroupBy(static item => item.Path, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.First().MediaType, StringComparer.Ordinal);
            var processor = new EpubCssProcessor(entries, mediaTypes, limits, diagnostics, ConsumedResourcePaths);
            cssStyles = await processor.ProcessAsync(
                xhtmlDocuments.Select(static item => (item.Item.Path, item.Document)),
                cancellationToken).ConfigureAwait(false);
        }

        internal async Task<AssetId?> ImportCoverAsync(
            EpubCoverMetadata? cover,
            CancellationToken cancellationToken)
        {
            if (cover is null)
            {
                return null;
            }

            var imported = await TryImportImageAssetAsync(cover.Path, "publication cover", cancellationToken)
                .ConfigureAwait(false);
            coverAssetId = imported?.AssetId;
            return coverAssetId;
        }

        internal DocumentPresentation? CreatePresentation()
        {
            if (NodeTypography.Count == 0 && CoverFigureId is null)
            {
                return null;
            }

            return new DocumentPresentation(
                nodeTypography: NodeTypography,
                cover: CoverFigureId is null ? null : new CoverPresentation(CoverFigureId));
        }

        internal EpubSourceMap CreateSourceMap(FlowDocument document)
        {
            var mapped = new List<EpubSourceLocation>(sourceLocations.Count);
            foreach (var location in sourceLocations)
            {
                if (document.Index.TryGetUniqueNode(location.NodeId, out _))
                {
                    mapped.Add(location);
                    continue;
                }

                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.UnmappedSourceLocation,
                    $"Source location '{FormatSourceLocation(location.ResourcePath, location.Fragment)}' did not produce a semantic Flow node.",
                    location.ResourcePath));
            }

            return new EpubSourceMap(mapped);
        }

        internal void FlushUnsupportedDiagnostics()
        {
            foreach (var (key, count) in unsupportedElements)
            {
                var occurrenceText = count == 1 ? "once" : $"{count} times";
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.UnsupportedElement,
                    $"{key.Description} has no direct Flow equivalent and occurred {occurrenceText}; recoverable text and child order were preserved.",
                    key.ResourcePath));
            }

            foreach (var (resourcePath, count) in approximatedAnchors)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.UnsupportedElement,
                    $"{count} anchor(s) attached to inline or unsupported elements were mapped to their containing Flow blocks; source fragments remain available through the source map.",
                    resourcePath));
            }

            foreach (var (key, count) in mathLosses)
            {
                var occurrenceText = count == 1 ? "once" : $"{count} times";
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.MathSemanticLoss,
                    $"{key.Description} occurred {occurrenceText}; safe child content or an available textual fallback was preserved.",
                    key.ResourcePath));
            }

            foreach (var (key, count) in svgImageIssues)
            {
                var occurrenceText = count == 1 ? "once" : $"{count} times";
                diagnostics.Add(Warning(
                    key.Code,
                    $"{key.Message} This occurred {occurrenceText}.",
                    key.ResourcePath));
            }

            foreach (var (key, count) in headingLevelNormalizations
                         .OrderBy(static item => item.Key.ResourcePath, StringComparer.Ordinal)
                         .ThenBy(static item => item.Key.SourceLevel)
                         .ThenBy(static item => item.Key.NormalizedLevel))
            {
                var occurrenceText = count == 1 ? "one heading" : $"{count} headings";
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.HeadingLevelNormalized,
                    $"Normalized {occurrenceText} from source level {key.SourceLevel} to level {key.NormalizedLevel} to preserve a valid heading sequence within the XHTML resource.",
                    key.ResourcePath,
                    count));
            }

            foreach (var (resourcePath, count) in noteResourceFallbacks.OrderBy(
                         static item => item.Key,
                         StringComparer.Ordinal))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.NoteResourceFallbackUsed,
                    $"Resolved {count} note reference(s) without a fragment because the target XHTML contained exactly one semantic note without a source ID.",
                    resourcePath,
                    count));
            }

        }

        internal void PrepareIds(IEnumerable<XhtmlModel> xhtmlDocuments)
        {
            foreach (var xhtml in xhtmlDocuments)
            {
                var chapterId = AllocateId($"chapter-{Slug(xhtml.StableIdentity)}", out var chapterCollision);
                if (chapterCollision)
                {
                    ReportIdCollision(xhtml.Item.Path, null, chapterId);
                }

                var body = xhtml.Document.Root?.Element(XhtmlNamespace + "body");
                if (body is null)
                {
                    continue;
                }

                elementIds[body] = chapterId;
                RegisterNodeTypography(body, chapterId);
                anchors.TryAdd(xhtml.Item.Path, chapterId);
                AddSourceLocation(xhtml.Item.Path, null, chapterId);
                var sourceIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var element in body.Descendants())
                {
                    var htmlId = (string?)element.Attribute("id");
                    if (string.IsNullOrWhiteSpace(htmlId))
                    {
                        continue;
                    }

                    if (!IsValidSourceId(htmlId))
                    {
                        diagnostics.Add(Warning(
                            EpubDiagnosticCodes.InvalidSourceId,
                            $"EPUB source ID '{htmlId}' is not a valid XML NCName; it was retained for traceability and link resolution.",
                            xhtml.Item.Path));
                    }

                    if (!sourceIds.Add(htmlId))
                    {
                        diagnostics.Add(Warning(
                            EpubDiagnosticCodes.DuplicateSourceId,
                            $"EPUB source ID '{htmlId}' occurs more than once in the same XHTML resource; the first occurrence remains the primary link target.",
                            xhtml.Item.Path));
                    }

                    var visualContainer = IsImageSourceElement(element)
                        ? element.Ancestors(XhtmlNamespace + "figure").FirstOrDefault()
                          ?? element.Ancestors(SvgNamespace + "svg").FirstOrDefault()
                          ?? FindSingleImageParagraph(element)
                        : null;
                    var targetElement = visualContainer
                        ?? (IsFootnoteElement(element) || IsRepresentedNode(element)
                            ? element
                            : element.Ancestors().FirstOrDefault(IsRepresentedNode) ?? body);
                    if (!elementIds.TryGetValue(targetElement, out var nodeId))
                    {
                        nodeId = AllocateId($"{chapterId.Value}-{Slug(htmlId)}", out var collision);
                        elementIds[targetElement] = nodeId;
                        if (collision)
                        {
                            ReportIdCollision(xhtml.Item.Path, htmlId, nodeId);
                        }
                    }

                    RegisterNodeTypography(targetElement, nodeId);

                    anchors.TryAdd($"{xhtml.Item.Path}#{htmlId}", nodeId);
                    AddSourceLocation(xhtml.Item.Path, htmlId, nodeId);
                    if (!ReferenceEquals(targetElement, element)
                        && !(IsImageSourceElement(element)
                             && targetElement.Name is { } targetName
                             && (targetName == XhtmlNamespace + "figure" || targetName == SvgNamespace + "svg")))
                    {
                        approximatedAnchors[xhtml.Item.Path] = approximatedAnchors.TryGetValue(
                            xhtml.Item.Path,
                            out var anchorCount)
                            ? anchorCount + 1
                            : 1;
                    }
                }
            }
        }

        internal void PrepareFootnotes(IEnumerable<XhtmlModel> xhtmlDocuments)
        {
            var documents = xhtmlDocuments.ToArray();
            var notesByTarget = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
            var notesByResource = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
            var referencesByTarget = new Dictionary<string, List<XElement>>(StringComparer.Ordinal);
            var resourcePaths = new Dictionary<XElement, string>();

            foreach (var xhtml in documents)
            {
                var body = xhtml.Document.Root?.Element(XhtmlNamespace + "body");
                if (body is null)
                {
                    continue;
                }

                foreach (var element in body.DescendantsAndSelf())
                {
                    resourcePaths[element] = xhtml.Item.Path;
                    if (IsFootnoteElement(element))
                    {
                        footnoteElements.Add(element);
                        AddTarget(notesByResource, xhtml.Item.Path, element);
                        if ((string?)element.Attribute("id") is { Length: > 0 } noteId)
                        {
                            AddTarget(notesByTarget, $"{xhtml.Item.Path}#{noteId}", element);
                        }
                        else
                        {
                            _ = IdFor(element, "footnote");
                        }
                    }

                    if (IsFootnoteReferenceElement(element))
                    {
                        var href = (string?)element.Attribute("href");
                        if (!TryBuildReferenceKey(xhtml.Item.Path, href ?? string.Empty, out _, out var key))
                        {
                            ReportOrphanReference(element, href, xhtml.Item.Path);
                            continue;
                        }

                        AddTarget(referencesByTarget, key, element);
                    }
                }
            }

            // Resolve forward and cross-resource references after every note has been catalogued.
            foreach (var (key, references) in referencesByTarget)
            {
                notesByTarget.TryGetValue(key, out var targets);
                foreach (var reference in references)
                {
                    if (footnoteReferenceTargets.ContainsKey(reference) || invalidFootnoteReferences.Contains(reference))
                    {
                        continue;
                    }

                    var resourcePath = resourcePaths[reference];
                    var href = (string?)reference.Attribute("href") ?? string.Empty;
                    if (targets is null)
                    {
                        if (!key.Contains('#')
                            && notesByResource.TryGetValue(key, out var resourceNotes)
                            && resourceNotes.Where(static note => string.IsNullOrWhiteSpace(
                                (string?)note.Attribute("id"))).ToArray() is [var implicitNote])
                        {
                            ResolveFootnoteReference(reference, href, resourcePath, [implicitNote]);
                            noteResourceFallbacks[resourcePath] = noteResourceFallbacks.GetValueOrDefault(resourcePath) + 1;
                        }
                        else
                        {
                            ReportOrphanReference(reference, href, resourcePath);
                        }
                    }
                    else
                    {
                        ResolveFootnoteReference(reference, href, resourcePath, targets);
                    }
                }
            }

            var referencedNoteIds = footnoteReferenceTargets.Values.ToHashSet();
            foreach (var note in footnoteElements)
            {
                if (!elementIds.TryGetValue(note, out var noteId) || !referencedNoteIds.Contains(noteId))
                {
                    var sourceId = (string?)note.Attribute("id") ?? "(without source ID)";
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.UnreferencedNote,
                        $"EPUB note '{sourceId}' has no valid noteref reference.",
                        resourcePaths[note]));
                }
            }

            ValidateFootnoteBacklinks(resourcePaths);
            ValidateFootnoteCycles(resourcePaths);

            static void AddTarget(Dictionary<string, List<XElement>> index, string key, XElement element)
            {
                if (!index.TryGetValue(key, out var values))
                {
                    values = [];
                    index.Add(key, values);
                }

                values.Add(element);
            }
        }

        private void ResolveFootnoteReference(
            XElement reference,
            string href,
            string resourcePath,
            IReadOnlyList<XElement> targets)
        {
            if (targets.Count != 1)
            {
                invalidFootnoteReferences.Add(reference);
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.AmbiguousNoteDestination,
                    $"Footnote reference target '{href}' resolves to {targets.Count} note elements; its label was preserved without an ambiguous link.",
                    resourcePath));
                return;
            }

            if (!elementIds.TryGetValue(targets[0], out var targetId))
            {
                ReportOrphanReference(reference, href, resourcePath);
                return;
            }

            footnoteReferenceTargets[reference] = targetId;
        }

        private void ReportOrphanReference(XElement reference, string? href, string resourcePath)
        {
            invalidFootnoteReferences.Add(reference);
            diagnostics.Add(Warning(
                EpubDiagnosticCodes.OrphanNoteReference,
                $"Footnote reference target '{href ?? "(missing)"}' does not resolve to one imported footnote or endnote; its label was preserved.",
                resourcePath));
        }

        private void ValidateFootnoteBacklinks(
            IReadOnlyDictionary<XElement, string> resourcePaths)
        {
            var referenceSourceKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reference in footnoteReferenceTargets.Keys)
            {
                if ((string?)reference.Attribute("id") is { Length: > 0 } id)
                {
                    referenceSourceKeys.Add($"{resourcePaths[reference]}#{id}");
                }
            }

            foreach (var note in footnoteElements)
            {
                var resourcePath = resourcePaths[note];
                foreach (var backlink in note.Descendants(XhtmlNamespace + "a").Where(IsBacklinkElement))
                {
                    var href = (string?)backlink.Attribute("href");
                    if (!TryBuildReferenceKey(resourcePath, href ?? string.Empty, out _, out var key)
                        || !referenceSourceKeys.Contains(key))
                    {
                        diagnostics.Add(Warning(
                            EpubDiagnosticCodes.MissingNoteBacklink,
                            $"Note backlink target '{href ?? "(missing)"}' does not resolve to a valid noteref source.",
                            resourcePath));
                    }
                }
            }
        }

        private void ValidateFootnoteCycles(IReadOnlyDictionary<XElement, string> resourcePaths)
        {
            var edges = footnoteElements.ToDictionary(static note => note, static _ => new List<XElement>());
            foreach (var (reference, targetId) in footnoteReferenceTargets)
            {
                var owner = reference.Ancestors().FirstOrDefault(footnoteElements.Contains);
                var target = footnoteElements.FirstOrDefault(note => elementIds.TryGetValue(note, out var id) && id == targetId);
                if (owner is not null && target is not null)
                {
                    edges[owner].Add(target);
                }
            }

            var state = new Dictionary<XElement, int>();
            var reported = new HashSet<XElement>();
            foreach (var note in footnoteElements)
            {
                Visit(note);
            }

            void Visit(XElement note)
            {
                if (state.TryGetValue(note, out var current))
                {
                    if (current == 1 && reported.Add(note))
                    {
                        diagnostics.Add(Warning(
                            EpubDiagnosticCodes.CircularNoteReference,
                            "A cycle exists between EPUB notes; references remain linked but renderer navigation may loop.",
                            resourcePaths[note]));
                    }

                    return;
                }

                state[note] = 1;
                foreach (var target in edges[note])
                {
                    Visit(target);
                }

                state[note] = 2;
            }
        }

        private void AddSourceLocation(string resourcePath, string? fragment, NodeId nodeId)
        {
            var key = new SourceLocationKey(resourcePath, fragment);
            var occurrence = sourceOccurrences.TryGetValue(key, out var previous) ? previous + 1 : 1;
            sourceOccurrences[key] = occurrence;
            sourceLocations.Add(new EpubSourceLocation(resourcePath, fragment, nodeId, occurrence));
        }

        private void ReportIdCollision(string resourcePath, string? fragment, NodeId allocatedId) =>
            diagnostics.Add(Warning(
                EpubDiagnosticCodes.SourceIdCollision,
                $"Source location '{FormatSourceLocation(resourcePath, fragment)}' normalized to an already allocated Flow ID; deterministic ID '{allocatedId.Value}' was assigned.",
                resourcePath));

        private static string FormatSourceLocation(string resourcePath, string? fragment) =>
            fragment is null ? resourcePath : $"{resourcePath}#{fragment}";

        private static bool IsValidSourceId(string value)
        {
            try
            {
                XmlConvert.VerifyNCName(value);
                return true;
            }
            catch (XmlException)
            {
                return false;
            }
        }

        internal TableOfContents? ConvertTableOfContents(
            IReadOnlyList<NavigationModel> navigationDocuments,
            string? spineTocId)
        {
            var epub3 = navigationDocuments
                .Where(static item => item.Kind == NavigationKind.Epub3)
                .SelectMany(ParseEpub3Navigation)
                .ToArray();
            var ncx = navigationDocuments
                .Where(static item => item.Kind == NavigationKind.Ncx)
                .OrderBy(item => string.Equals(item.Item.Id, spineTocId, StringComparison.Ordinal) ? 0 : 1)
                .Select(ParseNcxNavigation)
                .Where(static item => item is not null)
                .Cast<ParsedNavigation>()
                .ToArray();

            if (epub3.Length > 1)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.MultipleTableOfContents,
                    $"The publication contains {epub3.Length} EPUB 3 TOC navigation sections; the first one in manifest/document order was selected.",
                    epub3[0].Source.Item.Path));
            }

            if (ncx.Length > 1)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.MultipleTableOfContents,
                    $"The publication contains {ncx.Length} NCX tables of contents; the package spine TOC is preferred, then manifest order.",
                    ncx[0].Source.Item.Path));
            }

            var selected = epub3.FirstOrDefault(static item => !item.Entries.IsEmpty)
                ?? ncx.FirstOrDefault(static item => !item.Entries.IsEmpty);
            if (selected is null)
            {
                return null;
            }

            var comparisonNcx = ncx.FirstOrDefault(static item => !item.Entries.IsEmpty);
            if (epub3.Length > 0 && comparisonNcx is not null
                && !NavigationSignaturesMatch(epub3[0], comparisonNcx))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.TableOfContentsConflict,
                    "EPUB 3 Navigation Document and NCX describe different TOCs; the EPUB 3 Navigation Document takes precedence.",
                    epub3[0].Source.Item.Path));
            }

            if (epub3.Length > 0 && selected.Source.Kind == NavigationKind.Ncx)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableOfContents,
                    "No usable entry was found in the EPUB 3 TOC; the importer used the NCX fallback.",
                    epub3[0].Source.Item.Path));
            }

            var entries = new List<TableOfContentsEntry>();
            foreach (var entry in selected.Entries)
            {
                var label = entry.Label;
                if (!HasVisibleText(label))
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.EmptyTableOfContentsEntry,
                        $"A TOC entry for '{entry.Href}' has an empty label; a deterministic fallback label was used.",
                        selected.Source.Item.Path));
                    label = [new Text(entry.Href)];
                }

                if (!TryResolveNavigationTarget(selected.Source.Item.Path, entry.Href, out var anchor, out var circular))
                {
                    diagnostics.Add(Warning(
                        circular
                            ? EpubDiagnosticCodes.CircularTableOfContentsReference
                            : EpubDiagnosticCodes.MissingTableOfContentsTarget,
                        circular
                            ? $"TOC target '{entry.Href}' refers back to its navigation resource and was ignored."
                            : $"TOC target '{entry.Href}' does not resolve to imported semantic content and was ignored.",
                        selected.Source.Item.Path));
                    continue;
                }

                entries.Add(new TableOfContentsEntry(label, anchor!, entry.Level));
            }

            if (entries.Count == 0)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableOfContents,
                    "The selected TOC contains no entry with a resolvable semantic destination.",
                    selected.Source.Item.Path));
                return null;
            }

            ConsumedResourcePaths.Add(selected.Source.Item.Path);
            var maximumDepth = Math.Max(1, entries.Max(static entry => entry.Level));
            return new TableOfContents(
                AllocateId($"toc-{Slug(selected.Source.Item.Id)}"),
                selected.Title.IsEmpty ? [new Text("Contents")] : selected.Title,
                entries,
                maximumDepth);
        }

        private IEnumerable<ParsedNavigation> ParseEpub3Navigation(NavigationModel source)
        {
            if (source.Document.Root?.Name != XhtmlNamespace + "html")
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableOfContents,
                    "An EPUB 3 Navigation Document is not XHTML and was ignored.",
                    source.Item.Path));
                yield break;
            }

            var tocElements = source.Document
                .Descendants(XhtmlNamespace + "nav")
                .Where(static nav => HasToken((string?)nav.Attribute(EpubNamespace + "type"), "toc"))
                .ToArray();
            if (tocElements.Length == 0)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableOfContents,
                    "The EPUB 3 Navigation Document contains no nav element with epub:type='toc'.",
                    source.Item.Path));
                yield break;
            }

            if (tocElements.Length > 1)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.MultipleTableOfContents,
                    $"The Navigation Document contains {tocElements.Length} nav elements marked as TOC.",
                    source.Item.Path));
            }

            foreach (var toc in tocElements)
            {
                var heading = toc.Elements().FirstOrDefault(static element =>
                    element.Name.Namespace == XhtmlNamespace
                    && element.Name.LocalName is "h1" or "h2" or "h3" or "h4" or "h5" or "h6");
                var title = heading is null
                    ? ImmutableArray<InlineNode>.Empty
                    : ConvertInlineContent(heading, source.Item.Path).ToImmutableArray();
                var list = toc.Elements(XhtmlNamespace + "ol").FirstOrDefault();
                if (list is null)
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.InvalidTableOfContents,
                        "A TOC nav element contains no ordered list.",
                        source.Item.Path));
                    yield return new ParsedNavigation(source, title, []);
                    continue;
                }

                var entries = new List<NavigationEntryModel>();
                ParseEpub3List(list, source, level: 1, [], entries);
                yield return new ParsedNavigation(source, title, entries.ToImmutableArray());
            }
        }

        private void ParseEpub3List(
            XElement list,
            NavigationModel source,
            int level,
            ImmutableHashSet<string> ancestorIds,
            ICollection<NavigationEntryModel> result)
        {
            if (list.Elements(XhtmlNamespace + "ol").Any())
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableOfContentsLevel,
                    "A nested TOC list is not contained by an li entry and cannot define a valid hierarchy.",
                    source.Item.Path));
            }

            var effectiveLevel = NormalizeTocLevel(level, source.Item.Path);
            foreach (var item in list.Elements(XhtmlNamespace + "li"))
            {
                var logicalId = (string?)item.Attribute("id");
                if (!string.IsNullOrEmpty(logicalId) && ancestorIds.Contains(logicalId))
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.CircularTableOfContentsReference,
                        $"Nested TOC entry ID '{logicalId}' repeats an ancestor ID; the circular branch was ignored.",
                        source.Item.Path));
                    continue;
                }

                var anchor = item.Elements()
                    .TakeWhile(static element => element.Name != XhtmlNamespace + "ol")
                    .SelectMany(static element => element.DescendantsAndSelf())
                    .FirstOrDefault(static element => element.Name == XhtmlNamespace + "a");
                var href = (string?)anchor?.Attribute("href");
                if (anchor is null || string.IsNullOrWhiteSpace(href))
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.InvalidTableOfContents,
                        "A TOC li entry has no linked destination; its nested entries are still inspected.",
                        source.Item.Path));
                }
                else
                {
                    result.Add(new NavigationEntryModel(
                        ConvertInlineContent(anchor, source.Item.Path).ToImmutableArray(),
                        href,
                        effectiveLevel,
                        logicalId));
                }

                var nextAncestors = string.IsNullOrEmpty(logicalId) ? ancestorIds : ancestorIds.Add(logicalId);
                foreach (var nestedList in item.Elements(XhtmlNamespace + "ol"))
                {
                    ParseEpub3List(nestedList, source, level + 1, nextAncestors, result);
                }
            }
        }

        private ParsedNavigation? ParseNcxNavigation(NavigationModel source)
        {
            if (source.Document.Root?.Name != NcxNamespace + "ncx")
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableOfContents,
                    "An NCX resource does not contain an NCX root and was ignored.",
                    source.Item.Path));
                return null;
            }

            var titleText = NormalizedText(source.Document.Root
                .Element(NcxNamespace + "docTitle")?
                .Element(NcxNamespace + "text")?.Value ?? string.Empty);
            var title = titleText.Length == 0
                ? ImmutableArray<InlineNode>.Empty
                : ImmutableArray.Create<InlineNode>(new Text(titleText));
            var navMap = source.Document.Root.Element(NcxNamespace + "navMap");
            if (navMap is null)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableOfContents,
                    "The NCX resource contains no navMap.",
                    source.Item.Path));
                return new ParsedNavigation(source, title, []);
            }

            var entries = new List<NavigationEntryModel>();
            foreach (var point in navMap.Elements(NcxNamespace + "navPoint"))
            {
                ParseNcxPoint(point, source, level: 1, [], entries);
            }

            return new ParsedNavigation(source, title, entries.ToImmutableArray());
        }

        private void ParseNcxPoint(
            XElement point,
            NavigationModel source,
            int level,
            ImmutableHashSet<string> ancestorIds,
            ICollection<NavigationEntryModel> result)
        {
            var logicalId = (string?)point.Attribute("id");
            if (!string.IsNullOrEmpty(logicalId) && ancestorIds.Contains(logicalId))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.CircularTableOfContentsReference,
                    $"Nested NCX navPoint ID '{logicalId}' repeats an ancestor ID; the circular branch was ignored.",
                    source.Item.Path));
                return;
            }

            var href = (string?)point.Element(NcxNamespace + "content")?.Attribute("src");
            if (string.IsNullOrWhiteSpace(href))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableOfContents,
                    "An NCX navPoint has no content destination; its nested entries are still inspected.",
                    source.Item.Path));
            }
            else
            {
                var labelText = point.Element(NcxNamespace + "navLabel")?.Element(NcxNamespace + "text")?.Value ?? string.Empty;
                var label = NormalizedText(labelText) is { Length: > 0 } normalized
                    ? ImmutableArray.Create<InlineNode>(new Text(normalized))
                    : ImmutableArray<InlineNode>.Empty;
                result.Add(new NavigationEntryModel(label, href, NormalizeTocLevel(level, source.Item.Path), logicalId));
            }

            var nextAncestors = string.IsNullOrEmpty(logicalId) ? ancestorIds : ancestorIds.Add(logicalId);
            foreach (var child in point.Elements(NcxNamespace + "navPoint"))
            {
                ParseNcxPoint(child, source, level + 1, nextAncestors, result);
            }
        }

        private int NormalizeTocLevel(int level, string resourcePath)
        {
            if (level is >= 1 and <= 6)
            {
                return level;
            }

            diagnostics.Add(Warning(
                EpubDiagnosticCodes.InvalidTableOfContentsLevel,
                $"TOC nesting level {level} is outside Flow's supported range 1..6 and was clamped.",
                resourcePath));
            return Math.Clamp(level, 1, 6);
        }

        private bool TryResolveNavigationTarget(
            string navigationPath,
            string href,
            out DocumentAnchor? anchor,
            out bool circular)
        {
            anchor = null;
            circular = false;
            if (!TryBuildReferenceKey(navigationPath, href, out var targetPath, out var key))
            {
                return false;
            }

            circular = string.Equals(targetPath, navigationPath, StringComparison.Ordinal);
            if (circular || !anchors.TryGetValue(key, out var nodeId))
            {
                return false;
            }

            anchor = DocumentAnchor.Create([nodeId]);
            return true;
        }

        private static bool TryBuildReferenceKey(
            string sourcePath,
            string href,
            out string targetPath,
            out string key)
        {
            targetPath = string.Empty;
            key = string.Empty;
            if (string.IsNullOrWhiteSpace(href) || Uri.TryCreate(href, UriKind.Absolute, out _))
            {
                return false;
            }

            var parts = href.Split('#', 2);
            if (parts[0].Length == 0)
            {
                targetPath = sourcePath;
            }
            else if (!TryNormalizeArchivePath(GetDirectory(sourcePath), parts[0], out targetPath))
            {
                return false;
            }

            if (parts.Length == 1)
            {
                key = targetPath;
                return true;
            }

            try
            {
                var fragment = Uri.UnescapeDataString(parts[1]);
                if (fragment.Length == 0 || fragment.Any(char.IsControl))
                {
                    return false;
                }

                key = $"{targetPath}#{fragment}";
                return true;
            }
            catch (UriFormatException)
            {
                return false;
            }
        }

        private static bool NavigationSignaturesMatch(ParsedNavigation left, ParsedNavigation right)
        {
            if (left.Entries.Length != right.Entries.Length)
            {
                return false;
            }

            for (var index = 0; index < left.Entries.Length; index++)
            {
                var leftEntry = left.Entries[index];
                var rightEntry = right.Entries[index];
                if (leftEntry.Level != rightEntry.Level
                    || !string.Equals(NormalizedText(InlineText(leftEntry.Label)), NormalizedText(InlineText(rightEntry.Label)), StringComparison.Ordinal)
                    || !TryBuildReferenceKey(left.Source.Item.Path, leftEntry.Href, out _, out var leftKey)
                    || !TryBuildReferenceKey(right.Source.Item.Path, rightEntry.Href, out _, out var rightKey)
                    || !string.Equals(leftKey, rightKey, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasVisibleText(IEnumerable<InlineNode> nodes) =>
            !string.IsNullOrWhiteSpace(InlineText(nodes));

        private static string InlineText(IEnumerable<InlineNode> nodes) => string.Concat(nodes.Select(static node => node switch
        {
            Text text => text.Value,
            Strong strong => InlineText(strong.Children),
            Emphasis emphasis => InlineText(emphasis.Children),
            Underline underline => InlineText(underline.Children),
            Strikethrough strikethrough => InlineText(strikethrough.Children),
            InlineCode code => code.Code,
            Link link => InlineText(link.Children),
            FootnoteReference reference => InlineText(reference.Label),
            InlineMath math => math.AlternativeText ?? MathTextValue(math.Root),
            InlineContainerNode container => InlineText(container.Children),
            LineBreak => " ",
            _ => string.Empty,
        }));

        private static string MathTextValue(MathNode node) => node switch
        {
            MathText text => text.Value,
            MathElement element => string.Concat(element.Children.Select(MathTextValue)),
            _ => string.Empty,
        };

        private static bool HasToken(string? values, string token) =>
            values?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Contains(token, StringComparer.Ordinal) == true;

        internal async Task<Chapter> ConvertChapterAsync(XhtmlModel xhtml, CancellationToken cancellationToken)
        {
            var body = xhtml.Document.Root!.Element(XhtmlNamespace + "body")!;
            previousHeadingLevel = null;
            var children = await ConvertChildrenAsync(body, xhtml.Item.Path, cancellationToken).ConfigureAwait(false);

            if (children.Count == 0)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.UnsupportedElement,
                    "The XHTML body produced no supported block content.",
                    xhtml.Item.Path));
            }

            return new Chapter(elementIds[body], children);
        }

        private int NormalizeHeadingLevel(int sourceLevel, string resourcePath)
        {
            var normalizedLevel = previousHeadingLevel is { } previous
                                  && sourceLevel > previous + 1
                ? previous + 1
                : sourceLevel;

            if (normalizedLevel != sourceLevel)
            {
                var key = new HeadingLevelNormalizationKey(resourcePath, sourceLevel, normalizedLevel);
                headingLevelNormalizations[key] = headingLevelNormalizations.GetValueOrDefault(key) + 1;
            }

            previousHeadingLevel = normalizedLevel;
            return normalizedLevel;
        }

        private async Task<IEnumerable<DocumentNode>> ConvertBlockAsync(
            XElement element,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            if (element.Name == MathMlNamespace + "math")
            {
                return [ConvertBlockMath(element, resourcePath)];
            }

            if (element.Name == SvgNamespace + "svg")
            {
                return await ConvertSvgImageAsync(
                    element,
                    element,
                    caption: null,
                    fallbackImage: null,
                    resourcePath,
                    cancellationToken).ConfigureAwait(false);
            }

            if (element.Name == SvgNamespace + "image")
            {
                return await ConvertSvgImageElementsAsync(
                    [element],
                    element,
                    svg: null,
                    caption: null,
                    fallbackImage: null,
                    resourcePath,
                    cancellationToken).ConfigureAwait(false);
            }

            if (element.Name.Namespace != XhtmlNamespace)
            {
                ReportUnsupported(element, resourcePath, $"Foreign element <{element.Name.LocalName}>");
                return await ConvertChildrenAsync(element, resourcePath, cancellationToken).ConfigureAwait(false);
            }

            if (footnoteElements.Contains(element))
            {
                return [new Footnote(
                    IdFor(element, "footnote"),
                    await ConvertChildrenAsync(element, resourcePath, cancellationToken).ConfigureAwait(false))];
            }

            var name = element.Name.LocalName.ToLowerInvariant();
            switch (name)
            {
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    var sourceLevel = name[1] - '0';
                    var normalizedLevel = NormalizeHeadingLevel(sourceLevel, resourcePath);
                    return [new Heading(IdFor(element, "heading"), normalizedLevel, ConvertInlineContent(element, resourcePath))];
                case "p":
                    return await ConvertParagraphAsync(element, resourcePath, cancellationToken).ConfigureAwait(false);
                case "ol":
                    return [await ConvertOrderedListAsync(element, resourcePath, cancellationToken).ConfigureAwait(false)];
                case "ul":
                    return [await ConvertUnorderedListAsync(element, resourcePath, cancellationToken).ConfigureAwait(false)];
                case "blockquote":
                    return [new BlockQuote(
                        IdFor(element, "blockquote"),
                        await ConvertChildrenAsync(element, resourcePath, cancellationToken).ConfigureAwait(false))];
                case "table":
                    return [await ConvertTableAsync(element, resourcePath, cancellationToken).ConfigureAwait(false)];
                case "figure":
                    return await ConvertFigureAsync(element, resourcePath, cancellationToken).ConfigureAwait(false);
                case "img":
                    return await ConvertImageAsync(element, element, null, resourcePath, cancellationToken).ConfigureAwait(false);
                case "picture":
                    return await ConvertImageAsync(element, element, null, resourcePath, cancellationToken).ConfigureAwait(false);
                case "section":
                    return [new Section(
                        IdFor(element, name),
                        await ConvertChildrenAsync(element, resourcePath, cancellationToken).ConfigureAwait(false))];
                case "article":
                    ReportUnsupported(element, resourcePath, "Element <article> mapped to a Flow Section");
                    return [new Section(
                        IdFor(element, name),
                        await ConvertChildrenAsync(element, resourcePath, cancellationToken).ConfigureAwait(false))];
                case "main":
                case "header":
                case "footer":
                case "aside":
                case "address":
                case "details":
                case "summary":
                case "dl":
                case "dt":
                case "dd":
                    ReportUnsupported(element, resourcePath);
                    return await ConvertChildrenAsync(element, resourcePath, cancellationToken).ConfigureAwait(false);
                case "div":
                    ReportUnsupported(element, resourcePath, "Generic container <div>");
                    return await ConvertChildrenAsync(element, resourcePath, cancellationToken).ConfigureAwait(false);
                case "pre":
                    return [new CodeBlock(IdFor(element, "code"), element.Value)];
                case "hr":
                    return [new HorizontalRule(IdFor(element, "rule"))];
                case "script":
                case "style":
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.UnsupportedElement,
                        $"Executable or presentation element <{name}> was intentionally not imported.",
                        resourcePath));
                    return [];
                default:
                    ReportUnsupported(element, resourcePath);
                    return await ConvertChildrenAsync(element, resourcePath, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task<IReadOnlyList<DocumentNode>> ConvertChildrenAsync(
            XElement parent,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var result = new List<DocumentNode>();
            var inlineBuffer = new List<XNode>();

            void FlushInlineBuffer()
            {
                if (inlineBuffer.Count == 0)
                {
                    return;
                }

                var inline = ApplyInternationalization(
                    parent,
                    ConvertInline(inlineBuffer, resourcePath),
                    resourcePath,
                    inherit: true);
                if (HasVisibleText(inline))
                {
                    result.Add(new Paragraph(GeneratedIdFor(parent, "container-text"), inline));
                }

                inlineBuffer.Clear();
            }

            foreach (var child in parent.Nodes())
            {
                if (child is XText || child is XElement inlineElement && IsInlineElement(inlineElement))
                {
                    inlineBuffer.Add(child);
                    continue;
                }

                FlushInlineBuffer();
                if (child is XElement blockElement)
                {
                    result.AddRange(await ConvertBlockAsync(blockElement, resourcePath, cancellationToken)
                        .ConfigureAwait(false));
                }
            }

            FlushInlineBuffer();
            return result;
        }

        private async Task<IReadOnlyList<DocumentNode>> ConvertParagraphAsync(
            XElement paragraph,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var parts = SplitInlineContent(paragraph.Nodes());
            if (parts.All(static part => part.Image is null))
            {
                return [new Paragraph(IdFor(paragraph, "paragraph"), ConvertInlineContent(paragraph, resourcePath))];
            }

            var converted = new List<ConvertedInlinePart>(parts.Count);
            foreach (var part in parts)
            {
                if (part.Image is not null)
                {
                    converted.Add(new ConvertedInlinePart([], part.Image));
                    continue;
                }

                var inline = ApplyInternationalization(
                    paragraph,
                    ConvertInline(part.Nodes, resourcePath),
                    resourcePath,
                    inherit: true);
                if (HasVisibleText(inline))
                {
                    converted.Add(new ConvertedInlinePart(inline, null));
                }
            }

            var imageCount = converted.Count(static part => part.Image is not null);
            var hasText = converted.Any(static part => !part.Inline.IsEmpty);
            var result = new List<DocumentNode>(converted.Count);
            var paragraphIdUsed = false;

            foreach (var part in converted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!part.Inline.IsEmpty)
                {
                    var id = paragraphIdUsed
                        ? GeneratedIdFor(paragraph, "paragraph-continuation")
                        : IdFor(paragraph, "paragraph");
                    result.Add(new Paragraph(id, part.Inline));
                    paragraphIdUsed = true;
                    continue;
                }

                var image = part.Image!;
                ReportUnsupported(
                    image,
                    resourcePath,
                    "Inline image promoted to an ordered Flow Figure between paragraph text segments");
                var idSource = !hasText && imageCount == 1 ? paragraph : image;
                result.AddRange(await ConvertInlineImageAsync(
                    image,
                    idSource,
                    resourcePath,
                    cancellationToken).ConfigureAwait(false));
            }

            return result;
        }

        private async Task<IEnumerable<DocumentNode>> ConvertInlineImageAsync(
            XElement image,
            XElement idSource,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            if (image.Name == SvgNamespace + "svg")
            {
                return await ConvertSvgImageAsync(
                    image,
                    idSource,
                    caption: null,
                    fallbackImage: null,
                    resourcePath,
                    cancellationToken).ConfigureAwait(false);
            }

            if (image.Name == SvgNamespace + "image")
            {
                return await ConvertSvgImageElementsAsync(
                    [image],
                    idSource,
                    svg: null,
                    caption: null,
                    fallbackImage: null,
                    resourcePath,
                    cancellationToken).ConfigureAwait(false);
            }

            return await ConvertImageAsync(
                image,
                idSource,
                caption: null,
                resourcePath,
                cancellationToken).ConfigureAwait(false);
        }

        private static IReadOnlyList<InlineContentPart> SplitInlineContent(IEnumerable<XNode> nodes)
        {
            var result = new List<InlineContentPart>();
            var buffer = new List<XNode>();

            void Flush()
            {
                if (buffer.Count == 0)
                {
                    return;
                }

                result.Add(new InlineContentPart(buffer.ToArray(), null));
                buffer.Clear();
            }

            foreach (var node in nodes)
            {
                if (node is not XElement element)
                {
                    buffer.Add(node);
                    continue;
                }

                if (IsExtractableInlineImage(element))
                {
                    Flush();
                    result.Add(new InlineContentPart([], element));
                    continue;
                }

                if (!element.Descendants().Any(IsExtractableInlineImage))
                {
                    buffer.Add(element);
                    continue;
                }

                foreach (var nested in SplitInlineContent(element.Nodes()))
                {
                    if (nested.Image is not null)
                    {
                        Flush();
                        result.Add(nested);
                        continue;
                    }

                    if (nested.Nodes.Count > 0)
                    {
                        buffer.Add(new XElement(element.Name, element.Attributes(), nested.Nodes));
                    }
                }
            }

            Flush();
            return result;
        }

        private async Task<OrderedList> ConvertOrderedListAsync(
            XElement element,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var items = await ConvertListItemsAsync(element, resourcePath, cancellationToken).ConfigureAwait(false);
            var start = int.TryParse((string?)element.Attribute("start"), out var parsed) && parsed > 0 ? parsed : 1;
            return new OrderedList(IdFor(element, "ordered-list"), items, start);
        }

        private async Task<UnorderedList> ConvertUnorderedListAsync(
            XElement element,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var items = await ConvertListItemsAsync(element, resourcePath, cancellationToken).ConfigureAwait(false);
            return new UnorderedList(IdFor(element, "unordered-list"), items);
        }

        private async Task<Table> ConvertTableAsync(
            XElement table,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var headerIds = table
                .Descendants(XhtmlNamespace + "th")
                .Where(elementIds.ContainsKey)
                .Select(element => elementIds[element])
                .ToHashSet();
            var captions = table.Elements(XhtmlNamespace + "caption").ToArray();
            TableCaption? caption = null;
            if (captions.Length > 0)
            {
                caption = new TableCaption(
                    IdFor(captions[0], "table-caption"),
                    await ConvertChildrenAsync(captions[0], resourcePath, cancellationToken).ConfigureAwait(false));
            }

            if (captions.Length > 1)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableStructure,
                    $"Table contains {captions.Length} captions; the first remains the semantic caption and additional caption content is preserved in recovery rows.",
                    resourcePath));
            }

            var headElements = table.Elements(XhtmlNamespace + "thead").ToArray();
            var headRows = new List<TableRow>();
            foreach (var headElement in headElements)
            {
                headRows.AddRange(await ConvertRowsAsync(
                    headElement,
                    resourcePath,
                    headerIds,
                    cancellationToken).ConfigureAwait(false));
            }

            if (headElements.Length > 1)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableStructure,
                    $"Table contains {headElements.Length} thead groups; their rows were merged in source order.",
                    resourcePath));
            }

            var head = headElements.Length == 0
                ? null
                : new TableHead(IdFor(headElements[0], "table-head"), headRows);

            var footElements = table.Elements(XhtmlNamespace + "tfoot").ToArray();
            var footRows = new List<TableRow>();
            foreach (var footElement in footElements)
            {
                footRows.AddRange(await ConvertRowsAsync(
                    footElement,
                    resourcePath,
                    headerIds,
                    cancellationToken).ConfigureAwait(false));
            }

            if (footElements.Length > 1)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableStructure,
                    $"Table contains {footElements.Length} tfoot groups; their rows were merged in source order.",
                    resourcePath));
            }

            var foot = footElements.Length == 0
                ? null
                : new TableFoot(IdFor(footElements[0], "table-foot"), footRows);

            var bodies = new List<TableBody>();
            var implicitRows = new List<TableRow>();
            var orphanCells = new List<XElement>();

            async Task FlushOrphanCellsAsync()
            {
                if (orphanCells.Count == 0)
                {
                    return;
                }

                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableStructure,
                    $"{orphanCells.Count} table cells occurred outside a tr; they were preserved in one recovery row.",
                    resourcePath));
                var cells = new List<TableCellNode>();
                foreach (var cell in orphanCells)
                {
                    cells.Add(await ConvertTableCellAsync(
                        cell,
                        resourcePath,
                        headerIds,
                        cancellationToken).ConfigureAwait(false));
                }

                implicitRows.Add(new TableRow(GeneratedIdFor(table, "table-recovery-row"), cells));
                orphanCells.Clear();
            }

            void FlushImplicitBody()
            {
                if (implicitRows.Count == 0)
                {
                    return;
                }

                bodies.Add(new TableBody(GeneratedIdFor(table, "table-body"), implicitRows.ToArray()));
                implicitRows.Clear();
            }

            foreach (var node in table.Nodes())
            {
                if (node is XElement structural
                    && structural.Name.Namespace == XhtmlNamespace
                    && structural.Name.LocalName is "caption" or "thead" or "tfoot")
                {
                    continue;
                }

                if (node is XElement bodyElement && bodyElement.Name == XhtmlNamespace + "tbody")
                {
                    await FlushOrphanCellsAsync().ConfigureAwait(false);
                    FlushImplicitBody();
                    bodies.Add(new TableBody(
                        IdFor(bodyElement, "table-body"),
                        await ConvertRowsAsync(
                            bodyElement,
                            resourcePath,
                            headerIds,
                            cancellationToken).ConfigureAwait(false)));
                    continue;
                }

                if (node is XElement rowElement && rowElement.Name == XhtmlNamespace + "tr")
                {
                    await FlushOrphanCellsAsync().ConfigureAwait(false);
                    implicitRows.Add(await ConvertTableRowAsync(
                        rowElement,
                        resourcePath,
                        headerIds,
                        cancellationToken).ConfigureAwait(false));
                    continue;
                }

                if (node is XElement cellElement
                    && cellElement.Name.Namespace == XhtmlNamespace
                    && cellElement.Name.LocalName is "th" or "td")
                {
                    orphanCells.Add(cellElement);
                    continue;
                }

                if (node is XText text && string.IsNullOrWhiteSpace(text.Value))
                {
                    continue;
                }

                await FlushOrphanCellsAsync().ConfigureAwait(false);
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableStructure,
                    $"Unexpected {DescribeTableNode(node)} occurred directly inside table; recoverable content was preserved in a generated cell.",
                    resourcePath));
                var recovery = await ConvertRecoveryTableCellAsync(
                    node,
                    table,
                    resourcePath,
                    cancellationToken).ConfigureAwait(false);
                if (recovery is not null)
                {
                    implicitRows.Add(new TableRow(GeneratedIdFor(table, "table-recovery-row"), [recovery]));
                }
            }

            await FlushOrphanCellsAsync().ConfigureAwait(false);
            FlushImplicitBody();

            if (captions.Length > 1)
            {
                var recoveryRows = new List<TableRow>();
                foreach (var extraCaption in captions.Skip(1))
                {
                    recoveryRows.Add(new TableRow(
                        GeneratedIdFor(extraCaption, "table-recovery-row"),
                        [new TableCell(
                            GeneratedIdFor(extraCaption, "table-recovery-cell"),
                            await ConvertChildrenAsync(extraCaption, resourcePath, cancellationToken).ConfigureAwait(false))]));
                }

                bodies.Insert(0, new TableBody(GeneratedIdFor(table, "table-caption-recovery"), recoveryRows));
            }

            return new Table(IdFor(table, "table"), bodies, caption, head, foot);
        }

        private async Task<IReadOnlyList<TableRow>> ConvertRowsAsync(
            XElement group,
            string resourcePath,
            IReadOnlySet<NodeId> headerIds,
            CancellationToken cancellationToken)
        {
            var rows = new List<TableRow>();
            foreach (var node in group.Nodes())
            {
                if (node is XElement row && row.Name == XhtmlNamespace + "tr")
                {
                    rows.Add(await ConvertTableRowAsync(
                        row,
                        resourcePath,
                        headerIds,
                        cancellationToken).ConfigureAwait(false));
                }
                else if (node is not XText text || !string.IsNullOrWhiteSpace(text.Value))
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.InvalidTableStructure,
                        $"Unexpected {DescribeTableNode(node)} occurred inside {group.Name.LocalName}; recoverable content was preserved in a generated row and cell.",
                        resourcePath));
                    var recovery = await ConvertRecoveryTableCellAsync(
                        node,
                        group,
                        resourcePath,
                        cancellationToken).ConfigureAwait(false);
                    if (recovery is not null)
                    {
                        rows.Add(new TableRow(GeneratedIdFor(group, "table-recovery-row"), [recovery]));
                    }
                }
            }

            return rows;
        }

        private async Task<TableRow> ConvertTableRowAsync(
            XElement row,
            string resourcePath,
            IReadOnlySet<NodeId> headerIds,
            CancellationToken cancellationToken)
        {
            var cells = new List<TableCellNode>();
            foreach (var node in row.Nodes())
            {
                if (node is XElement cell
                    && cell.Name.Namespace == XhtmlNamespace
                    && cell.Name.LocalName is "th" or "td")
                {
                    cells.Add(await ConvertTableCellAsync(
                        cell,
                        resourcePath,
                        headerIds,
                        cancellationToken).ConfigureAwait(false));
                    continue;
                }

                if (node is XText text && string.IsNullOrWhiteSpace(text.Value))
                {
                    continue;
                }

                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableStructure,
                    $"Unexpected {DescribeTableNode(node)} occurred inside tr; recoverable content was preserved in a generated cell.",
                    resourcePath));
                var nestedCells = node is XElement wrapper
                    ? wrapper.Descendants().Where(element =>
                        element.Name.Namespace == XhtmlNamespace
                        && element.Name.LocalName is "th" or "td"
                        && !element.Ancestors().TakeWhile(ancestor => ancestor != row)
                            .Any(ancestor => ancestor.Name == XhtmlNamespace + "table"))
                        .ToArray()
                    : [];
                if (nestedCells.Length > 0)
                {
                    foreach (var nestedCell in nestedCells)
                    {
                        cells.Add(await ConvertTableCellAsync(
                            nestedCell,
                            resourcePath,
                            headerIds,
                            cancellationToken).ConfigureAwait(false));
                    }
                }
                else
                {
                    var recovery = await ConvertRecoveryTableCellAsync(
                        node,
                        row,
                        resourcePath,
                        cancellationToken).ConfigureAwait(false);
                    if (recovery is not null)
                    {
                        cells.Add(recovery);
                    }
                }
            }

            return new TableRow(IdFor(row, "table-row"), cells);
        }

        private async Task<TableCellNode> ConvertTableCellAsync(
            XElement cell,
            string resourcePath,
            IReadOnlySet<NodeId> headerIds,
            CancellationToken cancellationToken)
        {
            var columnSpan = ParseTableSpan(cell, "colspan", resourcePath);
            var rowSpan = ParseTableSpan(cell, "rowspan", resourcePath);
            var headers = ParseTableHeaders(cell, resourcePath, headerIds);
            var children = await ConvertChildrenAsync(cell, resourcePath, cancellationToken).ConfigureAwait(false);
            if (cell.Name.LocalName == "th")
            {
                return new TableHeaderCell(
                    IdFor(cell, "table-header-cell"),
                    children,
                    columnSpan,
                    rowSpan,
                    ParseTableScope(cell, resourcePath),
                    headers);
            }

            if (cell.Attribute("scope") is not null)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableScope,
                    "A td element declares scope, which applies only to header cells; the attribute was ignored.",
                    resourcePath));
            }

            return new TableCell(IdFor(cell, "table-cell"), children, columnSpan, rowSpan, headers);
        }

        private int ParseTableSpan(XElement cell, string attributeName, string resourcePath)
        {
            var raw = (string?)cell.Attribute(attributeName);
            if (raw is null)
            {
                return 1;
            }

            if (int.TryParse(raw, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value)
                && value > 0)
            {
                return value;
            }

            diagnostics.Add(Warning(
                EpubDiagnosticCodes.InvalidTableSpan,
                $"Table cell {attributeName} value '{raw}' is not a positive integer; span 1 was used.",
                resourcePath));
            return 1;
        }

        private IReadOnlyList<NodeId> ParseTableHeaders(
            XElement cell,
            string resourcePath,
            IReadOnlySet<NodeId> headerIds)
        {
            var result = new List<NodeId>();
            var seen = new HashSet<NodeId>();
            foreach (var token in ((string?)cell.Attribute("headers") ?? string.Empty)
                         .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (TryBuildReferenceKey(resourcePath, $"#{token}", out _, out var key)
                    && anchors.TryGetValue(key, out var nodeId)
                    && headerIds.Contains(nodeId))
                {
                    if (seen.Add(nodeId))
                    {
                        result.Add(nodeId);
                    }
                    else
                    {
                        diagnostics.Add(Warning(
                            EpubDiagnosticCodes.InvalidTableStructure,
                            $"Table cell headers token '{token}' is duplicated; one deterministic reference was retained.",
                            resourcePath));
                    }

                    continue;
                }

                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.MissingTableHeader,
                    $"Table cell headers token '{token}' does not resolve to one header cell in the same table and was omitted.",
                    resourcePath));
            }

            return result;
        }

        private TableHeaderScope? ParseTableScope(XElement cell, string resourcePath)
        {
            var raw = (string?)cell.Attribute("scope");
            if (raw is null)
            {
                return null;
            }

            var scope = raw.ToLowerInvariant() switch
            {
                "row" => TableHeaderScope.Row,
                "col" => TableHeaderScope.Column,
                "rowgroup" => TableHeaderScope.RowGroup,
                "colgroup" => TableHeaderScope.ColumnGroup,
                _ => (TableHeaderScope?)null,
            };
            if (scope is null)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTableScope,
                    $"Table header scope '{raw}' is unsupported; the header remains without an explicit scope.",
                    resourcePath));
            }

            return scope;
        }

        private async Task<TableCell?> ConvertRecoveryTableCellAsync(
            XNode node,
            XElement styleSource,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<DocumentNode> children;
            if (node is XText text)
            {
                var inline = ApplyInternationalization(
                    styleSource,
                    ConvertInline([text], resourcePath),
                    resourcePath,
                    inherit: true);
                children = inline.Count == 0
                    ? []
                    : [new Paragraph(GeneratedIdFor(styleSource, "table-recovery-text"), inline)];
            }
            else if (node is XElement element)
            {
                children = (await ConvertBlockAsync(element, resourcePath, cancellationToken).ConfigureAwait(false)).ToArray();
            }
            else
            {
                children = [];
            }

            return children.Count == 0
                ? null
                : new TableCell(GeneratedIdFor(styleSource, "table-recovery-cell"), children);
        }

        private static string DescribeTableNode(XNode node) => node switch
        {
            XElement element => $"element <{element.Name.LocalName}>",
            XText => "text",
            _ => "node",
        };

        private async Task<IReadOnlyList<ListItem>> ConvertListItemsAsync(
            XElement list,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var result = new List<ListItem>();
            foreach (var item in list.Elements(XhtmlNamespace + "li"))
            {
                if (footnoteElements.Contains(item))
                {
                    var note = new Footnote(
                        IdFor(item, "footnote"),
                        await ConvertChildrenAsync(item, resourcePath, cancellationToken).ConfigureAwait(false));
                    result.Add(new ListItem(GeneratedIdFor(item, "list-item"), [note]));
                    continue;
                }

                var blockChildren = new List<DocumentNode>();
                var inlineNodes = item.Nodes().TakeWhile(static node => node is not XElement element
                    || element.Name.LocalName is not ("ol" or "ul" or "p")).ToArray();
                if (NormalizedText(string.Concat(inlineNodes.Select(NodeText))).Length > 0)
                {
                    blockChildren.Add(new Paragraph(
                        GeneratedIdFor(item, "list-text"),
                        ApplyInternationalization(
                            item,
                            ConvertInline(inlineNodes, resourcePath),
                            resourcePath,
                            inherit: true)));
                }

                foreach (var child in item.Elements().Where(static child =>
                             child.Name.LocalName is "p" or "ol" or "ul" or "blockquote"))
                {
                    blockChildren.AddRange(await ConvertBlockAsync(child, resourcePath, cancellationToken).ConfigureAwait(false));
                }

                result.Add(new ListItem(IdFor(item, "list-item"), blockChildren));
            }

            return result;
        }

        private async Task<IEnumerable<DocumentNode>> ConvertFigureAsync(
            XElement element,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var svg = element.Descendants(SvgNamespace + "svg").FirstOrDefault();
            var image = element.Element(XhtmlNamespace + "picture")
                ?? element.DescendantsAndSelf(XhtmlNamespace + "img").FirstOrDefault();
            var captionElement = element.Element(XhtmlNamespace + "figcaption");
            if (image is null && svg is null)
            {
                ReportUnsupported(element, resourcePath, "Figure without an image");
                return await ConvertChildrenAsync(element, resourcePath, cancellationToken).ConfigureAwait(false);
            }

            Caption? caption = null;
            if (captionElement is not null)
            {
                caption = new Caption(IdFor(captionElement, "caption"), ConvertInlineContent(captionElement, resourcePath));
            }

            if (svg is not null)
            {
                return await ConvertSvgImageAsync(
                    svg,
                    element,
                    caption,
                    image,
                    resourcePath,
                    cancellationToken).ConfigureAwait(false);
            }

            return await ConvertImageAsync(image!, element, caption, resourcePath, cancellationToken).ConfigureAwait(false);
        }

        private async Task<IEnumerable<DocumentNode>> ConvertSvgImageAsync(
            XElement svg,
            XElement idSource,
            Caption? caption,
            XElement? fallbackImage,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            ReportUnsafeEmbeddedSvg(svg, resourcePath);
            var images = svg.Descendants(SvgNamespace + "image").ToArray();
            if (images.Length == 0)
            {
                ReportSvgImageIssue(
                    EpubDiagnosticCodes.InvalidSvgImageReference,
                    "Embedded SVG has no image reference that can become a Flow figure",
                    resourcePath);
                if (fallbackImage is not null)
                {
                    ReportSvgImageIssue(
                        EpubDiagnosticCodes.SvgImageFallbackUsed,
                        "Embedded SVG without a usable image used its XHTML img fallback",
                        resourcePath);
                    return await ConvertImageAsync(
                        fallbackImage,
                        idSource,
                        caption,
                        resourcePath,
                        cancellationToken).ConfigureAwait(false);
                }

                return AlternativeTextFallback(ReadSvgAlternativeText(svg, null, coverTitle: null), idSource);
            }

            if (images.Length > 1)
            {
                ReportSvgImageIssue(
                    EpubDiagnosticCodes.SvgImageSemanticLoss,
                    $"Embedded SVG contains {images.Length} image elements; they were preserved as ordered Flow figures without SVG composition",
                    resourcePath);
            }

            return await ConvertSvgImageElementsAsync(
                images,
                idSource,
                svg,
                caption,
                fallbackImage,
                resourcePath,
                cancellationToken).ConfigureAwait(false);
        }

        private async Task<IEnumerable<DocumentNode>> ConvertSvgImageElementsAsync(
            IReadOnlyList<XElement> images,
            XElement idSource,
            XElement? svg,
            Caption? caption,
            XElement? fallbackImage,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var result = new List<DocumentNode>();
            for (var index = 0; index < images.Count; index++)
            {
                var image = images[index];
                var href = (string?)image.Attribute("href");
                var legacyHref = (string?)image.Attribute(XLinkNamespace + "href");
                if (!string.IsNullOrWhiteSpace(href)
                    && !string.IsNullOrWhiteSpace(legacyHref)
                    && !string.Equals(href, legacyHref, StringComparison.Ordinal))
                {
                    ReportSvgImageIssue(
                        EpubDiagnosticCodes.InvalidSvgImageReference,
                        $"SVG image declares conflicting href '{href}' and xlink:href '{legacyHref}'; href takes precedence",
                        resourcePath);
                }

                var reference = !string.IsNullOrWhiteSpace(href) ? href : legacyHref;
                if (string.IsNullOrWhiteSpace(reference))
                {
                    ReportSvgImageIssue(
                        EpubDiagnosticCodes.InvalidSvgImageReference,
                        "SVG image has neither href nor xlink:href",
                        resourcePath);
                    continue;
                }

                if (!TryNormalizeArchivePath(GetDirectory(resourcePath), reference, out var imagePath))
                {
                    ReportSvgImageIssue(
                        EpubDiagnosticCodes.InvalidSvgImageReference,
                        $"SVG image reference '{reference}' is external, missing, or unsafe and was not loaded",
                        resourcePath);
                    continue;
                }

                var imported = await TryImportImageAssetAsync(
                    imagePath,
                    $"SVG image in '{resourcePath}'",
                    cancellationToken).ConfigureAwait(false);
                if (imported is null)
                {
                    ReportSvgImageIssue(
                        EpubDiagnosticCodes.InvalidSvgImageReference,
                        $"SVG image reference '{reference}' did not produce a safe local asset",
                        resourcePath);
                    continue;
                }

                var figureSource = index == 0 ? idSource : image;
                var alternativeText = ReadSvgAlternativeText(
                    image,
                    fallbackImage,
                    IsPublicationCover(imported.AssetId) ? publicationTitle : null);
                if (alternativeText is null)
                {
                    ReportSvgImageIssue(
                        EpubDiagnosticCodes.MissingImageAlternativeText,
                        "SVG image has no title, description, alt text, XHTML fallback, or cover title",
                        resourcePath);
                }

                result.Add(CreateFigure(
                    figureSource,
                    imported.AssetId,
                    index == 0 ? caption : null,
                    alternativeText,
                    image,
                    resourcePath));
            }

            if (result.Count > 0)
            {
                return result;
            }

            if (fallbackImage is not null)
            {
                ReportSvgImageIssue(
                    EpubDiagnosticCodes.SvgImageFallbackUsed,
                    "Embedded SVG did not produce a safe asset and used its XHTML img fallback",
                    resourcePath);
                return await ConvertImageAsync(
                    fallbackImage,
                    idSource,
                    caption,
                    resourcePath,
                    cancellationToken).ConfigureAwait(false);
            }

            return AlternativeTextFallback(
                ReadSvgAlternativeText(
                    svg ?? images.FirstOrDefault(),
                    null,
                    ReferencesPublicationCover(images, resourcePath) ? publicationTitle : null),
                idSource);
        }

        private bool ReferencesPublicationCover(IEnumerable<XElement> images, string resourcePath)
        {
            if (coverMetadata is null)
            {
                return false;
            }

            foreach (var image in images)
            {
                var reference = (string?)image.Attribute("href")
                    ?? (string?)image.Attribute(XLinkNamespace + "href");
                if (!string.IsNullOrWhiteSpace(reference)
                    && TryNormalizeArchivePath(GetDirectory(resourcePath), reference, out var path)
                    && string.Equals(path, coverMetadata.Path, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<IEnumerable<DocumentNode>> ConvertImageAsync(
            XElement imageSource,
            XElement idSource,
            Caption? caption,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var fallbackImage = imageSource.Name == XhtmlNamespace + "picture"
                ? imageSource.Elements(XhtmlNamespace + "img").LastOrDefault()
                : imageSource;
            var alternativeText = (string?)fallbackImage?.Attribute("alt");
            if (fallbackImage is null || fallbackImage.Attribute("alt") is null)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.MissingImageAlternativeText,
                    "An image has no alt attribute; no textual alternative can be preserved.",
                    resourcePath));
            }

            foreach (var reference in ReadImageReferences(imageSource, fallbackImage, resourcePath))
            {
                if (!TryNormalizeArchivePath(GetDirectory(resourcePath), reference, out var imagePath))
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.InvalidReference,
                        $"Image source '{reference}' is external, missing, or unsafe and was not loaded.",
                        resourcePath));
                    continue;
                }

                var imported = await TryImportImageAssetAsync(imagePath, $"image in '{resourcePath}'", cancellationToken)
                    .ConfigureAwait(false);
                if (imported is not null)
                {
                    return [CreateFigure(idSource, imported.AssetId, caption, alternativeText, fallbackImage!, resourcePath)];
                }
            }

            return AlternativeTextFallback(alternativeText, idSource);
        }

        private Figure CreateFigure(
            XElement idSource,
            AssetId assetId,
            Caption? caption,
            string? alternativeText,
            XElement linkSource,
            string resourcePath)
        {
            var figure = new Figure(
                IdFor(idSource, "figure"),
                assetId,
                caption,
                alternativeText,
                TryCreateFigureLink(linkSource, resourcePath));
            if (CoverFigureId is null && IsPublicationCover(assetId))
            {
                CoverFigureId = figure.Id;
            }

            return figure;
        }

        private FigureLink? TryCreateFigureLink(XElement source, string resourcePath)
        {
            var anchorsAroundImage = source.Ancestors(XhtmlNamespace + "a").ToArray();
            if (anchorsAroundImage.Length == 0)
            {
                return null;
            }

            if (anchorsAroundImage.Length > 1)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidReference,
                    "An image is nested in multiple links; the ambiguous destinations were rejected.",
                    resourcePath));
                return null;
            }

            var href = (string?)anchorsAroundImage[0].Attribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidReference,
                    "An image link has no href; the image was preserved without a destination.",
                    resourcePath));
                return null;
            }

            if (FigureLink.TryCreateExternal(href, out var external))
            {
                return external;
            }

            if (Uri.TryCreate(href, UriKind.Absolute, out _))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidReference,
                    $"Image link target '{href}' uses an unsafe or unsupported external scheme; the image was preserved without a destination.",
                    resourcePath));
                return null;
            }

            if (TryBuildReferenceKey(resourcePath, href, out _, out var key)
                && anchors.TryGetValue(key, out var nodeId))
            {
                return FigureLink.Internal(DocumentAnchor.Create([nodeId]));
            }

            diagnostics.Add(Warning(
                EpubDiagnosticCodes.InvalidReference,
                $"Image link target '{href}' could not be resolved; the image was preserved without a destination.",
                resourcePath));
            return null;
        }

        private bool IsPublicationCover(AssetId assetId) =>
            coverMetadata is not null && coverAssetId is not null && coverAssetId == assetId;

        private static string? ReadSvgAlternativeText(
            XElement? svgOrImage,
            XElement? fallbackImage,
            string? coverTitle)
        {
            if (svgOrImage is null)
            {
                return NormalizeAlternative(coverTitle);
            }

            var svg = svgOrImage.Name == SvgNamespace + "svg"
                ? svgOrImage
                : svgOrImage.Ancestors(SvgNamespace + "svg").FirstOrDefault();
            var image = svgOrImage.Name == SvgNamespace + "image"
                ? svgOrImage
                : svg?.Descendants(SvgNamespace + "image").FirstOrDefault();
            var candidates = new[]
            {
                (string?)image?.Attribute("alt"),
                (string?)image?.Attribute("aria-label"),
                (string?)svg?.Attribute("aria-label"),
                svg?.Element(SvgNamespace + "title")?.Value,
                svg?.Element(SvgNamespace + "desc")?.Value,
                (string?)fallbackImage?.Attribute("alt"),
                string.Concat(svg?.Descendants()
                    .Where(static element => element.Name.Namespace == XhtmlNamespace
                        && element.Name.LocalName is not ("script" or "style")
                        && !element.Ancestors().Any(static ancestor => ancestor.Name.Namespace == XhtmlNamespace))
                    .Select(static item => item.Value) ?? []),
                coverTitle,
            };

            var values = candidates
                .Select(NormalizeAlternative)
                .Where(static value => value is not null)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return values.Length == 0 ? null : string.Join(" — ", values!);
        }

        private static string? NormalizeAlternative(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        }

        private void ReportUnsafeEmbeddedSvg(XElement svg, string resourcePath)
        {
            var activeElements = svg.DescendantsAndSelf().Count(static element =>
                element.Name == SvgNamespace + "script"
                || element.Name == SvgNamespace + "foreignObject");
            var eventHandlers = svg.DescendantsAndSelf()
                .SelectMany(static element => element.Attributes())
                .Count(static attribute => attribute.Name.NamespaceName.Length == 0
                    && attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase));
            if (activeElements == 0 && eventHandlers == 0)
            {
                ReportUnrepresentedSvgGraphics(svg, resourcePath);
                return;
            }

            ReportSvgImageIssue(
                EpubDiagnosticCodes.UnsafeSvg,
                $"Embedded SVG active content was ignored ({activeElements} active elements, {eventHandlers} event handlers)",
                resourcePath);
            ReportUnrepresentedSvgGraphics(svg, resourcePath);
        }

        private void ReportUnrepresentedSvgGraphics(XElement svg, string resourcePath)
        {
            var unrepresented = svg.Descendants().Count(static element =>
                element.Name.Namespace == SvgNamespace
                && element.Name.LocalName is not ("image" or "title" or "desc" or "script" or "foreignObject"));
            if (unrepresented > 0)
            {
                ReportSvgImageIssue(
                    EpubDiagnosticCodes.SvgImageSemanticLoss,
                    $"Embedded SVG contains {unrepresented} non-image graphical elements that were not interpreted",
                    resourcePath);
            }
        }

        private void ReportSvgImageIssue(string code, string message, string resourcePath)
        {
            var key = new SvgImageIssueKey(code, message, resourcePath);
            svgImageIssues[key] = svgImageIssues.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        private IEnumerable<string> ReadImageReferences(
            XElement imageSource,
            XElement? fallbackImage,
            string resourcePath)
        {
            if (imageSource.Name == XhtmlNamespace + "picture")
            {
                foreach (var source in imageSource.Elements(XhtmlNamespace + "source"))
                {
                    var declaredType = (string?)source.Attribute("type");
                    if (declaredType is not null && !EpubImageInspector.IsSupportedDeclaredMediaType(declaredType))
                    {
                        diagnostics.Add(Warning(
                            EpubDiagnosticCodes.UnsupportedImageFormat,
                            $"Picture source type '{declaredType}' is not supported; the next source or img fallback is considered.",
                            resourcePath));
                        continue;
                    }

                    if (source.Attribute("media") is not null)
                    {
                        diagnostics.Add(Warning(
                            EpubDiagnosticCodes.UnsupportedElement,
                            "A picture source media condition cannot be evaluated without a rendering viewport; the source was skipped in favor of an unconditional fallback.",
                            resourcePath));
                        continue;
                    }

                    if (FirstSrcSetCandidate((string?)source.Attribute("srcset"), resourcePath) is { } candidate)
                    {
                        yield return candidate;
                    }
                }
            }

            var fallbackSource = (string?)fallbackImage?.Attribute("src");
            if (!string.IsNullOrWhiteSpace(fallbackSource))
            {
                yield return fallbackSource;
            }
            else
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidReference,
                    "An img fallback has no src attribute.",
                    resourcePath));
            }
        }

        private string? FirstSrcSetCandidate(string? srcset, string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(srcset))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidReference,
                    "A picture source has no srcset value.",
                    resourcePath));
                return null;
            }

            var candidates = srcset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (candidates.Length > 1 || candidates[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > 1)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.UnsupportedElement,
                    "Responsive srcset descriptors are not canonical Flow semantics; the first candidate is used deterministically.",
                    resourcePath));
            }

            return candidates[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        }

        private async Task<ImportedImage?> TryImportImageAssetAsync(
            string requestedPath,
            string usage,
            CancellationToken cancellationToken)
        {
            if (assetIds.TryGetValue(requestedPath, out var existingId))
            {
                return new ImportedImage(existingId, requestedPath);
            }

            var initial = manifest.Values.FirstOrDefault(item => string.Equals(item.Path, requestedPath, StringComparison.Ordinal));
            if (initial is null)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.MissingResource,
                    $"The {usage} references '{requestedPath}', which is absent from the manifest.",
                    requestedPath));
                return null;
            }

            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = initial;
            while (visited.Add(current.Id))
            {
                if (!entries.TryGetValue(current.Path, out var entry))
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.MissingResource,
                        $"Image manifest item '{current.Id}' references missing archive resource '{current.Path}'.",
                        current.Path));
                }
                else if (entry.Length > limits.MaximumImageBytes)
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.ImageBytesExceeded,
                        $"Image '{current.Path}' has {entry.Length} bytes, exceeding the configured {limits.MaximumImageBytes}-byte image limit.",
                        current.Path));
                }
                else
                {
                    await using var stream = entry.Open();
                    await using var buffer = await CopyWithLimitAsync(stream, limits.MaximumImageBytes, cancellationToken)
                        .ConfigureAwait(false);
                    var data = buffer.ToArray();
                    if (TryValidateImage(current, data, out var inspection))
                    {
                        var safeData = inspection.SanitizedData?.ToArray() ?? data;
                        if (inspection.SanitizationFindings is { Count: > 0 } findings)
                        {
                            diagnostics.Add(Warning(
                                EpubDiagnosticCodes.SanitizedSvg,
                                $"SVG '{current.Path}' was preserved after removing unsafe content: {string.Join(", ", findings)}.",
                                current.Path));
                        }

                        var digest = Convert.ToHexString(SHA256.HashData(safeData));
                        if (!assetHashes.TryGetValue(digest, out var assetId))
                        {
                            assetId = new AssetId(AllocateId($"asset-{Slug(current.Id)}").Value);
                            assetHashes.Add(digest, assetId);
                            Assets[assetId] = new FlowAsset(
                                assetId,
                                inspection.MediaType,
                                Path.GetFileName(current.Path),
                                safeData);
                        }
                        else
                        {
                            diagnostics.Add(new EpubDiagnostic(
                                EpubDiagnosticCodes.ImageDeduplicated,
                                EpubDiagnosticSeverity.Information,
                                $"Image '{current.Path}' has the same SHA-256 bytes as asset '{assetId.Value}' and reuses it.",
                                current.Path));
                        }

                        assetIds[current.Path] = assetId;
                        assetIds[requestedPath] = assetId;
                        ConsumedResourcePaths.Add(current.Path);
                        if (!string.Equals(current.Id, initial.Id, StringComparison.Ordinal))
                        {
                            diagnostics.Add(Warning(
                                EpubDiagnosticCodes.ImageFallbackUsed,
                                $"Image '{initial.Id}' used manifest fallback '{current.Id}' ({current.Path}).",
                                requestedPath));
                        }

                        return new ImportedImage(assetId, current.Path);
                    }
                }

                if (current.FallbackId is null || !manifest.TryGetValue(current.FallbackId, out current))
                {
                    return null;
                }
            }

            diagnostics.Add(Warning(
                EpubDiagnosticCodes.CircularFallback,
                $"Image fallback chain for '{initial.Id}' is circular and could not produce a safe asset.",
                requestedPath));
            return null;
        }

        private bool TryValidateImage(
            ManifestItem item,
            byte[] data,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out EpubImageInspection? inspection)
        {
            if (!EpubImageInspector.TryInspect(data, out inspection, out var failure))
            {
                var svg = item.MediaType.Split(';', 2)[0].Trim().Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase)
                    || failure?.StartsWith("SVG ", StringComparison.Ordinal) == true
                    || failure?.StartsWith("The SVG XML", StringComparison.Ordinal) == true;
                var code = svg
                    ? EpubDiagnosticCodes.UnsafeSvg
                    : EpubImageInspector.IsSupportedDeclaredMediaType(item.MediaType)
                        ? EpubDiagnosticCodes.InvalidImageData
                        : EpubDiagnosticCodes.UnsupportedImageFormat;
                diagnostics.Add(Warning(
                    code,
                    $"Image '{item.Path}' was rejected: {failure}",
                    item.Path));
                return false;
            }

            if (inspection.IsAnimated)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.UnsupportedImageFormat,
                    $"Animated GIF '{item.Path}' is unsupported; only static GIF is accepted.",
                    item.Path));
                inspection = null;
                return false;
            }

            if (inspection.Width is { } width && inspection.Height is { } height
                && (width > limits.MaximumImageWidth
                    || height > limits.MaximumImageHeight
                    || (long)width * height > limits.MaximumImagePixels))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.ImageDimensionsExceeded,
                    $"Image '{item.Path}' dimensions {width}x{height} exceed configured limits of {limits.MaximumImageWidth}x{limits.MaximumImageHeight} and {limits.MaximumImagePixels} pixels.",
                    item.Path));
                inspection = null;
                return false;
            }

            if (!EpubImageInspector.MediaTypeMatches(item.MediaType, inspection.MediaType))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.ImageMediaTypeMismatch,
                    $"Manifest media type '{item.MediaType}' for '{item.Path}' does not match detected bytes '{inspection.MediaType}'; the detected safe type is used.",
                    item.Path));
            }

            return true;
        }

        private IEnumerable<DocumentNode> AlternativeTextFallback(string? alternativeText, XElement image)
        {
            if (string.IsNullOrWhiteSpace(alternativeText))
            {
                return [];
            }

            return [new Paragraph(IdFor(image, "image-alt"), [new Text(alternativeText)])];
        }

        private IReadOnlyList<InlineNode> ConvertInline(IEnumerable<XNode> nodes, string resourcePath)
        {
            var result = new List<InlineNode>();
            foreach (var node in nodes)
            {
                if (node is XText text)
                {
                    if (text.Value.Length > 0)
                    {
                        result.Add(new Text(text.Value));
                    }

                    continue;
                }

                if (node is not XElement element)
                {
                    continue;
                }

                if (element.Name == MathMlNamespace + "math")
                {
                    result.AddRange(ApplyInternationalization(
                        element,
                        [ConvertInlineMath(element, resourcePath)],
                        resourcePath,
                        inherit: false));
                    continue;
                }

                var children = ConvertInline(element.Nodes(), resourcePath);
                if (element.Name.Namespace != XhtmlNamespace)
                {
                    ReportUnsupported(element, resourcePath, $"Foreign inline element <{element.Name.LocalName}>");
                    result.AddRange(children);
                    continue;
                }

                var converted = new List<InlineNode>();
                var name = element.Name.LocalName.ToLowerInvariant();
                switch (name)
                {
                    case "strong":
                    case "b":
                        converted.Add(new Strong(children));
                        break;
                    case "em":
                    case "i":
                        converted.Add(new Emphasis(children));
                        break;
                    case "u":
                        converted.Add(new Underline(children));
                        break;
                    case "s":
                    case "strike":
                    case "del":
                        converted.Add(new Strikethrough(children));
                        break;
                    case "code":
                        converted.Add(new InlineCode(element.Value));
                        break;
                    case "a":
                        if (IsFootnoteReferenceElement(element))
                        {
                            AddFootnoteReference(converted, element, children);
                        }
                        else
                        {
                            AddLink(converted, element, children, resourcePath);
                        }
                        break;
                    case "br":
                        converted.Add(new LineBreak());
                        break;
                    case "img":
                        diagnostics.Add(Warning(
                            EpubDiagnosticCodes.UnsupportedElement,
                            "An inline image cannot be represented in the current Flow inline model; its alt text was preserved.",
                            resourcePath));
                        var alt = (string?)element.Attribute("alt");
                        if (!string.IsNullOrWhiteSpace(alt))
                        {
                            converted.Add(new Text(alt));
                        }

                        break;
                    case "bdi":
                        converted.AddRange(ApplyInternationalization(
                            element,
                            children,
                            resourcePath,
                            inherit: false,
                            forcedMode: BidirectionalMode.Isolation));
                        result.AddRange(converted);
                        continue;
                    case "bdo":
                        converted.AddRange(ApplyInternationalization(
                            element,
                            children,
                            resourcePath,
                            inherit: false,
                            forcedMode: BidirectionalMode.Override));
                        result.AddRange(converted);
                        continue;
                    case "ruby":
                        var hasAnnotation = children.Any(static child => child is RubyAnnotation);
                        var hasBase = children.Any(static child => child is not RubyAnnotation and not RubyFallbackParenthesis);
                        if (!hasAnnotation || !hasBase || element.Ancestors(XhtmlNamespace + "ruby").Any())
                        {
                            diagnostics.Add(Warning(
                                EpubDiagnosticCodes.InvalidRubyStructure,
                                "Malformed or nested ruby was flattened while preserving its supported inline content.",
                                resourcePath));
                            converted.AddRange(children);
                        }
                        else
                        {
                            converted.Add(new Ruby(children));
                        }

                        break;
                    case "rt":
                        if (element.Parent?.Name != XhtmlNamespace + "ruby")
                        {
                            diagnostics.Add(Warning(
                                EpubDiagnosticCodes.InvalidRubyStructure,
                                "An <rt> outside a direct <ruby> parent was flattened while preserving its text.",
                                resourcePath));
                            converted.AddRange(children);
                        }
                        else
                        {
                            converted.Add(new RubyAnnotation(ApplyInternationalization(
                                element,
                                children,
                                resourcePath,
                                inherit: false)));
                            result.AddRange(converted);
                            continue;
                        }

                        break;
                    case "rp":
                        if (element.Parent?.Name != XhtmlNamespace + "ruby")
                        {
                            diagnostics.Add(Warning(
                                EpubDiagnosticCodes.InvalidRubyStructure,
                                "An <rp> outside a direct <ruby> parent was flattened while preserving its text.",
                                resourcePath));
                            converted.AddRange(children);
                        }
                        else
                        {
                            converted.Add(new RubyFallbackParenthesis(ApplyInternationalization(
                                element,
                                children,
                                resourcePath,
                                inherit: false)));
                            result.AddRange(converted);
                            continue;
                        }

                        break;
                    case "rb":
                    case "rtc":
                        ReportUnsupported(element, resourcePath, $"Ruby helper <{name}> was flattened");
                        converted.AddRange(children);
                        break;
                    case "span":
                        converted.AddRange(children);
                        break;
                    case "small":
                        ReportUnsupported(element, resourcePath);
                        converted.AddRange(children);
                        break;
                    case "abbr":
                    case "cite":
                    case "q":
                    case "sub":
                    case "sup":
                    case "mark":
                    case "time":
                        ReportUnsupported(element, resourcePath);
                        converted.AddRange(children);
                        break;
                    default:
                        ReportUnsupported(element, resourcePath, $"Inline element <{element.Name.LocalName}>");
                        converted.AddRange(children);
                        break;
                }

                result.AddRange(ApplyInternationalization(
                    element,
                    converted,
                    resourcePath,
                    inherit: false));
            }

            return result;
        }

        private IReadOnlyList<InlineNode> ConvertInlineContent(XElement element, string resourcePath) =>
            ApplyInternationalization(
                element,
                ConvertInline(element.Nodes(), resourcePath),
                resourcePath,
                inherit: true);

        private IReadOnlyList<InlineNode> ApplyInternationalization(
            XElement element,
            IEnumerable<InlineNode> nodes,
            string resourcePath,
            bool inherit,
            BidirectionalMode? forcedMode = null)
        {
            IReadOnlyList<InlineNode> result = nodes.ToArray();
            var directionElement = forcedMode is null && inherit
                ? element.AncestorsAndSelf().FirstOrDefault(static candidate => candidate.Attribute("dir") is not null)
                : element;
            var directionValue = (string?)directionElement?.Attribute("dir");
            var mode = forcedMode ?? BidirectionalMode.Embedding;
            if (forcedMode == BidirectionalMode.Isolation && string.IsNullOrWhiteSpace(directionValue))
            {
                result = [new BidirectionalSpan(TextDirection.Auto, mode, result)];
            }
            else if (TryReadDirection(directionValue, out var direction))
            {
                if (mode == BidirectionalMode.Override && direction == TextDirection.Auto)
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.InvalidTextDirection,
                        "A bidirectional override cannot use dir=\"auto\"; its content was preserved without override semantics.",
                        resourcePath));
                }
                else
                {
                    result = [new BidirectionalSpan(direction, mode, result)];
                }
            }
            else if (forcedMode == BidirectionalMode.Override || directionValue is not null)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidTextDirection,
                    $"Text direction '{directionValue ?? "(missing)"}' is invalid; visible content was preserved without directional semantics.",
                    resourcePath));
            }

            var languageElement = inherit
                ? element.AncestorsAndSelf().FirstOrDefault(HasLanguageDeclaration)
                : element;
            if (languageElement is not null && TryReadLanguage(languageElement, resourcePath, out var language))
            {
                result = [new LanguageSpan(language, result)];
            }

            return result;
        }

        private bool TryReadLanguage(
            XElement element,
            string resourcePath,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out LanguageTag? language)
        {
            var htmlValue = (string?)element.Attribute("lang");
            var xmlValue = (string?)element.Attribute(XNamespace.Xml + "lang");
            var htmlValid = LanguageTag.TryParse(htmlValue, out var htmlLanguage);
            var xmlValid = LanguageTag.TryParse(xmlValue, out var xmlLanguage);
            if (htmlValue is not null && !htmlValid)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidLanguage,
                    $"Inline lang value '{htmlValue}' is not a structurally valid BCP 47 tag.",
                    resourcePath));
            }

            if (xmlValue is not null && !xmlValid)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.InvalidLanguage,
                    $"Inline xml:lang value '{xmlValue}' is not a structurally valid BCP 47 tag.",
                    resourcePath));
            }

            if (htmlLanguage is not null && xmlLanguage is not null && htmlLanguage != xmlLanguage)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.ConflictingInlineLanguage,
                    $"Inline lang '{htmlLanguage}' conflicts with xml:lang '{xmlLanguage}'; xml:lang takes precedence.",
                    resourcePath));
            }

            language = xmlLanguage ?? htmlLanguage;
            return language is not null;
        }

        private static bool HasLanguageDeclaration(XElement element) =>
            element.Attribute("lang") is not null || element.Attribute(XNamespace.Xml + "lang") is not null;

        private static bool TryReadDirection(string? value, out TextDirection direction)
        {
            direction = value?.Trim().ToLowerInvariant() switch
            {
                "auto" => TextDirection.Auto,
                "ltr" => TextDirection.LeftToRight,
                "rtl" => TextDirection.RightToLeft,
                _ => (TextDirection)(-1),
            };
            return Enum.IsDefined(direction);
        }

        private MathExpression ConvertBlockMath(XElement element, string resourcePath)
        {
            var root = ConvertMathElement(element, resourcePath);
            return new MathExpression(IdFor(element, "math"), root, FindMathAlternative(element));
        }

        private InlineMath ConvertInlineMath(XElement element, string resourcePath) =>
            new(ConvertMathElement(element, resourcePath), FindMathAlternative(element));

        private MathElement ConvertMathElement(XElement element, string resourcePath)
        {
            var attributes = new List<KeyValuePair<string, string>>();
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration
                    || attribute.Name.LocalName == "alttext"
                    || attribute.Name == XNamespace.Xml + "lang"
                    || attribute.Name.Namespace == XNamespace.None
                    && attribute.Name.LocalName is "lang" or "dir")
                {
                    continue;
                }

                if (attribute.Name.Namespace == XNamespace.None
                    && MathElement.IsSupportedAttribute(attribute.Name.LocalName))
                {
                    attributes.Add(KeyValuePair.Create(attribute.Name.LocalName, attribute.Value));
                }
                else
                {
                    ReportMathLoss(element, resourcePath, $"MathML attribute '{attribute.Name}' was removed");
                }
            }

            return new MathElement("math", ConvertMathChildren(element.Nodes(), resourcePath), attributes);
        }

        private IEnumerable<MathNode> ConvertMathChildren(IEnumerable<XNode> nodes, string resourcePath)
        {
            foreach (var node in nodes)
            {
                if (node is XText text)
                {
                    if (text.Value.Length > 0)
                    {
                        yield return new MathText(text.Value);
                    }

                    continue;
                }

                if (node is not XElement element)
                {
                    continue;
                }

                var name = element.Name.LocalName;
                if (element.Name.Namespace != MathMlNamespace
                    || name is "script" or "annotation-xml")
                {
                    ReportMathLoss(element, resourcePath, $"Unsafe or foreign MathML child <{name}> was removed");
                    continue;
                }

                if (!MathElement.IsSupportedName(name))
                {
                    ReportMathLoss(element, resourcePath, $"Unsupported MathML element <{name}> was flattened");
                    foreach (var child in ConvertMathChildren(element.Nodes(), resourcePath))
                    {
                        yield return child;
                    }

                    continue;
                }

                var attributes = new List<KeyValuePair<string, string>>();
                foreach (var attribute in element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration)
                    {
                        continue;
                    }

                    if (attribute.Name.Namespace == XNamespace.None
                        && MathElement.IsSupportedAttribute(attribute.Name.LocalName))
                    {
                        attributes.Add(KeyValuePair.Create(attribute.Name.LocalName, attribute.Value));
                    }
                    else
                    {
                        ReportMathLoss(element, resourcePath, $"MathML attribute '{attribute.Name}' was removed");
                    }
                }

                yield return new MathElement(name, ConvertMathChildren(element.Nodes(), resourcePath), attributes);
            }
        }

        private static string? FindMathAlternative(XElement element)
        {
            var altText = (string?)element.Attribute("alttext");
            if (!string.IsNullOrWhiteSpace(altText))
            {
                return altText;
            }

            return element.Descendants(MathMlNamespace + "annotation")
                .Where(annotation => ((string?)annotation.Attribute("encoding")) is "text/plain" or "application/x-tex")
                .Select(static annotation => annotation.Value)
                .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        }

        private void ReportMathLoss(XElement element, string resourcePath, string description)
        {
            var key = new UnsupportedElementKey(resourcePath, element.Name.ToString(), description);
            mathLosses[key] = mathLosses.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        private void AddFootnoteReference(
            ICollection<InlineNode> result,
            XElement element,
            IReadOnlyList<InlineNode> label)
        {
            if (footnoteReferenceTargets.TryGetValue(element, out var targetId))
            {
                result.Add(new FootnoteReference(targetId, label));
                return;
            }

            foreach (var child in label)
            {
                result.Add(child);
            }
        }

        private void AddLink(
            ICollection<InlineNode> result,
            XElement element,
            IReadOnlyList<InlineNode> children,
            string resourcePath)
        {
            var href = (string?)element.Attribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                diagnostics.Add(Warning(EpubDiagnosticCodes.InvalidReference, "A link has no href; its label was preserved.", resourcePath));
                foreach (var child in children)
                {
                    result.Add(child);
                }

                return;
            }

            if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                if (IsSafeMailtoHref(href))
                {
                    result.Add(new Link(href, children));
                }
                else
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.InvalidReference,
                        "Unsafe mailto link was rejected; its label was preserved.",
                        resourcePath));
                    foreach (var child in children)
                    {
                        result.Add(child);
                    }
                }

                return;
            }

            if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
            {
                if (absolute.Scheme is "http" or "https")
                {
                    result.Add(new Link(href, children));
                }
                else
                {
                    diagnostics.Add(Warning(
                        EpubDiagnosticCodes.InvalidReference,
                        $"Link scheme '{absolute.Scheme}' is unsupported; its label was preserved.",
                        resourcePath));
                    foreach (var child in children)
                    {
                        result.Add(child);
                    }
                }

                return;
            }

            if (TryBuildReferenceKey(resourcePath, href, out _, out var key)
                && anchors.TryGetValue(key, out var nodeId))
            {
                result.Add(new Link(DocumentAnchor.Create([nodeId]).Value, children));
                return;
            }

            diagnostics.Add(Warning(
                EpubDiagnosticCodes.InvalidReference,
                $"Internal link target '{href}' could not be resolved; its label was preserved.",
                resourcePath));
            foreach (var child in children)
            {
                result.Add(child);
            }
        }

        private void ReportUnsupported(XElement element, string resourcePath, string? description = null)
        {
            var key = new UnsupportedElementKey(
                resourcePath,
                element.Name.ToString(),
                description ?? $"Element <{element.Name.LocalName}>");
            unsupportedElements[key] = unsupportedElements.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        private NodeId IdFor(XElement? element, string kind)
        {
            if (element is not null && elementIds.TryGetValue(element, out var nodeId))
            {
                RegisterNodeTypography(element, nodeId);
                return nodeId;
            }

            nodeId = AllocateId($"epub-{kind}-{++generatedId}");
            if (element is not null)
            {
                elementIds[element] = nodeId;
                RegisterNodeTypography(element, nodeId);
            }

            return nodeId;
        }

        private void RegisterNodeTypography(XElement element, NodeId nodeId)
        {
            if (cssStyles.TryGetValue(element, out var style))
            {
                NodeTypography.TryAdd(nodeId, style);
            }
        }

        private NodeId GeneratedIdFor(XElement styleSource, string kind)
        {
            var nodeId = AllocateId($"epub-{kind}-{++generatedId}");
            RegisterNodeTypography(styleSource, nodeId);
            return nodeId;
        }

        private NodeId AllocateId(string candidate) => AllocateId(candidate, out _);

        private NodeId AllocateId(string candidate, out bool collision)
        {
            var normalized = Slug(candidate);
            if (normalized.Length == 0 || normalized[0] is < 'a' or > 'z')
            {
                normalized = $"epub-{normalized}";
            }

            normalized = normalized[..Math.Min(normalized.Length, 112)].TrimEnd('-', '_', '.');
            var unique = normalized;
            var suffix = 2;
            collision = false;
            while (!allocatedIds.Add(unique))
            {
                collision = true;
                unique = $"{normalized}-{suffix++}";
            }

            return new NodeId(unique);
        }

        private static bool IsRepresentedNode(XElement element)
        {
            if (element.Name == MathMlNamespace + "math")
            {
                return true;
            }

            if (element.Name == SvgNamespace + "svg" || element.Name == SvgNamespace + "image")
            {
                return true;
            }

            var name = element.Name.LocalName.ToLowerInvariant();
            return name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
                or "p" or "ol" or "ul" or "li" or "blockquote" or "figure" or "img"
                or "picture" or "figcaption" or "section" or "article" or "pre" or "hr"
                or "table" or "caption" or "thead" or "tbody" or "tfoot" or "tr" or "th" or "td";
        }

        private static bool IsImageSourceElement(XElement element) =>
            element.Name == XhtmlNamespace + "img"
            || element.Name == XhtmlNamespace + "picture"
            || element.Name == SvgNamespace + "svg"
            || element.Name == SvgNamespace + "image";

        private static bool IsExtractableInlineImage(XElement element) =>
            element.Name == XhtmlNamespace + "img"
            || element.Name == XhtmlNamespace + "picture"
            || element.Name == SvgNamespace + "svg"
            || element.Name == SvgNamespace + "image";

        private static XElement? FindSingleImageParagraph(XElement image)
        {
            var paragraph = image.Ancestors(XhtmlNamespace + "p").FirstOrDefault();
            if (paragraph is null)
            {
                return null;
            }

            var images = paragraph.Descendants().Count(IsImageOccurrence);
            var hasVisibleText = paragraph.DescendantNodes()
                .OfType<XText>()
                .Any(static text => !string.IsNullOrWhiteSpace(text.Value));
            return images == 1 && !hasVisibleText ? paragraph : null;
        }

        private static bool IsImageOccurrence(XElement element) =>
            element.Name == XhtmlNamespace + "img"
            || element.Name == SvgNamespace + "image";

        private static bool IsFootnoteElement(XElement element) =>
            element.Name.Namespace == XhtmlNamespace
            && (HasToken((string?)element.Attribute(EpubNamespace + "type"), "footnote")
                || HasToken((string?)element.Attribute(EpubNamespace + "type"), "endnote")
                || HasToken((string?)element.Attribute("role"), "doc-footnote"));

        private static bool IsFootnoteReferenceElement(XElement element) =>
            element.Name == XhtmlNamespace + "a"
            && (HasToken((string?)element.Attribute(EpubNamespace + "type"), "noteref")
                || HasToken((string?)element.Attribute("role"), "doc-noteref"));

        private static bool IsBacklinkElement(XElement element) =>
            HasToken((string?)element.Attribute(EpubNamespace + "type"), "backlink")
            || HasToken((string?)element.Attribute("role"), "doc-backlink")
            || HasToken((string?)element.Attribute("rel"), "backlink");

        private static bool IsInlineElement(XElement element)
        {
            if (element.Name == MathMlNamespace + "math")
            {
                return !string.Equals((string?)element.Attribute("display"), "block", StringComparison.OrdinalIgnoreCase);
            }

            if (element.Name.Namespace != XhtmlNamespace)
            {
                return false;
            }

            var name = element.Name.LocalName.ToLowerInvariant();
            return name is "strong" or "b" or "em" or "i" or "u" or "s" or "strike"
                or "del" or "code" or "a" or "br" or "span" or "small" or "abbr"
                or "cite" or "q" or "sub" or "sup" or "mark" or "time" or "bdi"
                or "bdo" or "ruby" or "rt" or "rp" or "rb" or "rtc";
        }

        private static string Slug(string value)
        {
            var builder = new StringBuilder(value.Length);
            var previousSeparator = false;
            foreach (var character in value.ToLowerInvariant())
            {
                if (character is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_')
                {
                    builder.Append(character);
                    previousSeparator = false;
                }
                else if (!previousSeparator)
                {
                    builder.Append('-');
                    previousSeparator = true;
                }
            }

            return builder.ToString().Trim('-', '_', '.');
        }

        private static string NodeText(XNode node) => node switch
        {
            XText text => text.Value,
            XElement element => element.Value,
            _ => string.Empty,
        };

        private readonly record struct UnsupportedElementKey(
            string ResourcePath,
            string ElementName,
            string Description);

        private readonly record struct SourceLocationKey(string ResourcePath, string? Fragment);

        private readonly record struct SvgImageIssueKey(string Code, string Message, string ResourcePath);

        private readonly record struct HeadingLevelNormalizationKey(
            string ResourcePath,
            int SourceLevel,
            int NormalizedLevel);

        private sealed record InlineContentPart(IReadOnlyList<XNode> Nodes, XElement? Image);

        private sealed record ConvertedInlinePart(ImmutableArray<InlineNode> Inline, XElement? Image)
        {
            internal ConvertedInlinePart(IEnumerable<InlineNode> inline, XElement? image)
                : this(inline.ToImmutableArray(), image)
            {
            }
        }

        private sealed record ImportedImage(AssetId AssetId, string ResourcePath);
    }
}
