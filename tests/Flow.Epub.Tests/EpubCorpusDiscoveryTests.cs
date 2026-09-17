using System.Security.Cryptography;
using System.Text;
using Flow.Epub;

namespace Flow.Epub.Tests;

public sealed class EpubCorpusDiscoveryTests
{
    [Fact]
    public async Task DiscoverAsync_FindsVersionedFixtureCopiesItAndDeletesWorkspaceAfterDispose()
    {
        using var workspace = new DiscoveryWorkspace();
        var bytes = "project fixture"u8.ToArray();
        var publication = CreatePublication(
            "fixture",
            bytes,
            EpubCorpusPublicationKind.ProjectFixture,
            EpubCorpusRedistribution.Allowed,
            "fixtures/book.epub");
        var sourcePath = workspace.WriteRepositoryFile("fixtures/book.epub", bytes);
        var service = workspace.CreateService();

        var session = await service.DiscoverAsync(Manifest(publication), workspace.Options());
        var temporaryDirectory = session.TemporaryDirectory;
        var item = Assert.Single(session.Report.Items);

        Assert.Equal(EpubCorpusDiscoveryStatus.Available, item.Status);
        Assert.Equal(EpubCorpusDiscoverySource.RepositoryFixture, item.Source);
        Assert.Equal(EpubCorpusMatchKind.RelativePath, item.MatchKind);
        File.Delete(sourcePath);
        await using (var isolated = session.OpenRead(publication.Id))
        {
            using var copy = new MemoryStream();
            await isolated.CopyToAsync(copy);
            Assert.Equal(bytes, copy.ToArray());
        }

        Assert.True(Directory.Exists(temporaryDirectory));
        await session.DisposeAsync();
        Assert.False(Directory.Exists(temporaryDirectory));
        Assert.Throws<ObjectDisposedException>(() => session.OpenRead(publication.Id));
    }

    [Fact]
    public async Task DiscoverAsync_PrivatePublicationWithoutConfiguredDirectoryIsMissingNotFailed()
    {
        using var workspace = new DiscoveryWorkspace();
        var publication = CreatePublication(
            "private-book",
            "private"u8.ToArray(),
            EpubCorpusPublicationKind.LocalNonRedistributablePublication,
            EpubCorpusRedistribution.Prohibited,
            "private/private.epub");

        await using var session = await workspace.CreateService().DiscoverAsync(
            Manifest(publication),
            workspace.Options(externalRoot: null));

        var item = Assert.Single(session.Report.Items);
        Assert.Equal(EpubCorpusDiscoveryStatus.Missing, item.Status);
        Assert.Equal(EpubCorpusDiagnosticSeverity.Warning, Assert.Single(item.Diagnostics).Severity);
    }

    [Fact]
    public async Task DiscoverAsync_UnavailableExternalDirectoryIsReportedWithoutExposingItsPath()
    {
        using var workspace = new DiscoveryWorkspace();
        var unavailable = Path.Combine(workspace.Root, "person-name", "secret-library");
        var publication = CreatePublication(
            "unavailable",
            "private"u8.ToArray(),
            EpubCorpusPublicationKind.LocalNonRedistributablePublication,
            EpubCorpusRedistribution.Prohibited,
            "private/unavailable.epub");

        await using var session = await workspace.CreateService().DiscoverAsync(
            Manifest(publication),
            workspace.Options(unavailable));
        var bytes = WriteReport(session.Report);
        var reportText = Encoding.UTF8.GetString(bytes);

        Assert.Equal(EpubCorpusDiscoveryStatus.Missing, Assert.Single(session.Report.Items).Status);
        Assert.Contains(EpubCorpusDiscoveryDiagnosticCodes.ExternalDirectoryUnavailable, reportText, StringComparison.Ordinal);
        Assert.DoesNotContain(unavailable, reportText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("person-name", reportText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiscoverAsync_IdCandidateWithWrongHashIsHashMismatch()
    {
        using var workspace = new DiscoveryWorkspace();
        var expected = "expected"u8.ToArray();
        var publication = CreatePublication(
            "known-id",
            expected,
            EpubCorpusPublicationKind.LocalNonRedistributablePublication,
            EpubCorpusRedistribution.Prohibited,
            "private/expected.epub");
        workspace.WriteExternalFile("known-id.epub", "different"u8.ToArray());

        await using var session = await workspace.CreateService().DiscoverAsync(
            Manifest(publication),
            workspace.Options(workspace.ExternalRoot));

        var item = Assert.Single(session.Report.Items);
        Assert.Equal(EpubCorpusDiscoveryStatus.HashMismatch, item.Status);
        Assert.Contains(item.Diagnostics, static diagnostic => diagnostic.Code == EpubCorpusDiscoveryDiagnosticCodes.HashMismatch);
    }

    [Fact]
    public async Task DiscoverAsync_MatchesExternalPublicationByHashRegardlessOfFileName()
    {
        using var workspace = new DiscoveryWorkspace();
        var bytes = "licensed local publication"u8.ToArray();
        var publication = CreatePublication(
            "stable-id",
            bytes,
            EpubCorpusPublicationKind.LocalNonRedistributablePublication,
            EpubCorpusRedistribution.Prohibited,
            "private/original-name.epub");
        var source = workspace.WriteExternalFile("renamed/by-the-reader.epub", bytes);

        await using var session = await workspace.CreateService().DiscoverAsync(
            Manifest(publication),
            workspace.Options(workspace.ExternalRoot));
        File.Delete(source);

        var item = Assert.Single(session.Report.Items);
        Assert.Equal(EpubCorpusDiscoveryStatus.Available, item.Status);
        Assert.Equal(EpubCorpusDiscoverySource.ExternalCorpus, item.Source);
        Assert.Equal(EpubCorpusMatchKind.Sha256, item.MatchKind);
        await using var isolated = session.OpenRead(publication.Id);
        using var copy = new MemoryStream();
        await isolated.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
        Assert.False(IsWithin(workspace.RepositoryRoot, session.TemporaryDirectory));
        Assert.False(IsWithin(workspace.ExternalRoot, session.TemporaryDirectory));
    }

    [Fact]
    public async Task DiscoverAsync_MatchesExternalPublicationByStableIdBeforeHashScan()
    {
        using var workspace = new DiscoveryWorkspace();
        var bytes = "stable ID"u8.ToArray();
        var publication = CreatePublication(
            "stable-id",
            bytes,
            EpubCorpusPublicationKind.RedistributablePublication,
            EpubCorpusRedistribution.Allowed,
            "public/original.epub");
        workspace.WriteExternalFile("stable-id.epub", bytes);

        await using var session = await workspace.CreateService().DiscoverAsync(
            Manifest(publication),
            workspace.Options(workspace.ExternalRoot));

        Assert.Equal(EpubCorpusMatchKind.PublicationId, Assert.Single(session.Report.Items).MatchKind);
    }

    [Fact]
    public async Task DiscoverAsync_RedistributablePublicationCanFallBackFromStaleRepositoryCopyToExternalHash()
    {
        using var workspace = new DiscoveryWorkspace();
        var expected = "current public edition"u8.ToArray();
        var publication = CreatePublication(
            "public-edition",
            expected,
            EpubCorpusPublicationKind.RedistributablePublication,
            EpubCorpusRedistribution.Allowed,
            "public/book.epub");
        workspace.WriteRepositoryFile("public/book.epub", "stale edition"u8.ToArray());
        workspace.WriteExternalFile("renamed.epub", expected);

        await using var session = await workspace.CreateService().DiscoverAsync(
            Manifest(publication),
            workspace.Options(workspace.ExternalRoot));

        var item = Assert.Single(session.Report.Items);
        Assert.Equal(EpubCorpusDiscoveryStatus.Available, item.Status);
        Assert.Equal(EpubCorpusDiscoverySource.ExternalCorpus, item.Source);
        Assert.Equal(EpubCorpusMatchKind.Sha256, item.MatchKind);
    }

    [Fact]
    public async Task DiscoverAsync_RejectsInconsistentLicenseClassification()
    {
        using var workspace = new DiscoveryWorkspace();
        var publication = CreatePublication(
            "bad-license",
            "content"u8.ToArray(),
            EpubCorpusPublicationKind.LocalNonRedistributablePublication,
            EpubCorpusRedistribution.Allowed,
            "private/book.epub");

        await using var session = await workspace.CreateService().DiscoverAsync(
            Manifest(publication),
            workspace.Options(workspace.ExternalRoot));

        Assert.Equal(EpubCorpusDiscoveryStatus.LicenseRejected, Assert.Single(session.Report.Items).Status);
    }

    [Fact]
    public async Task DiscoverAsync_RejectsPrivateCorpusStoredInsideRepository()
    {
        using var workspace = new DiscoveryWorkspace();
        var privateRoot = Directory.CreateDirectory(Path.Combine(workspace.RepositoryRoot, "private-corpus")).FullName;
        var publication = CreatePublication(
            "private-in-repository",
            "content"u8.ToArray(),
            EpubCorpusPublicationKind.LocalNonRedistributablePublication,
            EpubCorpusRedistribution.Prohibited,
            "private/book.epub");

        await using var session = await workspace.CreateService().DiscoverAsync(
            Manifest(publication),
            workspace.Options(privateRoot));

        Assert.Equal(EpubCorpusDiscoveryStatus.LicenseRejected, Assert.Single(session.Report.Items).Status);
    }

    [Theory]
    [InlineData("../outside.epub")]
    [InlineData("fixtures/%2E%2E/outside.epub")]
    [InlineData("fixtures/%5C..%5Coutside.epub")]
    [InlineData("fixtures/%ZZ/outside.epub")]
    [InlineData("C:/books/outside.epub")]
    public async Task DiscoverAsync_RejectsUnsafeProgrammaticCatalogPath(string relativePath)
    {
        using var workspace = new DiscoveryWorkspace();
        var publication = CreatePublication(
            "unsafe-path",
            "content"u8.ToArray(),
            EpubCorpusPublicationKind.ProjectFixture,
            EpubCorpusRedistribution.Allowed,
            relativePath);

        await using var session = await workspace.CreateService().DiscoverAsync(
            Manifest(publication),
            workspace.Options());

        Assert.Equal(EpubCorpusDiscoveryStatus.UnsafePath, Assert.Single(session.Report.Items).Status);
    }

    [Fact]
    public async Task DiscoverAsync_RejectsSymbolicLinkWhenPlatformAllowsCreatingIt()
    {
        using var workspace = new DiscoveryWorkspace();
        var bytes = "link target"u8.ToArray();
        var target = workspace.WriteExternalFile("target.bin", bytes);
        var link = Path.Combine(workspace.ExternalRoot, "linked.epub");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        var publication = CreatePublication(
            "linked",
            bytes,
            EpubCorpusPublicationKind.LocalNonRedistributablePublication,
            EpubCorpusRedistribution.Prohibited,
            "private/linked.epub");

        await using var session = await workspace.CreateService().DiscoverAsync(
            Manifest(publication),
            workspace.Options(workspace.ExternalRoot));

        Assert.Equal(EpubCorpusDiscoveryStatus.UnsafePath, Assert.Single(session.Report.Items).Status);
    }

    [Fact]
    public async Task DiscoverAsync_CancellationDeletesTemporaryWorkspace()
    {
        using var workspace = new DiscoveryWorkspace();
        var service = workspace.CreateService();
        var expectedTemporaryDirectory = workspace.NextTemporaryDirectory;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.DiscoverAsync(
            Manifest(CreatePublication(
                "cancelled",
                "content"u8.ToArray(),
                EpubCorpusPublicationKind.ProjectFixture,
                EpubCorpusRedistribution.Allowed,
                "fixtures/book.epub")),
            workspace.Options(),
            cancellation.Token));

        Assert.False(Directory.Exists(expectedTemporaryDirectory));
    }

    [Fact]
    public async Task DiscoverAsync_FailedWorkspaceValidationDeletesTemporaryWorkspace()
    {
        using var workspace = new DiscoveryWorkspace(createTemporaryInsideRepository: true);
        var service = workspace.CreateService();
        var expectedTemporaryDirectory = workspace.NextTemporaryDirectory;

        await Assert.ThrowsAsync<IOException>(() => service.DiscoverAsync(
            Manifest(CreatePublication(
                "failed",
                "content"u8.ToArray(),
                EpubCorpusPublicationKind.ProjectFixture,
                EpubCorpusRedistribution.Allowed,
                "fixtures/book.epub")),
            workspace.Options()));

        Assert.False(Directory.Exists(expectedTemporaryDirectory));
    }

    [Fact]
    public void DiscoveryReport_IsDeterministicUtf8AndContainsNoPhysicalPaths()
    {
        var missing = new EpubCorpusDiscoveryItem(
            new EpubCorpusPublicationId("book-b"),
            EpubCorpusDiscoveryStatus.Missing,
            diagnostics:
            [
                new EpubCorpusDiagnostic(
                    EpubCorpusDiscoveryDiagnosticCodes.Missing,
                    EpubCorpusDiagnosticSeverity.Warning,
                    "No external publication matched.",
                    "publications/book-b"),
            ]);
        var available = new EpubCorpusDiscoveryItem(
            new EpubCorpusPublicationId("book-a"),
            EpubCorpusDiscoveryStatus.Available,
            EpubCorpusDiscoverySource.ExternalCorpus,
            EpubCorpusMatchKind.Sha256);

        var first = WriteReport(new EpubCorpusDiscoveryReport([missing, available]));
        var second = WriteReport(new EpubCorpusDiscoveryReport([available, missing]));
        var text = Encoding.UTF8.GetString(first);

        Assert.Equal(first, second);
        Assert.True(text.IndexOf("book-a", StringComparison.Ordinal) < text.IndexOf("book-b", StringComparison.Ordinal));
        Assert.DoesNotContain('\r', text);
        Assert.Equal((byte)'\n', first[^1]);
        Assert.False(first.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.DoesNotContain("path", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DiscoverAsync_ReportsBoundedExternalSearch()
    {
        using var workspace = new DiscoveryWorkspace();
        workspace.WriteExternalFile("one.epub", "one"u8.ToArray());
        workspace.WriteExternalFile("two.epub", "two"u8.ToArray());
        var publication = CreatePublication(
            "not-present",
            "expected"u8.ToArray(),
            EpubCorpusPublicationKind.LocalNonRedistributablePublication,
            EpubCorpusRedistribution.Prohibited,
            "private/not-present.epub");

        await using var session = await workspace.CreateService().DiscoverAsync(
            Manifest(publication),
            workspace.Options(workspace.ExternalRoot, maximumCandidateFiles: 1));

        Assert.Equal(EpubCorpusDiscoveryStatus.SearchLimitExceeded, Assert.Single(session.Report.Items).Status);
    }

    [Fact]
    public void Options_FromEnvironmentReadsOnlyTheDocumentedLocalDirectorySetting()
    {
        using var workspace = new DiscoveryWorkspace();
        var previous = Environment.GetEnvironmentVariable(EpubCorpusDiscoveryOptions.ExternalCorpusEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(
                EpubCorpusDiscoveryOptions.ExternalCorpusEnvironmentVariable,
                workspace.ExternalRoot);

            var options = EpubCorpusDiscoveryOptions.FromEnvironment(workspace.RepositoryRoot);

            Assert.Equal(workspace.ExternalRoot, options.ExternalCorpusRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EpubCorpusDiscoveryOptions.ExternalCorpusEnvironmentVariable, previous);
        }
    }

    private static EpubCorpusManifest Manifest(params EpubCorpusPublication[] publications) =>
        new(EpubCorpusManifest.CurrentFormat, publications);

    private static EpubCorpusPublication CreatePublication(
        string id,
        byte[] bytes,
        EpubCorpusPublicationKind kind,
        EpubCorpusRedistribution redistribution,
        string relativePath) =>
        new(
            new EpubCorpusPublicationId(id),
            "Corpus publication",
            "project:test",
            new EpubCorpusLicense("Test fixture", "Generated by the test suite"),
            kind,
            redistribution,
            relativePath,
            EpubVersionFamily.Epub3,
            bytes.LongLength,
            ["en"],
            ["xhtml"],
            ["spine"],
            ["discovery"],
            [],
            new EpubCorpusSha256(Convert.ToHexString(SHA256.HashData(bytes))));

    private static byte[] WriteReport(EpubCorpusDiscoveryReport report)
    {
        using var destination = new MemoryStream();
        new EpubCorpusDiscoveryReportJsonSerializer().Write(report, destination);
        return destination.ToArray();
    }

    private static bool IsWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathFullyQualified(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private sealed class DiscoveryWorkspace : IDisposable
    {
        private readonly bool createTemporaryInsideRepository;
        private int temporaryCounter;

        public DiscoveryWorkspace(bool createTemporaryInsideRepository = false)
        {
            Root = Path.Combine(Path.GetTempPath(), $"flow-corpus-discovery-tests-{Guid.NewGuid():N}");
            RepositoryRoot = Directory.CreateDirectory(Path.Combine(Root, "repository")).FullName;
            ExternalRoot = Directory.CreateDirectory(Path.Combine(Root, "external")).FullName;
            TemporaryRoot = Directory.CreateDirectory(Path.Combine(Root, "temporary")).FullName;
            this.createTemporaryInsideRepository = createTemporaryInsideRepository;
        }

        public string Root { get; }

        public string RepositoryRoot { get; }

        public string ExternalRoot { get; }

        public string TemporaryRoot { get; }

        public string NextTemporaryDirectory => Path.Combine(
            createTemporaryInsideRepository ? RepositoryRoot : TemporaryRoot,
            $"session-{temporaryCounter + 1}");

        public EpubCorpusDiscoveryService CreateService() => new(() =>
        {
            temporaryCounter++;
            return Directory.CreateDirectory(Path.Combine(
                createTemporaryInsideRepository ? RepositoryRoot : TemporaryRoot,
                $"session-{temporaryCounter}")).FullName;
        });

        public EpubCorpusDiscoveryOptions Options(
            string? externalRoot = null,
            int maximumCandidateFiles = 4096) =>
            new(RepositoryRoot, externalRoot, maximumCandidateFiles);

        public string WriteRepositoryFile(string relativePath, byte[] bytes) =>
            WriteFile(RepositoryRoot, relativePath, bytes);

        public string WriteExternalFile(string relativePath, byte[] bytes) =>
            WriteFile(ExternalRoot, relativePath, bytes);

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string WriteFile(string root, string relativePath, byte[] bytes)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
            return path;
        }
    }
}
