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
            await using var archiveBuffer = await CopyWithLimitAsync(
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
            if (entry.Length > limits.MaximumEntryBytes
                || excessiveRatio)
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

    private async Task<XDocument?> LoadXmlAsync(
        ZipArchiveEntry entry,
        string resource,
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
            if (!manifest.TryAdd(id, new ManifestItem(id, resourcePath, mediaType, properties)))
            {
                diagnostics.Add(Error(
                    EpubDiagnosticCodes.InvalidPackage,
                    $"Manifest ID '{id}' occurs more than once.",
                    packagePath));
            }
        }

        var spine = new List<SpineItem>();
        foreach (var itemReference in root.Element(OpfNamespace + "spine")?.Elements(OpfNamespace + "itemref") ?? [])
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

        return new PackageModel(packagePath, title, identifier, language, authors, description, manifest, spine);
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

        var context = new ConversionContext(entries, package.Manifest, diagnostics, limits);
        context.PrepareIds(xhtmlDocuments);
        var chapters = new List<DocumentNode>();
        foreach (var xhtml in xhtmlDocuments)
        {
            chapters.Add(await context.ConvertChapterAsync(xhtml, cancellationToken).ConfigureAwait(false));
        }

        ReportUnusedManifestResources(package, context, diagnostics);

        var documentId = CreateDocumentId(package.Identifier, package.PackagePath, diagnostics);
        var document = new FlowDocument(
            new DocumentIdentity(documentId),
            new DocumentMetadata(package.Title, package.Language, package.Authors, description: package.Description),
            new DocumentContent(chapters),
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

    private static async Task<MemoryStream> CopyWithLimitAsync(
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

    private static bool TryNormalizeArchivePath(string? baseDirectory, string value, out string normalized)
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

            if (segment.Contains(':', StringComparison.Ordinal)
                || segment.Any(char.IsControl))
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

    private static string GetDirectory(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? string.Empty : path[..separator];
    }

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
        IReadOnlyList<SpineItem> Spine);

    private sealed record XhtmlModel(ManifestItem Item, XDocument Document);

    private sealed class EpubLimitExceededException : Exception
    {
        internal EpubLimitExceededException(string message)
            : base(message)
        {
        }
    }

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

            var parts = href.Split('#', 2);
            if (parts.Length == 2)
            {
                try
                {
                    parts[1] = Uri.UnescapeDataString(parts[1]);
                }
                catch (UriFormatException)
                {
                    parts[1] = string.Empty;
                }
            }
            var targetPath = parts[0].Length == 0
                ? resourcePath
                : TryNormalizeArchivePath(GetDirectory(resourcePath), parts[0], out var normalized) ? normalized : null;
            var key = targetPath is null ? null : parts.Length == 2 ? $"{targetPath}#{parts[1]}" : targetPath;
            if (key is not null && anchors.TryGetValue(key, out var nodeId))
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
