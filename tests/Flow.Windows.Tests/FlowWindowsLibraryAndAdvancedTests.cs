using Flow.Application;
using Flow.Windows.Shell;

namespace Flow.Windows.Tests;

public sealed class FlowWindowsLibraryAndAdvancedTests
{
    [Fact]
    public async Task LibraryIndexIsSortedBoundedAndNeverPersisted()
    {
        var root = Path.Combine(Path.GetTempPath(), $"flow-library-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "A.epub");
            var second = Path.Combine(root, "sub", "B.epub");
            Directory.CreateDirectory(Path.GetDirectoryName(second)!);
            await File.WriteAllTextAsync(first, "one");
            await File.WriteAllTextAsync(second, "two");
            var operations = new RecordingOperationService();
            var service = new FlowWindowsLibraryService(operations);

            var index = await service.DiscoverAsync(root);

            Assert.Equal([first, second], index.Items.Select(static item => item.SourcePath));
            Assert.All(index.Items, static item => Assert.True(item.IsUsable));
            Assert.False(index.WasTruncated);
            Assert.Equal(4, operations.Requests.Count);
            Assert.Equal(2, Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EquivalentCommandsAreDisplayOnlyEscapedAndComplete()
    {
        var source = Path.Combine(Path.GetTempPath(), "O'Brien book.epub");
        var output = Path.Combine(Path.GetTempPath(), "Flow output", "book.flow.json");
        var request = new FlowWindowsOperationRequest(
            FlowWindowsOperationKind.Import,
            source,
            output,
            writeDiagnosticsReport: true,
            writeHtmlBook: true,
            htmlLanguage: FlowWindowsHtmlLanguage.PortugueseBrazil);

        var commands = FlowWindowsCommandDisplayService.Create(request, FlowCommandShell.PowerShell);

        Assert.Equal(2, commands.Length);
        Assert.All(commands, static command => Assert.Equal("flow", command.Executable));
        Assert.Contains("O''Brien book.epub", commands[0].Text, StringComparison.Ordinal);
        Assert.Contains("--diagnostics-json", commands[0].Arguments);
        Assert.Contains("--html-book", commands[1].Arguments);
        Assert.Contains("pt-BR", commands[1].Arguments);
    }

    private sealed class RecordingOperationService : IFlowWindowsOperationService
    {
        public List<FlowWindowsOperationRequest> Requests { get; } = [];

        public Task<FlowWindowsOperationResult> ExecuteAsync(
            FlowWindowsOperationRequest request,
            IProgress<FlowApplicationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var summary = new FlowWindowsBookSummary(
                Path.GetFileNameWithoutExtension(request.SourcePath),
                [],
                "en",
                request.Operation == FlowWindowsOperationKind.Inspect ? "3.0" : null,
                1,
                1,
                0,
                null,
                []);
            return Task.FromResult(new FlowWindowsOperationResult(request.Operation, true, summary, []));
        }

        public string SuggestDocumentOutputPath(string sourcePath, string? title = null) =>
            Path.ChangeExtension(sourcePath, ".flow.json");

        public string GetInterruptedOutputPath(string finalPath) => finalPath + ".partial";

        public Task<FlowWindowsPreviewSession> CreatePreviewAsync(
            string sourcePath,
            FlowWindowsPreviewProfile profile,
            FlowWindowsHtmlLanguage htmlLanguage = FlowWindowsHtmlLanguage.Automatic,
            IProgress<FlowApplicationProgress>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
