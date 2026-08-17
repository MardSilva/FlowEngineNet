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
    private static readonly XNamespace DcNamespace = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace XhtmlNamespace = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace EpubNamespace = "http://www.idpf.org/2007/ops";
    private static readonly XNamespace NcxNamespace = "http://www.daisy.org/z3986/2005/ncx/";

    private readonly EpubImportLimits limits;

    /// <summary>Creates an importer with optional host-defined resource limits.</summary>
    /// <param name="limits">Limits to apply, or <see langword="null" /> for conservative defaults.</param>
    public EpubImporter(EpubImportLimits? limits = null)
    {
        this.limits = limits ?? new EpubImportLimits();
    }

    /// <inheritdoc />
    public async Task<EpubImportResult> ImportAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The EPUB source stream must be readable.", nameof(source));
        }

        var diagnostics = new List<EpubDiagnostic>();
        try
        {
            await using var archiveBuffer = await EpubArchiveUtilities.CopyWithLimitAsync(
                source,
                limits.MaximumArchiveBytes,
                cancellationToken).ConfigureAwait(false);
            using var archive = new ZipArchive(archiveBuffer, ZipArchiveMode.Read, leaveOpen: false);
            var entries = IndexArchive(archive, diagnostics);
            if (HasErrors(diagnostics))
            {
                return new EpubImportResult(null, diagnostics);
            }

            if (!entries.TryGetValue(ContainerPath, out var containerEntry))
            {
                diagnostics.Add(Error(EpubDiagnosticCodes.MissingContainer, $"Required resource '{ContainerPath}' was not found."));
                return new EpubImportResult(null, diagnostics);
            }

            var container = await LoadXmlAsync(containerEntry, ContainerPath, diagnostics, cancellationToken)
                .ConfigureAwait(false);
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

                return new EpubImportResult(null, diagnostics);
            }

            var package = await LoadXmlAsync(packageEntry, packagePath, diagnostics, cancellationToken)
                .ConfigureAwait(false);
            if (package is null)
            {
                return new EpubImportResult(null, diagnostics);
            }

            var model = ReadPackage(package, packagePath, diagnostics);
            if (model is null)
            {
                return new EpubImportResult(null, diagnostics);
            }

            var document = await ConvertPackageAsync(model, entries, diagnostics, cancellationToken)
                .ConfigureAwait(false);
            return new EpubImportResult(document, diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (EpubLimitExceededException exception)
        {
            diagnostics.Add(Error(EpubDiagnosticCodes.ArchiveLimitExceeded, exception.Message));
            return new EpubImportResult(null, diagnostics);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or XmlException)
        {
            diagnostics.Add(Error(
                EpubDiagnosticCodes.InvalidArchive,
                $"The EPUB could not be processed safely: {exception.Message}"));
            return new EpubImportResult(null, diagnostics);
        }
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

        var metadataElement = root.Element(OpfNamespace + "metadata");
        var title = metadataElement?.Elements(DcNamespace + "title")
            .Select(static element => NormalizedText(element.Value))
            .FirstOrDefault(static value => value.Length > 0);
        if (title is null)
        {
            title = "Untitled EPUB";
            diagnostics.Add(Warning(
                EpubDiagnosticCodes.MetadataFallback,
                "The EPUB has no dc:title; the importer used 'Untitled EPUB'.",
                packagePath));
        }

        var identifier = metadataElement?.Elements(DcNamespace + "identifier")
            .Select(static element => NormalizedText(element.Value))
            .FirstOrDefault(static value => value.Length > 0);
        var language = metadataElement?.Elements(DcNamespace + "language")
            .Select(static element => NormalizedText(element.Value))
            .FirstOrDefault(static value => value.Length > 0);
        var authors = metadataElement?.Elements(DcNamespace + "creator")
            .Select(static element => NormalizedText(element.Value))
            .Where(static value => value.Length > 0)
            .ToImmutableArray() ?? [];
        var description = metadataElement?.Elements(DcNamespace + "description")
            .Select(static element => NormalizedText(element.Value))
            .FirstOrDefault(static value => value.Length > 0);

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
            var manifestItem = new ManifestItem(id, resourcePath, mediaType, properties);
            if (!manifest.TryAdd(id, manifestItem))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.InvalidPackage,
                    $"Manifest ID '{id}' occurs more than once.",
                    packagePath));
            }
            else
            {
                manifestOrder.Add(manifestItem);
            }
        }

        var spine = new List<SpineItem>();
        var spineElement = root.Element(OpfNamespace + "spine");
        var spineTocId = (string?)spineElement?.Attribute("toc");
        foreach (var itemReference in spineElement?.Elements(OpfNamespace + "itemref") ?? [])
        {
            var idref = (string?)itemReference.Attribute("idref");
            if (string.IsNullOrWhiteSpace(idref) || !manifest.TryGetValue(idref, out var item))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.InvalidPackage,
                    $"Spine reference '{idref ?? "(missing)"}' does not resolve to a manifest item.",
                    packagePath));
                continue;
            }

            var isLinear = !string.Equals((string?)itemReference.Attribute("linear"), "no", StringComparison.OrdinalIgnoreCase);
            if (!isLinear)
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.NonLinearSpineItem,
                    $"Non-linear spine item '{idref}' is imported in declared order so its content is not lost.",
                    item.Path));
            }

            spine.Add(new SpineItem(item, isLinear));
        }

        if (spine.Count == 0)
        {
            diagnostics.Add(Error(EpubDiagnosticCodes.InvalidPackage, "The OPF spine contains no resolvable items.", packagePath));
            return null;
        }

        return new PackageModel(
            packagePath,
            title,
            identifier,
            language,
            authors,
            description,
            manifest,
            manifestOrder,
            spine,
            spineTocId);
    }

    private async Task<FlowDocument?> ConvertPackageAsync(
        PackageModel package,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        List<EpubDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var xhtmlDocuments = new List<XhtmlModel>();
        foreach (var spineItem in package.Spine)
        {
            if (!string.Equals(spineItem.Item.MediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Warning(
                    EpubDiagnosticCodes.UnsupportedResource,
                    $"Spine resource with media type '{spineItem.Item.MediaType}' is not supported.",
                    spineItem.Item.Path));
                continue;
            }

            if (!entries.TryGetValue(spineItem.Item.Path, out var entry))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.MissingResource,
                    "A spine XHTML resource is missing from the archive.",
                    spineItem.Item.Path));
                continue;
            }

            var xhtml = await LoadXmlAsync(entry, spineItem.Item.Path, diagnostics, cancellationToken)
                .ConfigureAwait(false);
            if (xhtml?.Root?.Name != XhtmlNamespace + "html"
                || xhtml.Root.Element(XhtmlNamespace + "body") is null)
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.InvalidXml,
                    "The spine resource must be XHTML in the XHTML namespace and contain a body.",
                    spineItem.Item.Path));
                continue;
            }

            xhtmlDocuments.Add(new XhtmlModel(spineItem.Item, xhtml));
        }

        if (xhtmlDocuments.Count == 0)
        {
            return null;
        }

        var navigationDocuments = await LoadNavigationDocumentsAsync(
            package,
            entries,
            xhtmlDocuments,
            diagnostics,
            cancellationToken).ConfigureAwait(false);

        var context = new ConversionContext(entries, package.Manifest, diagnostics, limits);
        context.PrepareIds(xhtmlDocuments);
        var content = new List<DocumentNode>();
        var tableOfContents = context.ConvertTableOfContents(navigationDocuments, package.SpineTocId);
        if (tableOfContents is not null)
        {
            content.Add(tableOfContents);
        }

        foreach (var xhtml in xhtmlDocuments)
        {
            content.Add(await context.ConvertChapterAsync(xhtml, cancellationToken).ConfigureAwait(false));
        }

        ReportUnusedManifestResources(package, context, diagnostics);

        var documentId = CreateDocumentId(package.Identifier, package.PackagePath, diagnostics);
        var document = new FlowDocument(
            new DocumentIdentity(documentId),
            new DocumentMetadata(package.Title, package.Language, package.Authors, description: package.Description),
            new DocumentContent(content),
            context.Assets.Values);

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

        return document;
    }

    private async Task<IReadOnlyList<NavigationModel>> LoadNavigationDocumentsAsync(
        PackageModel package,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        IReadOnlyList<XhtmlModel> xhtmlDocuments,
        List<EpubDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var xhtmlByPath = xhtmlDocuments.ToDictionary(static item => item.Item.Path, StringComparer.Ordinal);
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
        var spinePaths = package.Spine.Select(static item => item.Item.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var item in package.Manifest.Values.OrderBy(static item => item.Path, StringComparer.Ordinal))
        {
            if (spinePaths.Contains(item.Path) || context.ConsumedResourcePaths.Contains(item.Path))
            {
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

    private static bool HasErrors(IEnumerable<EpubDiagnostic> diagnostics) =>
        diagnostics.Any(static diagnostic => diagnostic.Severity == EpubDiagnosticSeverity.Error);

    private static EpubDiagnostic Error(string code, string message, string? resource = null) =>
        new(code, EpubDiagnosticSeverity.Error, message, resource);

    private static EpubDiagnostic Warning(string code, string message, string? resource = null) =>
        new(code, EpubDiagnosticSeverity.Warning, message, resource);

    private sealed record ManifestItem(
        string Id,
        string Path,
        string MediaType,
        ImmutableHashSet<string> Properties);

    private sealed record SpineItem(ManifestItem Item, bool IsLinear);

    private sealed record PackageModel(
        string PackagePath,
        string Title,
        string? Identifier,
        string? Language,
        ImmutableArray<string> Authors,
        string? Description,
        IReadOnlyDictionary<string, ManifestItem> Manifest,
        IReadOnlyList<ManifestItem> ManifestOrder,
        IReadOnlyList<SpineItem> Spine,
        string? SpineTocId);

    private sealed record XhtmlModel(ManifestItem Item, XDocument Document);

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
        private readonly Dictionary<XElement, NodeId> elementIds = [];
        private readonly Dictionary<string, NodeId> anchors = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AssetId> assetIds = new(StringComparer.Ordinal);
        private readonly HashSet<string> allocatedIds = new(StringComparer.Ordinal);
        private int generatedId;

        internal ConversionContext(
            IReadOnlyDictionary<string, ZipArchiveEntry> entries,
            IReadOnlyDictionary<string, ManifestItem> manifest,
            List<EpubDiagnostic> diagnostics,
            EpubImportLimits limits)
        {
            this.entries = entries;
            this.manifest = manifest;
            this.diagnostics = diagnostics;
            this.limits = limits;
        }

        internal Dictionary<AssetId, FlowAsset> Assets { get; } = [];

        internal HashSet<string> ConsumedResourcePaths { get; } = new(StringComparer.Ordinal);

        internal void PrepareIds(IEnumerable<XhtmlModel> xhtmlDocuments)
        {
            foreach (var xhtml in xhtmlDocuments)
            {
                var chapterId = AllocateId($"chapter-{Slug(xhtml.Item.Id)}");
                var body = xhtml.Document.Root?.Element(XhtmlNamespace + "body");
                if (body is null)
                {
                    continue;
                }

                elementIds[body] = chapterId;
                anchors[xhtml.Item.Path] = chapterId;
                foreach (var element in body.Descendants())
                {
                    var htmlId = (string?)element.Attribute("id");
                    if (string.IsNullOrWhiteSpace(htmlId))
                    {
                        continue;
                    }

                    var targetElement = IsRepresentedNode(element)
                        ? element
                        : element.Ancestors().FirstOrDefault(IsRepresentedNode) ?? body;
                    if (!elementIds.TryGetValue(targetElement, out var nodeId))
                    {
                        nodeId = AllocateId($"{chapterId.Value}-{Slug(htmlId)}");
                        elementIds[targetElement] = nodeId;
                    }

                    anchors[$"{xhtml.Item.Path}#{htmlId}"] = nodeId;
                    if (!ReferenceEquals(targetElement, element))
                    {
                        diagnostics.Add(Warning(
                            EpubDiagnosticCodes.UnsupportedElement,
                            $"Anchor '#{htmlId}' is attached to an inline or unsupported element and was mapped to its containing Flow block.",
                            xhtml.Item.Path));
                    }
                }
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
                    : ConvertInline(heading.Nodes(), source.Item.Path).ToImmutableArray();
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
                        ConvertInline(anchor.Nodes(), source.Item.Path).ToImmutableArray(),
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
            LineBreak => " ",
            _ => string.Empty,
        }));

        private static bool HasToken(string? values, string token) =>
            values?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Contains(token, StringComparer.Ordinal) == true;

        internal async Task<Chapter> ConvertChapterAsync(XhtmlModel xhtml, CancellationToken cancellationToken)
        {
            var body = xhtml.Document.Root!.Element(XhtmlNamespace + "body")!;
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

        private async Task<IEnumerable<DocumentNode>> ConvertBlockAsync(
            XElement element,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            if (element.Name.Namespace != XhtmlNamespace)
            {
                ReportUnsupported(element, resourcePath);
                return FallbackTextBlock(element, "foreign");
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
                    return [new Heading(IdFor(element, "heading"), name[1] - '0', ConvertInline(element.Nodes(), resourcePath))];
                case "p":
                    return [new Paragraph(IdFor(element, "paragraph"), ConvertInline(element.Nodes(), resourcePath))];
                case "ol":
                    return [await ConvertOrderedListAsync(element, resourcePath, cancellationToken).ConfigureAwait(false)];
                case "ul":
                    return [await ConvertUnorderedListAsync(element, resourcePath, cancellationToken).ConfigureAwait(false)];
                case "blockquote":
                    return [new BlockQuote(
                        IdFor(element, "blockquote"),
                        await ConvertChildrenAsync(element, resourcePath, cancellationToken).ConfigureAwait(false))];
                case "figure":
                    return await ConvertFigureAsync(element, resourcePath, cancellationToken).ConfigureAwait(false);
                case "img":
                    return await ConvertImageAsync(element, element, null, resourcePath, cancellationToken).ConfigureAwait(false);
                case "section":
                case "article":
                    return [new Section(
                        IdFor(element, name),
                        await ConvertChildrenAsync(element, resourcePath, cancellationToken).ConfigureAwait(false))];
                case "div":
                case "main":
                case "header":
                case "footer":
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
                    return FallbackTextBlock(element, name);
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

                var inline = ConvertInline(inlineBuffer, resourcePath);
                if (inline.Any(static node => node is not Text text || !string.IsNullOrWhiteSpace(text.Value)))
                {
                    result.Add(new Paragraph(IdFor(null, "container-text"), inline));
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

        private async Task<IReadOnlyList<ListItem>> ConvertListItemsAsync(
            XElement list,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var result = new List<ListItem>();
            foreach (var item in list.Elements(XhtmlNamespace + "li"))
            {
                var blockChildren = new List<DocumentNode>();
                var inlineNodes = item.Nodes().TakeWhile(static node => node is not XElement element
                    || element.Name.LocalName is not ("ol" or "ul" or "p")).ToArray();
                if (NormalizedText(string.Concat(inlineNodes.Select(NodeText))).Length > 0)
                {
                    blockChildren.Add(new Paragraph(IdFor(null, "list-text"), ConvertInline(inlineNodes, resourcePath)));
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
            var image = element.DescendantsAndSelf(XhtmlNamespace + "img").FirstOrDefault();
            var captionElement = element.Element(XhtmlNamespace + "figcaption");
            if (image is null)
            {
                ReportUnsupported(element, resourcePath, "Figure without an image");
                return FallbackTextBlock(element, "figure");
            }

            Caption? caption = null;
            if (captionElement is not null)
            {
                caption = new Caption(IdFor(captionElement, "caption"), ConvertInline(captionElement.Nodes(), resourcePath));
            }

            return await ConvertImageAsync(image, element, caption, resourcePath, cancellationToken).ConfigureAwait(false);
        }

        private async Task<IEnumerable<DocumentNode>> ConvertImageAsync(
            XElement image,
            XElement idSource,
            Caption? caption,
            string resourcePath,
            CancellationToken cancellationToken)
        {
            var source = (string?)image.Attribute("src");
            var alternativeText = (string?)image.Attribute("alt");
            if (string.IsNullOrWhiteSpace(source)
                || !TryNormalizeArchivePath(GetDirectory(resourcePath), source, out var imagePath))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.InvalidReference,
                    $"Image source '{source ?? "(missing)"}' is missing or unsafe.",
                    resourcePath));
                return AlternativeTextFallback(alternativeText, idSource);
            }

            var manifestItem = manifest.Values.FirstOrDefault(item => string.Equals(item.Path, imagePath, StringComparison.Ordinal));
            if (manifestItem is null
                || !manifestItem.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || !entries.TryGetValue(imagePath, out var entry))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.MissingResource,
                    $"Image resource '{imagePath}' is absent from the manifest/archive or is not declared as an image.",
                    resourcePath));
                return AlternativeTextFallback(alternativeText, idSource);
            }

            if (!assetIds.TryGetValue(imagePath, out var assetId))
            {
                assetId = new AssetId(AllocateId($"asset-{Slug(manifestItem.Id)}").Value);
                assetIds.Add(imagePath, assetId);
                await using var stream = entry.Open();
                await using var buffer = await CopyWithLimitAsync(stream, limits.MaximumEntryBytes, cancellationToken)
                    .ConfigureAwait(false);
                Assets[assetId] = new FlowAsset(assetId, manifestItem.MediaType, Path.GetFileName(imagePath), buffer.ToArray());
                ConsumedResourcePaths.Add(imagePath);
            }

            return [new Figure(IdFor(idSource, "figure"), assetId, caption, alternativeText)];
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

                var children = ConvertInline(element.Nodes(), resourcePath);
                switch (element.Name.LocalName.ToLowerInvariant())
                {
                    case "strong":
                    case "b":
                        result.Add(new Strong(children));
                        break;
                    case "em":
                    case "i":
                        result.Add(new Emphasis(children));
                        break;
                    case "u":
                        result.Add(new Underline(children));
                        break;
                    case "s":
                    case "strike":
                    case "del":
                        result.Add(new Strikethrough(children));
                        break;
                    case "code":
                        result.Add(new InlineCode(element.Value));
                        break;
                    case "a":
                        AddLink(result, element, children, resourcePath);
                        break;
                    case "br":
                        result.Add(new LineBreak());
                        break;
                    case "img":
                        diagnostics.Add(Warning(
                            EpubDiagnosticCodes.UnsupportedElement,
                            "An inline image cannot be represented in the current Flow inline model; its alt text was preserved.",
                            resourcePath));
                        var alt = (string?)element.Attribute("alt");
                        if (!string.IsNullOrWhiteSpace(alt))
                        {
                            result.Add(new Text(alt));
                        }

                        break;
                    case "span":
                    case "small":
                    case "sub":
                    case "sup":
                        result.AddRange(children);
                        break;
                    default:
                        diagnostics.Add(Warning(
                            EpubDiagnosticCodes.UnsupportedElement,
                            $"Inline element <{element.Name.LocalName}> is unsupported; its textual content was preserved.",
                            resourcePath));
                        result.AddRange(children);
                        break;
                }
            }

            return result;
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

            if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
            {
                if (absolute.Scheme is "http" or "https" or "mailto")
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

        private IEnumerable<DocumentNode> FallbackTextBlock(XElement element, string kind)
        {
            var text = NormalizedText(element.Value);
            return text.Length == 0
                ? []
                : [new Paragraph(IdFor(element, $"fallback-{kind}"), [new Text(text)])];
        }

        private void ReportUnsupported(XElement element, string resourcePath, string? description = null)
        {
            diagnostics.Add(Warning(
                EpubDiagnosticCodes.UnsupportedElement,
                $"{description ?? $"Element <{element.Name.LocalName}>"} is unsupported; recoverable text was preserved.",
                resourcePath));
        }

        private NodeId IdFor(XElement? element, string kind)
        {
            if (element is not null && elementIds.TryGetValue(element, out var nodeId))
            {
                return nodeId;
            }

            return AllocateId($"epub-{kind}-{++generatedId}");
        }

        private NodeId AllocateId(string candidate)
        {
            var normalized = Slug(candidate);
            if (normalized.Length == 0 || normalized[0] is < 'a' or > 'z')
            {
                normalized = $"epub-{normalized}";
            }

            normalized = normalized[..Math.Min(normalized.Length, 112)].TrimEnd('-', '_', '.');
            var unique = normalized;
            var suffix = 2;
            while (!allocatedIds.Add(unique))
            {
                unique = $"{normalized}-{suffix++}";
            }

            return new NodeId(unique);
        }

        private static bool IsRepresentedNode(XElement element)
        {
            var name = element.Name.LocalName.ToLowerInvariant();
            return name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
                or "p" or "ol" or "ul" or "li" or "blockquote" or "figure" or "img"
                or "figcaption" or "section" or "article" or "pre" or "hr";
        }

        private static bool IsInlineElement(XElement element)
        {
            var name = element.Name.LocalName.ToLowerInvariant();
            return name is "strong" or "b" or "em" or "i" or "u" or "s" or "strike"
                or "del" or "code" or "a" or "br" or "img" or "span" or "small" or "sub" or "sup";
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
    }
}
