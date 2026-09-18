using System.Buffers;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Flow.Core;
using Flow.Documents;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Epub.Corpus;

/// <summary>Builds transactional, script-free material for assisted local review.</summary>
public sealed class EpubLargePublicationReviewPackageGenerator : IEpubLargePublicationReviewPackageGenerator
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UserReadingPreferences DefaultPreferences = new();
    private readonly IEpubImporter importer;
    private readonly DocumentValidator validator;
    private readonly IDocumentIntegrityService integrityService;
    private readonly ILayoutEngine layoutEngine;
    private readonly IHtmlBookPackageRenderer htmlRenderer;

    public EpubLargePublicationReviewPackageGenerator()
        : this(
            new EpubImporter(),
            new DocumentValidator(),
            new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer()),
            new AdaptiveLayoutEngine(),
            new HtmlBookPackageRenderer())
    {
    }

    public EpubLargePublicationReviewPackageGenerator(
        IEpubImporter importer,
        DocumentValidator validator,
        IDocumentIntegrityService integrityService,
        ILayoutEngine layoutEngine,
        IHtmlBookPackageRenderer htmlRenderer)
    {
        ArgumentNullException.ThrowIfNull(importer);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(integrityService);
        ArgumentNullException.ThrowIfNull(layoutEngine);
        ArgumentNullException.ThrowIfNull(htmlRenderer);
        this.importer = importer;
        this.validator = validator;
        this.integrityService = integrityService;
        this.layoutEngine = layoutEngine;
        this.htmlRenderer = htmlRenderer;
    }

    public async Task<EpubLargePublicationReviewResult> GenerateAsync(
        EpubLargePublicationCandidate candidate,
        EpubLargePublicationReviewOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateInputs(candidate, options);

        var sourceHash = await ComputeFileHashAsync(candidate.Path, cancellationToken).ConfigureAwait(false);
        if (sourceHash != options.ExpectedSourceSha256)
        {
            throw new InvalidDataException("The EPUB SHA-256 does not match the explicitly selected review candidate.");
        }

        EpubImportResult imported;
        await using (var source = new FileStream(
                         candidate.Path,
                         FileMode.Open,
                         FileAccess.Read,
                         FileShare.Read,
                         bufferSize: 81920,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            imported = await importer.ImportAsync(source, cancellationToken).ConfigureAwait(false);
        }

        var document = imported.Document
            ?? throw new InvalidDataException("The EPUB import did not produce a Flow document for review.");
        var validation = validator.Validate(document);
        if (!validation.IsValid)
        {
            var codes = string.Join(',', validation.Errors.Select(static item => item.Code).Distinct().Order(StringComparer.Ordinal));
            throw new InvalidDataException($"The imported Flow document is invalid. Diagnostics: {codes}.");
        }

        var canonical = integrityService.ComputeHash(document);
        if (!string.Equals(canonical.Algorithm, "SHA-256", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The review package requires a SHA-256 canonical document hash.");
        }

        var canonicalHash = new EpubCorpusSha256(canonical.Hash);
        var integrity = new HtmlBookIntegrity(canonical.Algorithm, canonical.Hash, canonical.CanonicalizationVersion);
        var parent = Path.GetDirectoryName(options.OutputDirectory)
            ?? throw new ArgumentException("The review output directory must have a parent directory.", nameof(options));
        Directory.CreateDirectory(parent);
        EnsureSafeDirectory(parent);
        var staging = Path.Combine(parent, $".{Path.GetFileName(options.OutputDirectory)}.{Guid.NewGuid():N}.staging");
        Directory.CreateDirectory(staging);
        try
        {
            var mobile = await RenderAndWriteAsync(
                    document,
                    new LayoutContext(390, 844, DeviceClass.Phone),
                    integrity,
                    options.UiLanguage,
                    staging,
                    "mobile",
                    cancellationToken)
                .ConfigureAwait(false);
            var desktop = await RenderAndWriteAsync(
                    document,
                    new LayoutContext(1600, 1000, DeviceClass.Desktop),
                    integrity,
                    options.UiLanguage,
                    staging,
                    "desktop",
                    cancellationToken)
                .ConfigureAwait(false);

            var samples = CreateSamples(document, mobile.Evidence, desktop.Evidence);
            var targets = CreateTargets(document, imported, mobile.Evidence, desktop.Evidence);
            var reviewItems = CreateReviewItems(samples, targets);
            var checklistBytes = SerializeChecklist(options.CandidateId, samples, targets, reviewItems);
            await File.WriteAllBytesAsync(
                    Path.Combine(staging, "review-checklist.json"),
                    checklistBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            var shortcutsBytes = CreateShortcuts(
                options.CandidateId,
                samples,
                targets,
                ReviewTextCatalog.For(options.UiLanguage, document.Metadata.Language));
            await File.WriteAllBytesAsync(
                    Path.Combine(staging, "review.html"),
                    shortcutsBytes,
                    cancellationToken)
                .ConfigureAwait(false);
            var manifestBytes = SerializeManifest(
                options.CandidateId,
                sourceHash,
                canonicalHash,
                canonical.CanonicalizationVersion,
                mobile.Hash,
                desktop.Hash,
                Hash(checklistBytes),
                Hash(shortcutsBytes),
                samples,
                targets);
            await File.WriteAllBytesAsync(
                    Path.Combine(staging, "review-manifest.json"),
                    manifestBytes,
                    cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            CommitDirectory(staging, options.OutputDirectory);
            return new EpubLargePublicationReviewResult(
                options.OutputDirectory,
                sourceHash,
                canonicalHash,
                mobile.Hash,
                desktop.Hash,
                samples,
                targets);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    private async Task<RenderedPackage> RenderAndWriteAsync(
        FlowDocument document,
        LayoutContext context,
        HtmlBookIntegrity integrity,
        HtmlBookUiLanguage uiLanguage,
        string staging,
        string directoryName,
        CancellationToken cancellationToken)
    {
        var layout = layoutEngine.Layout(document, context);
        var package = htmlRenderer.Render(
            document,
            layout,
            DefaultPreferences,
            integrity,
            new HtmlBookPackageOptions(uiLanguage),
            cancellationToken);
        var evidence = HtmlBookPackageVerifier.Verify(package, cancellationToken);
        var root = Path.Combine(staging, directoryName);
        Directory.CreateDirectory(root);
        foreach (var file in package.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, file.Content.ToArray(), cancellationToken).ConfigureAwait(false);
        }

        return new RenderedPackage(evidence, HashPackage(package));
    }

    private static ImmutableArray<EpubLargePublicationReviewSample> CreateSamples(
        FlowDocument document,
        HtmlBookPackageEvidence mobile,
        HtmlBookPackageEvidence desktop)
    {
        var chapters = document.Index.Locations
            .Select(static item => item.Node)
            .OfType<Chapter>()
            .ToArray();
        if (chapters.Length == 0)
        {
            throw new InvalidDataException("The review document has no semantic chapters to sample.");
        }

        var selected = new[]
        {
            (EpubLargePublicationSamplePosition.Beginning, chapters[0]),
            (EpubLargePublicationSamplePosition.Middle, chapters[(chapters.Length - 1) / 2]),
            (EpubLargePublicationSamplePosition.End, chapters[^1]),
        };
        return selected.Select(item => new EpubLargePublicationReviewSample(
                item.Item1,
                item.Item2.Id,
                TargetFor(item.Item2.Id, "mobile", mobile),
                TargetFor(item.Item2.Id, "desktop", desktop)))
            .ToImmutableArray();
    }

    private static ImmutableArray<EpubLargePublicationReviewTarget> CreateTargets(
        FlowDocument document,
        EpubImportResult import,
        HtmlBookPackageEvidence mobile,
        HtmlBookPackageEvidence desktop)
    {
        var targets = new List<EpubLargePublicationReviewTarget>
        {
            new("table-of-contents", null, "mobile/toc.html", "desktop/toc.html"),
        };
        AddTarget("link", FindInlineOwner<Link>(document), targets, mobile, desktop);
        AddTarget("image", document.Index.Locations.Select(static item => item.Node).OfType<Figure>().FirstOrDefault(), targets, mobile, desktop);
        AddTarget(
            "linked-image",
            document.Index.Locations.Select(static item => item.Node).OfType<Figure>()
                .FirstOrDefault(static figure => figure.Link is not null),
            targets,
            mobile,
            desktop);
        if (document.Presentation?.Cover is { } cover
            && document.Index.TryGetUniqueNode(cover.FigureId, out var coverNode))
        {
            AddTarget("cover", coverNode, targets, mobile, desktop);
        }

        AddTarget("note", document.Index.Locations.Select(static item => item.Node).OfType<Footnote>().FirstOrDefault(), targets, mobile, desktop);
        AddTarget("table", document.Index.Locations.Select(static item => item.Node).OfType<Table>().FirstOrDefault(), targets, mobile, desktop);
        AddTarget("ruby", FindInlineOwner<Ruby>(document), targets, mobile, desktop);
        AddTarget("bidirectional", FindInlineOwner<BidirectionalSpan>(document), targets, mobile, desktop);
        AddTarget(
            "math",
            document.Index.Locations.Select(static item => item.Node).OfType<MathExpression>().FirstOrDefault()
            ?? FindInlineOwner<InlineMath>(document),
            targets,
            mobile,
            desktop);
        AddTarget(
            "svg",
            document.Index.Locations.Select(static item => item.Node).OfType<Figure>().FirstOrDefault(figure =>
                document.Assets.TryGetValue(figure.AssetId, out var asset)
                && string.Equals(asset.MediaType, "image/svg+xml", StringComparison.OrdinalIgnoreCase)),
            targets,
            mobile,
            desktop);
        AddTarget("diagnostics", FindDiagnosticChapter(document, import), targets, mobile, desktop);
        return targets.OrderBy(static item => item.Category, StringComparer.Ordinal).ToImmutableArray();
    }

    private static ImmutableArray<EpubLargePublicationReviewItem> CreateReviewItems(
        ImmutableArray<EpubLargePublicationReviewSample> samples,
        ImmutableArray<EpubLargePublicationReviewTarget> targets)
    {
        var targetCategories = targets.Select(static item => item.Category).ToHashSet(StringComparer.Ordinal);
        var items = new List<EpubLargePublicationReviewItem>
        {
            Pending("chapters.order-and-presence"),
            Pending("readability.basic"),
            Pending("navigation.table-of-contents"),
            Item("navigation.links", targetCategories.Contains("link")),
            Item("media.images-and-cover", targetCategories.Contains("image") || targetCategories.Contains("cover")),
            Item("notes", targetCategories.Contains("note")),
            Item("tables", targetCategories.Contains("table")),
            Item("internationalization", targetCategories.Contains("ruby") || targetCategories.Contains("bidirectional")),
            Item("math", targetCategories.Contains("math")),
            Item("diagnostics", targetCategories.Contains("diagnostics")),
        };
        items.AddRange(samples.Select(sample => Pending($"sample.{Token(sample.Position)}")));
        return items.OrderBy(static item => item.Id, StringComparer.Ordinal).ToImmutableArray();

        static EpubLargePublicationReviewItem Pending(string id) =>
            new(id, EpubLargePublicationReviewStatus.Inconclusive);

        static EpubLargePublicationReviewItem Item(string id, bool applicable) => new(
            id,
            applicable ? EpubLargePublicationReviewStatus.Inconclusive : EpubLargePublicationReviewStatus.NotApplicable);
    }

    private static void AddTarget(
        string category,
        DocumentNode? node,
        ICollection<EpubLargePublicationReviewTarget> targets,
        HtmlBookPackageEvidence mobile,
        HtmlBookPackageEvidence desktop)
    {
        if (node is null)
        {
            return;
        }

        targets.Add(new EpubLargePublicationReviewTarget(
            category,
            node.Id,
            TargetFor(node.Id, "mobile", mobile),
            TargetFor(node.Id, "desktop", desktop)));
    }

    private static DocumentNode? FindInlineOwner<TInline>(FlowDocument document)
        where TInline : InlineNode => document.Index.Locations
        .Select(static item => item.Node)
        .FirstOrDefault(node => EnumerateInline(node).OfType<TInline>().Any());

    private static Chapter? FindDiagnosticChapter(FlowDocument document, EpubImportResult import)
    {
        if (import.SourceMap is null)
        {
            return null;
        }

        var resource = import.Diagnostics
            .Where(static item => !string.IsNullOrWhiteSpace(item.Resource))
            .GroupBy(static item => item.Resource!, StringComparer.Ordinal)
            .Select(static group => new
            {
                Resource = group.Key,
                Count = group.Sum(static item => item.Count),
            })
            .OrderByDescending(static item => item.Count)
            .ThenBy(static item => item.Resource, StringComparer.Ordinal)
            .FirstOrDefault();
        if (resource is null)
        {
            return null;
        }

        var parentByNode = document.Index.Locations.ToDictionary(
            static location => location.Node.Id,
            static location => location.Parent?.Id);
        foreach (var location in import.SourceMap.Locations
                     .Where(item => string.Equals(item.ResourcePath, resource.Resource, StringComparison.Ordinal))
                     .OrderBy(static item => item.Fragment, StringComparer.Ordinal))
        {
            var nodeId = location.NodeId;
            while (document.Index.TryGetUniqueNode(nodeId, out var node))
            {
                if (node is Chapter chapter)
                {
                    return chapter;
                }

                if (!parentByNode.TryGetValue(nodeId, out var parentId) || parentId is null)
                {
                    break;
                }

                nodeId = parentId;
            }
        }

        return null;
    }

    private static IEnumerable<InlineNode> EnumerateInline(DocumentNode node)
    {
        var roots = node switch
        {
            Heading heading => heading.Content,
            Paragraph paragraph => paragraph.Content,
            Caption caption => caption.Content,
            TableOfContents toc => toc.Title.Concat(toc.Entries.SelectMany(static entry => entry.Label)),
            _ => [],
        };
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in EnumerateInlineChildren(root))
            {
                yield return child;
            }
        }
    }

    private static IEnumerable<InlineNode> EnumerateInlineChildren(InlineNode node)
    {
        var children = node switch
        {
            InlineContainerNode container => container.Children,
            FootnoteReference reference => reference.Label,
            _ => [],
        };
        foreach (var child in children)
        {
            yield return child;
            foreach (var descendant in EnumerateInlineChildren(child))
            {
                yield return descendant;
            }
        }
    }

    private static string TargetFor(NodeId id, string root, HtmlBookPackageEvidence evidence)
    {
        var paths = evidence.PathsById.GetValueOrDefault(id.Value, []);
        if (paths.Length != 1)
        {
            throw new InvalidDataException("A sampled semantic ID does not resolve to one HTML review page.");
        }

        return $"{root}/{paths[0]}#{Uri.EscapeDataString(id.Value)}";
    }

    private static byte[] SerializeChecklist(
        EpubCorpusPublicationId candidateId,
        IEnumerable<EpubLargePublicationReviewSample> samples,
        IEnumerable<EpubLargePublicationReviewTarget> targets,
        IEnumerable<EpubLargePublicationReviewItem> items) => WriteJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("format", "flow-epub-large-review-checklist-0.1");
        writer.WriteString("reviewKind", "human");
        writer.WriteString("candidateId", candidateId.Value);
        WriteSamples(writer, samples);
        WriteTargets(writer, targets);
        writer.WriteStartArray("items");
        foreach (var item in items.OrderBy(static item => item.Id, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("id", item.Id);
            writer.WriteString("status", Token(item.Status));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    private static byte[] SerializeManifest(
        EpubCorpusPublicationId candidateId,
        EpubCorpusSha256 sourceHash,
        EpubCorpusSha256 canonicalHash,
        string canonicalizationVersion,
        EpubCorpusSha256 mobileHash,
        EpubCorpusSha256 desktopHash,
        EpubCorpusSha256 checklistHash,
        EpubCorpusSha256 shortcutsHash,
        IEnumerable<EpubLargePublicationReviewSample> samples,
        IEnumerable<EpubLargePublicationReviewTarget> targets) => WriteJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("format", "flow-epub-large-review-manifest-0.1");
        writer.WriteString("candidateId", candidateId.Value);
        writer.WriteString("sourceEpubSha256", sourceHash.Value);
        writer.WriteString("canonicalDocumentSha256", canonicalHash.Value);
        writer.WriteString("canonicalizationVersion", canonicalizationVersion);
        writer.WriteString("mobilePackageSha256", mobileHash.Value);
        writer.WriteString("desktopPackageSha256", desktopHash.Value);
        writer.WriteString("checklistTemplateSha256", checklistHash.Value);
        writer.WriteString("shortcutsSha256", shortcutsHash.Value);
        WriteSamples(writer, samples);
        WriteTargets(writer, targets);
        writer.WriteEndObject();
    });

    private static void WriteSamples(Utf8JsonWriter writer, IEnumerable<EpubLargePublicationReviewSample> samples)
    {
        writer.WriteStartArray("samples");
        foreach (var sample in samples.OrderBy(static item => item.Position))
        {
            writer.WriteStartObject();
            writer.WriteString("position", Token(sample.Position));
            writer.WriteString("chapterId", sample.ChapterId.Value);
            writer.WriteString("mobileTarget", sample.MobileTarget);
            writer.WriteString("desktopTarget", sample.DesktopTarget);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void WriteTargets(Utf8JsonWriter writer, IEnumerable<EpubLargePublicationReviewTarget> targets)
    {
        writer.WriteStartArray("targets");
        foreach (var target in targets.OrderBy(static item => item.Category, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("category", target.Category);
            if (target.NodeId is { } id)
            {
                writer.WriteString("nodeId", id.Value);
            }
            else
            {
                writer.WriteNull("nodeId");
            }

            writer.WriteString("mobileTarget", target.MobileTarget);
            writer.WriteString("desktopTarget", target.DesktopTarget);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static byte[] CreateShortcuts(
        EpubCorpusPublicationId candidateId,
        IEnumerable<EpubLargePublicationReviewSample> samples,
        IEnumerable<EpubLargePublicationReviewTarget> targets,
        ReviewTextCatalog text)
    {
        var builder = new StringBuilder();
        builder.Append("<!doctype html>\n<html lang=\"").Append(text.LanguageTag)
            .Append("\"><head><meta charset=\"utf-8\"><title>")
            .Append(HtmlEncoder.Default.Encode(text.Get("Title")))
            .Append("</title></head><body>\n<h1>")
            .Append(HtmlEncoder.Default.Encode(text.Get("Title")))
            .Append("</h1><p>")
            .Append(HtmlEncoder.Default.Encode(text.Get("Candidate")))
            .Append(" <code>")
            .Append(HtmlEncoder.Default.Encode(candidateId.Value))
            .Append("</code></p>\n");
        builder.Append("<p>").Append(HtmlEncoder.Default.Encode(text.Get("Instructions")))
            .Append("</p>\n<h2>").Append(HtmlEncoder.Default.Encode(text.Get("ChapterSamples"))).Append("</h2><ul>\n");
        foreach (var sample in samples.OrderBy(static item => item.Position))
        {
            AppendLinks(builder, text.Label(Token(sample.Position)), sample.MobileTarget, sample.DesktopTarget, text);
        }

        builder.Append("</ul><h2>").Append(HtmlEncoder.Default.Encode(text.Get("RepresentativeFeatures")))
            .Append("</h2><ul>\n");
        foreach (var target in targets.OrderBy(static item => item.Category, StringComparer.Ordinal))
        {
            AppendLinks(builder, text.Label(target.Category), target.MobileTarget, target.DesktopTarget, text);
        }

        builder.Append("</ul></body></html>\n");
        return Utf8WithoutBom.GetBytes(builder.ToString());

        static void AppendLinks(
            StringBuilder builder,
            string label,
            string mobile,
            string desktop,
            ReviewTextCatalog text)
        {
            builder.Append("<li>").Append(HtmlEncoder.Default.Encode(label)).Append(": <a href=\"")
                .Append(HtmlEncoder.Default.Encode(mobile)).Append("\">")
                .Append(HtmlEncoder.Default.Encode(text.Get("Mobile"))).Append("</a> | <a href=\"")
                .Append(HtmlEncoder.Default.Encode(desktop)).Append("\">")
                .Append(HtmlEncoder.Default.Encode(text.Get("Desktop"))).Append("</a></li>\n");
        }
    }

    private static byte[] WriteJson(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            write(writer);
            writer.Flush();
        }

        var normalized = new ArrayBufferWriter<byte>(buffer.WrittenCount + 1);
        foreach (var value in buffer.WrittenSpan)
        {
            if (value != (byte)'\r')
            {
                normalized.GetSpan(1)[0] = value;
                normalized.Advance(1);
            }
        }

        normalized.GetSpan(1)[0] = (byte)'\n';
        normalized.Advance(1);
        return normalized.WrittenSpan.ToArray();
    }

    private static EpubCorpusSha256 HashPackage(HtmlBookPackage package)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in package.Files.OrderBy(static item => item.Path, StringComparer.Ordinal))
        {
            Append(hash, file.Path);
            Append(hash, file.MediaType);
            hash.AppendData(file.Content.AsSpan());
            hash.AppendData([0]);
        }

        return new EpubCorpusSha256(Convert.ToHexString(hash.GetHashAndReset()));

        static void Append(IncrementalHash hash, string value)
        {
            hash.AppendData(Utf8WithoutBom.GetBytes(value));
            hash.AppendData([0]);
        }
    }

    private static EpubCorpusSha256 Hash(ReadOnlySpan<byte> bytes) =>
        new(Convert.ToHexString(SHA256.HashData(bytes)));

    private static async Task<EpubCorpusSha256> ComputeFileHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return new EpubCorpusSha256(Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)));
    }

    private static void ValidateInputs(
        EpubLargePublicationCandidate candidate,
        EpubLargePublicationReviewOptions options)
    {
        if (candidate.Id != options.CandidateId)
        {
            throw new ArgumentException("The review candidate ID does not match the selected options.", nameof(candidate));
        }

        if (!candidate.LegalUseDeclared || !candidate.DrmFreeDeclared)
        {
            throw new InvalidOperationException("Local review requires declared legal use and a DRM-free publication.");
        }

        var source = new FileInfo(candidate.Path);
        if (!source.Exists || source.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new FileNotFoundException("The local review candidate is unavailable or uses an unsafe reparse point.");
        }

        if (IsInside(options.RepositoryRoot, options.OutputDirectory))
        {
            throw new ArgumentException("Licensed review artifacts cannot be written inside the repository.", nameof(options));
        }

        if (IsInside(options.OutputDirectory, candidate.Path))
        {
            throw new ArgumentException("The review destination cannot contain the source EPUB.", nameof(options));
        }

        if (Path.GetDirectoryName(options.OutputDirectory) is null)
        {
            throw new ArgumentException("A file-system root cannot be used as the review destination.", nameof(options));
        }

        if (Directory.Exists(options.OutputDirectory))
        {
            EnsureSafeDirectory(options.OutputDirectory);
            EnsureExistingReviewDestination(options.OutputDirectory);
        }
    }

    private static void EnsureExistingReviewDestination(string outputDirectory)
    {
        var manifestPath = Path.Combine(outputDirectory, "review-manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new IOException("An existing destination can be replaced only when it is a Flow review directory.");
        }

        try
        {
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            if (manifest.RootElement.GetProperty("format").GetString() != "flow-epub-large-review-manifest-0.1")
            {
                throw new IOException("The existing review destination uses an unknown manifest format.");
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new IOException("The existing review destination has an invalid manifest.", exception);
        }
    }

    private static void EnsureSafeDirectory(string path)
    {
        for (var current = new DirectoryInfo(Path.GetFullPath(path)); current is not null; current = current.Parent)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new IOException("The review output path contains an unsafe reparse point.");
            }
        }
    }

    private static bool IsInside(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(normalizedRoot, Path.TrimEndingDirectorySeparator(normalizedPath), comparison)
            || normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static void CommitDirectory(string staging, string destination)
    {
        var parent = Path.GetDirectoryName(destination)!;
        var backup = Path.Combine(parent, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.backup");
        var hadDestination = Directory.Exists(destination);
        try
        {
            if (hadDestination)
            {
                Directory.Move(destination, backup);
            }

            Directory.Move(staging, destination);
            if (Directory.Exists(backup))
            {
                Directory.Delete(backup, recursive: true);
            }
        }
        catch
        {
            if (!Directory.Exists(destination) && Directory.Exists(backup))
            {
                Directory.Move(backup, destination);
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(backup) && Directory.Exists(destination))
            {
                Directory.Delete(backup, recursive: true);
            }
        }
    }

    private static string Token<T>(T value)
        where T : struct, Enum => string.Concat(value.ToString().Select((character, index) =>
        char.IsUpper(character) && index > 0
            ? $"-{char.ToLowerInvariant(character)}"
            : char.ToLowerInvariant(character).ToString()));

    private sealed record RenderedPackage(HtmlBookPackageEvidence Evidence, EpubCorpusSha256 Hash);
}
