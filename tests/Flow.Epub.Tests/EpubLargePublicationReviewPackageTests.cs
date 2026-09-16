using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flow.Documents;
using Flow.Epub.Corpus;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Epub.Tests;

public sealed class EpubLargePublicationReviewPackageTests
{
    [Fact]
    public async Task ProjectFixture_ProducesTransactionalPackagesChecklistSamplesAndShortcuts()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture(20);
        var candidate = workspace.AddCandidate("review-fixture", bytes);
        var output = Path.Combine(workspace.Path, "review-output");
        var generator = CreateGenerator();

        var result = await generator.GenerateAsync(candidate, Options(candidate, bytes, output, workspace.RepositoryRoot));

        Assert.Equal(Path.GetFullPath(output), result.OutputDirectory);
        Assert.True(File.Exists(Path.Combine(output, "mobile", "index.html")));
        Assert.True(File.Exists(Path.Combine(output, "desktop", "index.html")));
        Assert.True(File.Exists(Path.Combine(output, "review.html")));
        Assert.Equal(
            new[]
            {
                EpubLargePublicationSamplePosition.Beginning,
                EpubLargePublicationSamplePosition.Middle,
                EpubLargePublicationSamplePosition.End,
            },
            result.Samples.Select(static item => item.Position));
        Assert.All(result.Samples, static sample =>
        {
            Assert.StartsWith("mobile/chapters/", sample.MobileTarget, StringComparison.Ordinal);
            Assert.StartsWith("desktop/chapters/", sample.DesktopTarget, StringComparison.Ordinal);
            Assert.Contains($"#{sample.ChapterId.Value}", sample.MobileTarget, StringComparison.Ordinal);
        });
        Assert.Contains(result.Targets, static item => item.Category == "table-of-contents");
        Assert.Contains(result.Targets, static item => item.Category == "link");
        Assert.Contains(result.Targets, static item => item.Category == "image");
        Assert.Contains(result.Targets, static item => item.Category == "note");
        Assert.Contains(result.Targets, static item => item.Category == "table");

        var checklistBytes = await File.ReadAllBytesAsync(Path.Combine(output, "review-checklist.json"));
        Assert.False(checklistBytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.DoesNotContain('\r', Encoding.UTF8.GetString(checklistBytes));
        using var checklist = JsonDocument.Parse(checklistBytes);
        Assert.Equal("human", checklist.RootElement.GetProperty("reviewKind").GetString());
        Assert.All(
            checklist.RootElement.GetProperty("items").EnumerateArray(),
            static item => Assert.Contains(
                item.GetProperty("status").GetString(),
                new[] { "inconclusive", "not-applicable" }));

        var manifestText = await File.ReadAllTextAsync(Path.Combine(output, "review-manifest.json"));
        Assert.Contains(result.SourceEpubSha256.Value, manifestText, StringComparison.Ordinal);
        Assert.Contains(result.CanonicalDocumentSha256.Value, manifestText, StringComparison.Ordinal);
        Assert.Contains(result.MobilePackageSha256.Value, manifestText, StringComparison.Ordinal);
        Assert.Contains(result.DesktopPackageSha256.Value, manifestText, StringComparison.Ordinal);
        Assert.DoesNotContain(candidate.Path, manifestText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Conteúdo determinístico", manifestText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RepeatedGeneration_ProducesIdenticalFilesWithoutMachinePaths()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture(20);
        var candidate = workspace.AddCandidate("deterministic-review", bytes);
        var first = Path.Combine(workspace.Path, "first");
        var second = Path.Combine(workspace.Path, "second");
        var generator = CreateGenerator();

        await generator.GenerateAsync(candidate, Options(candidate, bytes, first, workspace.RepositoryRoot));
        await generator.GenerateAsync(candidate, Options(candidate, bytes, second, workspace.RepositoryRoot));

        var firstFiles = ReadTree(first);
        var secondFiles = ReadTree(second);
        Assert.Equal(firstFiles.Keys, secondFiles.Keys);
        Assert.All(firstFiles, pair => Assert.Equal(pair.Value, secondFiles[pair.Key]));
        Assert.DoesNotContain(
            firstFiles.Where(static pair => pair.Key.EndsWith(".json", StringComparison.Ordinal))
                .Select(static pair => Encoding.UTF8.GetString(pair.Value)),
            text => text.Contains(workspace.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RendererFailure_PreservesPreviousDestinationAndRemovesStagingDirectory()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture(20);
        var candidate = workspace.AddCandidate("failed-review", bytes);
        var output = Path.Combine(workspace.Path, "existing-review");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "previous.txt"), "keep");
        await WriteReviewMarkerAsync(output);
        var generator = CreateGenerator(new FailOnSecondRenderRenderer());

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            generator.GenerateAsync(candidate, Options(candidate, bytes, output, workspace.RepositoryRoot)));

        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(output, "previous.txt")));
        Assert.Equal(2, Directory.EnumerateFileSystemEntries(output).Count());
        Assert.Empty(Directory.EnumerateDirectories(workspace.Path, "*.staging", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Cancellation_PreservesPreviousDestinationAndRemovesStagingDirectory()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture(20);
        var candidate = workspace.AddCandidate("cancelled-review", bytes);
        var output = Path.Combine(workspace.Path, "existing-review");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "previous.txt"), "keep");
        await WriteReviewMarkerAsync(output);
        using var cancellation = new CancellationTokenSource();
        var generator = CreateGenerator(new CancelOnSecondRenderRenderer(cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            generator.GenerateAsync(
                candidate,
                Options(candidate, bytes, output, workspace.RepositoryRoot),
                cancellation.Token));

        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(output, "previous.txt")));
        Assert.Equal(2, Directory.EnumerateFileSystemEntries(output).Count());
        Assert.Empty(Directory.EnumerateDirectories(workspace.Path, "*.staging", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task OutputInsideProtectedRepository_IsRejectedWithoutWritingArtifacts()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture(20);
        var candidate = workspace.AddCandidate("unsafe-output", bytes);
        var protectedRoot = Path.Combine(workspace.Path, "repository");
        Directory.CreateDirectory(protectedRoot);
        var output = Path.Combine(protectedRoot, "licensed-review");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateGenerator().GenerateAsync(candidate, Options(candidate, bytes, output, protectedRoot)));

        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public void RelativeOutputPath_IsRejected()
    {
        using var workspace = new TemporaryWorkspace();

        Assert.Throws<ArgumentException>(() => new EpubLargePublicationReviewOptions(
            new EpubCorpusPublicationId("relative-review"),
            new EpubCorpusSha256(new string('A', 64)),
            "relative-output",
            workspace.RepositoryRoot));
    }

    [Fact]
    public async Task ExistingUnrelatedDirectory_IsRejectedWithoutReplacingItsContent()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture(20);
        var candidate = workspace.AddCandidate("unrelated-output", bytes);
        var output = Path.Combine(workspace.Path, "personal-files");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "keep.txt"), "keep");

        await Assert.ThrowsAsync<IOException>(() =>
            CreateGenerator().GenerateAsync(candidate, Options(candidate, bytes, output, workspace.RepositoryRoot)));

        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(output, "keep.txt")));
        Assert.Single(Directory.EnumerateFileSystemEntries(output));
    }

    [Fact]
    public async Task SuccessfulGeneration_ReplacesOnlyARecognizedPreviousReviewDirectory()
    {
        using var workspace = new TemporaryWorkspace();
        var bytes = EpubCorpusFixtureFactory.CreateLargeGateFixture(20);
        var candidate = workspace.AddCandidate("replace-review", bytes);
        var output = Path.Combine(workspace.Path, "replaceable-review");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "obsolete.txt"), "remove after success");
        await WriteReviewMarkerAsync(output);

        await CreateGenerator().GenerateAsync(candidate, Options(candidate, bytes, output, workspace.RepositoryRoot));

        Assert.False(File.Exists(Path.Combine(output, "obsolete.txt")));
        Assert.True(File.Exists(Path.Combine(output, "review-manifest.json")));
        Assert.Empty(Directory.EnumerateDirectories(workspace.Path, "*.backup", SearchOption.TopDirectoryOnly));
    }

    private static EpubLargePublicationReviewPackageGenerator CreateGenerator(
        IHtmlBookPackageRenderer? renderer = null) => new(
        new EpubImporter(),
        new DocumentValidator(),
        new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer()),
        new AdaptiveLayoutEngine(),
        renderer ?? new HtmlBookPackageRenderer());

    private static EpubLargePublicationReviewOptions Options(
        EpubLargePublicationCandidate candidate,
        byte[] bytes,
        string output,
        string repositoryRoot) => new(
        candidate.Id,
        new EpubCorpusSha256(Convert.ToHexString(SHA256.HashData(bytes))),
        output,
        repositoryRoot,
        HtmlBookUiLanguage.PortugueseBrazil);

    private static SortedDictionary<string, byte[]> ReadTree(string root) => Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToSortedDictionary(
            path => Path.GetRelativePath(root, path).Replace('\\', '/'),
            File.ReadAllBytes,
            StringComparer.Ordinal);

    private static Task WriteReviewMarkerAsync(string output) => File.WriteAllTextAsync(
        Path.Combine(output, "review-manifest.json"),
        "{\"format\":\"flow-epub-large-review-manifest-0.1\"}");

    private sealed class FailOnSecondRenderRenderer : IHtmlBookPackageRenderer
    {
        private readonly HtmlBookPackageRenderer inner = new();
        private int calls;

        public HtmlBookPackage Render(
            FlowDocument document,
            LayoutDocument layout,
            UserReadingPreferences userPreferences,
            HtmlBookIntegrity integrity,
            HtmlBookPackageOptions options,
            CancellationToken cancellationToken)
        {
            if (++calls == 2)
            {
                throw new InvalidDataException("Injected desktop rendering failure.");
            }

            return inner.Render(document, layout, userPreferences, integrity, options, cancellationToken);
        }

        public HtmlBookPackage Render(
            FlowDocument document,
            LayoutDocument layout,
            UserReadingPreferences userPreferences,
            HtmlBookIntegrity integrity) => inner.Render(document, layout, userPreferences, integrity);
    }

    private sealed class CancelOnSecondRenderRenderer(CancellationTokenSource cancellation) : IHtmlBookPackageRenderer
    {
        private readonly HtmlBookPackageRenderer inner = new();
        private int calls;

        public HtmlBookPackage Render(
            FlowDocument document,
            LayoutDocument layout,
            UserReadingPreferences userPreferences,
            HtmlBookIntegrity integrity,
            HtmlBookPackageOptions options,
            CancellationToken cancellationToken)
        {
            if (++calls == 2)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return inner.Render(document, layout, userPreferences, integrity, options, cancellationToken);
        }

        public HtmlBookPackage Render(
            FlowDocument document,
            LayoutDocument layout,
            UserReadingPreferences userPreferences,
            HtmlBookIntegrity integrity) => inner.Render(document, layout, userPreferences, integrity);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"flow-review-{Guid.NewGuid():N}");
            RepositoryRoot = System.IO.Path.Combine(Path, "protected-repository");
            Directory.CreateDirectory(RepositoryRoot);
        }

        public string Path { get; }

        public string RepositoryRoot { get; }

        public EpubLargePublicationCandidate AddCandidate(string id, byte[] bytes)
        {
            var path = System.IO.Path.Combine(Path, $"{id}.epub");
            File.WriteAllBytes(path, bytes);
            return new EpubLargePublicationCandidate(new EpubCorpusPublicationId(id), path, true, true);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

internal static class DictionaryTestExtensions
{
    internal static SortedDictionary<TKey, TValue> ToSortedDictionary<TSource, TKey, TValue>(
        this IEnumerable<TSource> source,
        Func<TSource, TKey> keySelector,
        Func<TSource, TValue> valueSelector,
        IComparer<TKey> comparer)
        where TKey : notnull
    {
        var result = new SortedDictionary<TKey, TValue>(comparer);
        foreach (var item in source)
        {
            result.Add(keySelector(item), valueSelector(item));
        }

        return result;
    }
}
