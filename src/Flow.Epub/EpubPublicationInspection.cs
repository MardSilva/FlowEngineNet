using System.Collections.Immutable;

namespace Flow.Epub;

/// <summary>Contains a non-converting structural inspection of one EPUB publication.</summary>
public sealed record EpubPublicationInspection
{
    public EpubPublicationInspection(
        string containerPath,
        EpubPackageInfo? package,
        IEnumerable<EpubManifestItemInfo> manifest,
        IEnumerable<EpubSpineItemInfo> spine,
        IEnumerable<string> navigationDocumentPaths,
        EpubResourceSummary resources,
        IEnumerable<EpubDiagnostic> diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerPath);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(spine);
        ArgumentNullException.ThrowIfNull(navigationDocumentPaths);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(diagnostics);

        ContainerPath = containerPath;
        Package = package;
        Manifest = manifest.ToImmutableArray();
        Spine = spine.ToImmutableArray();
        NavigationDocumentPaths = navigationDocumentPaths.Order(StringComparer.Ordinal).ToImmutableArray();
        Resources = resources;
        Diagnostics = diagnostics.ToImmutableArray();
    }

    public string ContainerPath { get; }

    public EpubPackageInfo? Package { get; }

    public ImmutableArray<EpubManifestItemInfo> Manifest { get; }

    public ImmutableArray<EpubSpineItemInfo> Spine { get; }

    public ImmutableArray<string> NavigationDocumentPaths { get; }

    public EpubResourceSummary Resources { get; }

    public ImmutableArray<EpubDiagnostic> Diagnostics { get; }

    public bool IsSuccess => Package is not null
        && Diagnostics.All(static diagnostic => diagnostic.Severity != EpubDiagnosticSeverity.Error);
}
