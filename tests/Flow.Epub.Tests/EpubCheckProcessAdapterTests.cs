using Flow.Epub.Corpus;

namespace Flow.Epub.Tests;

public sealed class EpubCheckProcessAdapterTests
{
    [Fact]
    public async Task EvaluateAsync_RecognizesConformantJsonAndSanitizesCapturedPaths()
    {
        using var workspace = new FakeToolWorkspace();
        var adapter = workspace.CreateAdapter("success");

        var evidence = await adapter.EvaluateAsync(new MemoryStream("epub"u8.ToArray()), "book");

        Assert.Equal(EpubCheckEvidenceStatus.Conformant, evidence.Status);
        Assert.Equal("5.4.0", evidence.ToolVersion);
        Assert.Equal(0, evidence.ExitCode);
        Assert.DoesNotContain(workspace.Root, evidence.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(workspace.Root, evidence.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetFileName(workspace.Root), evidence.StandardOutput, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetFileName(workspace.Root), evidence.StandardError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<epub>", evidence.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_NonConformantResultRemainsTypedExternalEvidence()
    {
        using var workspace = new FakeToolWorkspace();

        var evidence = await workspace.CreateAdapter("failure")
            .EvaluateAsync(new MemoryStream("epub"u8.ToArray()), "book");

        Assert.Equal(EpubCheckEvidenceStatus.NonConformant, evidence.Status);
        Assert.Equal(1, evidence.ExitCode);
        Assert.Equal(1, evidence.ErrorCount);
        var message = Assert.Single(evidence.Messages);
        Assert.Equal("RSC-001", message.Code);
        Assert.Equal("EPUB/missing.xhtml", message.Resource);
    }

    [Fact]
    public async Task EvaluateAsync_TimeoutKillsToolAndReturnsDiagnostic()
    {
        using var workspace = new FakeToolWorkspace();

        var evidence = await workspace.CreateAdapter("timeout", timeout: TimeSpan.FromMilliseconds(150))
            .EvaluateAsync(new MemoryStream("epub"u8.ToArray()), "book");

        Assert.Equal(EpubCheckEvidenceStatus.TimedOut, evidence.Status);
        Assert.Contains(evidence.Diagnostics, static item => item.Code == EpubCheckDiagnosticCodes.Timeout);
        Assert.Empty(Directory.EnumerateDirectories(workspace.ProcessRoot));
    }

    [Fact]
    public async Task EvaluateAsync_UserCancellationIsPropagatedAndWorkspaceIsRemoved()
    {
        using var workspace = new FakeToolWorkspace();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workspace.CreateAdapter("cancel", TimeSpan.FromMinutes(1))
            .EvaluateAsync(new MemoryStream("epub"u8.ToArray()), "book", cancellation.Token));

        Assert.Empty(Directory.EnumerateDirectories(workspace.ProcessRoot));
    }

    [Fact]
    public async Task EvaluateAsync_ExcessiveOutputIsBoundedAndDiagnosed()
    {
        using var workspace = new FakeToolWorkspace();

        var evidence = await workspace.CreateAdapter("excessive", maximumCharacters: 1024)
            .EvaluateAsync(new MemoryStream("epub"u8.ToArray()), "book");

        Assert.Equal(EpubCheckEvidenceStatus.OutputLimitExceeded, evidence.Status);
        Assert.True(evidence.StandardOutput.Length <= 1024);
        Assert.Contains(evidence.Diagnostics, static item => item.Code == EpubCheckDiagnosticCodes.OutputLimitExceeded);
    }

    [Fact]
    public async Task EvaluateAsync_MissingConfiguredToolDoesNotStartAProcess()
    {
        using var workspace = new FakeToolWorkspace();
        var missing = Path.Combine(workspace.Root, "missing", "epubcheck.exe");
        var adapter = new EpubCheckProcessAdapter(new EpubCheckProcessOptions(EpubCheckToolKind.Executable, missing));

        var evidence = await adapter.EvaluateAsync(new MemoryStream("epub"u8.ToArray()), "book");

        Assert.Equal(EpubCheckEvidenceStatus.Unavailable, evidence.Status);
        Assert.Contains(evidence.Diagnostics, static item => item.Code == EpubCheckDiagnosticCodes.ToolMissing);
    }

    [Theory]
    [InlineData("incompatible", EpubCheckEvidenceStatus.IncompatibleVersion, EpubCheckDiagnosticCodes.IncompatibleVersion)]
    [InlineData("unrecognized", EpubCheckEvidenceStatus.UnrecognizedOutput, EpubCheckDiagnosticCodes.UnrecognizedOutput)]
    public async Task EvaluateAsync_DiagnosesUnsupportedVersionAndUnknownOutput(
        string mode,
        EpubCheckEvidenceStatus expectedStatus,
        string expectedCode)
    {
        using var workspace = new FakeToolWorkspace();

        var evidence = await workspace.CreateAdapter(mode)
            .EvaluateAsync(new MemoryStream("epub"u8.ToArray()), "book");

        Assert.Equal(expectedStatus, evidence.Status);
        Assert.Contains(evidence.Diagnostics, item => item.Code == expectedCode);
    }

    [Fact]
    public async Task EvaluateAsync_PreservesArgumentsContainingSpacesWithoutShellParsing()
    {
        using var workspace = new FakeToolWorkspace(copyToolToPathWithSpaces: true);

        var evidence = await workspace.CreateAdapter("require-spaces")
            .EvaluateAsync(new MemoryStream("epub"u8.ToArray()), "book with spaces");

        Assert.Equal(EpubCheckEvidenceStatus.Conformant, evidence.Status);
    }

    private sealed class FakeToolWorkspace : IDisposable
    {
        private readonly string fakeToolDll;

        public FakeToolWorkspace(bool copyToolToPathWithSpaces = false)
        {
            Root = Path.Combine(Path.GetTempPath(), $"flow epubcheck tests {Guid.NewGuid():N}");
            ProcessRoot = Directory.CreateDirectory(Path.Combine(Root, "process workspaces with spaces")).FullName;
            var sourceDirectory = FindFakeToolOutput();
            if (copyToolToPathWithSpaces)
            {
                var destination = Directory.CreateDirectory(Path.Combine(Root, "fake tool with spaces")).FullName;
                foreach (var source in Directory.EnumerateFiles(sourceDirectory))
                {
                    File.Copy(source, Path.Combine(destination, Path.GetFileName(source)));
                }

                sourceDirectory = destination;
            }

            fakeToolDll = Path.Combine(sourceDirectory, "Flow.EpubCheck.FakeTool.dll");
        }

        public string Root { get; }

        public string ProcessRoot { get; }

        public EpubCheckProcessAdapter CreateAdapter(
            string mode,
            TimeSpan? timeout = null,
            int maximumCharacters = 256 * 1024)
        {
            var options = new EpubCheckProcessOptions(
                EpubCheckToolKind.Executable,
                FindDotNetHost(),
                toolArguments: [fakeToolDll, mode],
                timeout: timeout,
                maximumCapturedCharacters: maximumCharacters);
            return new EpubCheckProcessAdapter(options, () =>
                Directory.CreateDirectory(Path.Combine(ProcessRoot, Guid.NewGuid().ToString("N"))).FullName);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string FindFakeToolOutput()
        {
            var configuration = Directory.GetParent(Directory.GetParent(AppContext.BaseDirectory)!.FullName)!.Name;
            return Path.Combine(FindRepositoryRoot(), "tests", "Flow.EpubCheck.FakeTool", "bin", configuration, "net10.0");
        }

        private static string FindDotNetHost()
        {
            var configured = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            {
                return configured;
            }

            var candidates = OperatingSystem.IsWindows()
                ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe") }
                : new[] { "/usr/bin/dotnet", "/usr/local/bin/dotnet" };
            return candidates.First(File.Exists);
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "Flow.sln")))
            {
                current = current.Parent;
            }

            return current?.FullName ?? throw new DirectoryNotFoundException("The repository root was not found.");
        }
    }
}
