using System.Collections.Immutable;
using System.Security.Cryptography;
using Flow.Core;
using Flow.Documents;
using Flow.Rendering.Html;

namespace Flow.Cli.Tests;

public sealed class CliDocumentAssistantTests
{
    [Fact]
    public async Task StandaloneAssistantAndDirectCommandProduceIdenticalBytes()
    {
        using var workspace = new TemporaryDirectory();
        var documentPath = Path.Combine(workspace.Path, "Livro [ação].flow.json");
        var directPath = Path.Combine(workspace.Path, "direct.html");
        var interactivePath = Path.Combine(workspace.Path, "interativo [ação].html");
        await WriteSampleAsync(documentPath);
        using var directOutput = new StringWriter();
        using var directError = new StringWriter();
        var directExit = await FlowCliApplication.CreateDefault().RunAsync(
            ["render", documentPath, "--html", directPath, "--width", "390", "--height", "844"],
            directOutput,
            directError);

        var view = ScriptedAssistantView.ForStandalone(documentPath, interactivePath, "390", "844");
        var interactive = await RunInteractiveAsync(view, "render");

        Assert.Equal(0, directExit);
        Assert.Equal(0, interactive.ExitCode);
        Assert.Empty(directError.ToString());
        Assert.Empty(interactive.Error);
        Assert.Equal(await File.ReadAllBytesAsync(directPath), await File.ReadAllBytesAsync(interactivePath));
        Assert.Equal(Path.GetFullPath(interactivePath), Assert.Single(Assert.Single(view.Results).WrittenFiles));
    }

    [Fact]
    public async Task HtmlBookAssistantAndDirectCommandProduceIdenticalPackageBytes()
    {
        using var workspace = new TemporaryDirectory();
        var documentPath = Path.Combine(workspace.Path, "livro.flow.json");
        var directDirectory = Path.Combine(workspace.Path, "direct-book");
        var interactiveDirectory = Path.Combine(workspace.Path, "interactive [book]");
        await WriteSampleAsync(documentPath);
        using var directOutput = new StringWriter();
        using var directError = new StringWriter();
        var directExit = await FlowCliApplication.CreateDefault().RunAsync(
            ["render", documentPath, "--html-book", directDirectory, "--ui-language", "pt-BR"],
            directOutput,
            directError);

        var view = ScriptedAssistantView.ForHtmlBook(documentPath, interactiveDirectory, "pt-BR");
        var interactive = await RunInteractiveAsync(view, "render");

        Assert.Equal(0, directExit);
        Assert.Equal(0, interactive.ExitCode);
        Assert.Empty(directError.ToString());
        Assert.Empty(interactive.Error);
        Assert.Equal(DirectorySnapshot(directDirectory), DirectorySnapshot(interactiveDirectory));
        Assert.Equal(
            Path.GetFullPath(interactiveDirectory),
            Assert.Single(Assert.Single(view.Results).WrittenFiles));
    }

    [Fact]
    public async Task ValidateAssistantPreservesSemanticExitCodeTwo()
    {
        using var workspace = new TemporaryDirectory();
        var path = Path.Combine(workspace.Path, "invalid.flow.json");
        var duplicateId = new NodeId("duplicate");
        var invalid = new FlowDocument(
            new DocumentIdentity(new DocumentId("urn:flow:assistant:invalid")),
            new DocumentMetadata("Invalid"),
            new DocumentContent(
            [
                new Paragraph(duplicateId, [new Text("One")]),
                new Paragraph(duplicateId, [new Text("Two")]),
            ]));
        await WriteDocumentAsync(invalid, path);
        var view = ScriptedAssistantView.ForReadOnly(path);

        var result = await RunInteractiveAsync(view, "validate");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(2, Assert.Single(view.Results).ExitCode);
        Assert.Contains(ValidationDiagnosticCodes.DuplicateNodeId, result.Output, StringComparison.Ordinal);
        Assert.Empty(view.Results.Single().WrittenFiles);
    }

    [Theory]
    [InlineData("inspect", "Title:")]
    [InlineData("hash", "Hash: SHA-256:")]
    public async Task ReadOnlyAssistantsUseExistingOutputAndDoNotWriteFiles(
        string commandName,
        string expectedOutput)
    {
        using var workspace = new TemporaryDirectory();
        var documentPath = Path.Combine(workspace.Path, "sample.flow.json");
        await WriteSampleAsync(documentPath);
        var before = File.GetLastWriteTimeUtc(documentPath);
        var view = ScriptedAssistantView.ForReadOnly(documentPath);

        var result = await RunInteractiveAsync(view, commandName);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, Assert.Single(view.Results).ExitCode);
        Assert.Contains(expectedOutput, result.Output, StringComparison.Ordinal);
        Assert.Empty(view.Results.Single().WrittenFiles);
        Assert.Equal(before, File.GetLastWriteTimeUtc(documentPath));
    }

    [Fact]
    public async Task CancellingExistingRenderDestinationLeavesItUntouched()
    {
        using var workspace = new TemporaryDirectory();
        var documentPath = Path.Combine(workspace.Path, "sample.flow.json");
        var outputPath = Path.Combine(workspace.Path, "existing.html");
        await WriteSampleAsync(documentPath);
        await File.WriteAllTextAsync(outputPath, "keep-me");
        var view = ScriptedAssistantView.ForStandalone(
            documentPath,
            outputPath,
            "1024",
            "768",
            confirm: false);
        var executions = 0;
        var assistant = new CliDocumentAssistant(
            view,
            new CliTextCatalog("en-US"),
            (_, _, _) =>
            {
                executions++;
                return Task.FromResult(new CliMenuExecutionResult(0, []));
            });

        await assistant.RunAsync("render", CancellationToken.None);

        Assert.Equal(0, executions);
        Assert.Equal("keep-me", await File.ReadAllTextAsync(outputPath));
        var summary = Assert.Single(view.Summaries);
        Assert.Contains(summary.Warnings, warning => warning.Contains("already exists", StringComparison.Ordinal));
        Assert.Contains(summary.Rows, row => row.Value.Contains(outputPath, StringComparison.Ordinal));
    }

    [Fact]
    public void FlowPathValidationRequiresExistingRegularFlowJson()
    {
        using var workspace = new TemporaryDirectory();
        var jsonPath = Path.Combine(workspace.Path, "document.json");
        File.WriteAllText(jsonPath, "{}");

        Assert.False(CliDocumentAssistant.TryNormalizeFlowDocumentPath(
            jsonPath,
            out _,
            out var extensionError));
        Assert.Equal("DocumentAssistantExtensionRequired", extensionError);
        Assert.False(CliDocumentAssistant.TryNormalizeFlowDocumentPath(
            Path.Combine(workspace.Path, "missing.flow.json"),
            out _,
            out var missingError));
        Assert.Equal("DocumentAssistantNotFound", missingError);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunInteractiveAsync(
        ScriptedAssistantView view,
        string commandName)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = FlowCliApplication.CreateDefault(
            new InteractiveTerminal(output),
            new EmptyEnvironment(),
            new AssistantOnlyMenu(view, commandName));
        var exitCode = await application.RunAsync(["--language", "en-US", "menu"], output, error);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static Task WriteSampleAsync(string path) => WriteDocumentAsync(SampleBookFactory.Create(), path);

    private static async Task WriteDocumentAsync(FlowDocument document, string path)
    {
        await using var stream = File.Create(path);
        await new FlowJsonDocumentSerializer().SerializeAsync(document, stream);
    }

    private static ImmutableSortedDictionary<string, string> DirectorySnapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToImmutableSortedDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                StringComparer.Ordinal);

    private sealed class AssistantOnlyMenu(ScriptedAssistantView view, string commandName) : ICliMenu
    {
        public async Task<int> RunAsync(
            TextWriter output,
            TextWriter error,
            CliTextCatalog text,
            CliPresentationProfile presentation,
            CliMenuCommandExecutor executor,
            CancellationToken cancellationToken)
        {
            await new CliDocumentAssistant(view, text, executor).RunAsync(commandName, cancellationToken);
            return 0;
        }
    }

    private sealed class ScriptedAssistantView : ICliAssistantView
    {
        private readonly Queue<CliAssistantTextResult> _texts = [];
        private readonly Queue<CliAssistantChoiceResult> _choices = [];

        public List<CliMenuExecutionResult> Results { get; } = [];

        public List<CliAssistantSummary> Summaries { get; } = [];

        public static ScriptedAssistantView ForReadOnly(string documentPath)
        {
            var view = new ScriptedAssistantView();
            view._texts.Enqueue(CliAssistantTextResult.Entered(documentPath));
            view._choices.Enqueue(new CliAssistantChoiceResult(CliAssistantChoiceAction.Select, "run"));
            return view;
        }

        public static ScriptedAssistantView ForStandalone(
            string documentPath,
            string outputPath,
            string width,
            string height,
            bool confirm = true)
        {
            var view = new ScriptedAssistantView();
            view._texts.Enqueue(CliAssistantTextResult.Entered(documentPath));
            view._texts.Enqueue(CliAssistantTextResult.Entered(outputPath));
            view._texts.Enqueue(CliAssistantTextResult.Entered(width));
            view._texts.Enqueue(CliAssistantTextResult.Entered(height));
            view._choices.Enqueue(new CliAssistantChoiceResult(CliAssistantChoiceAction.Select, "standalone"));
            view._choices.Enqueue(new CliAssistantChoiceResult(
                CliAssistantChoiceAction.Select,
                confirm ? "run" : "back"));
            return view;
        }

        public static ScriptedAssistantView ForHtmlBook(
            string documentPath,
            string outputDirectory,
            string language)
        {
            var view = new ScriptedAssistantView();
            view._texts.Enqueue(CliAssistantTextResult.Entered(documentPath));
            view._texts.Enqueue(CliAssistantTextResult.Entered(outputDirectory));
            view._choices.Enqueue(new CliAssistantChoiceResult(CliAssistantChoiceAction.Select, "html-book"));
            view._choices.Enqueue(new CliAssistantChoiceResult(CliAssistantChoiceAction.Select, language));
            view._choices.Enqueue(new CliAssistantChoiceResult(CliAssistantChoiceAction.Select, "run"));
            return view;
        }

        public Task<CliAssistantTextResult> PromptTextAsync(
            string title,
            string cancellationHint,
            CancellationToken cancellationToken) => Task.FromResult(_texts.Dequeue());

        public Task<CliAssistantChoiceResult> SelectOneAsync(
            string title,
            ImmutableArray<CliAssistantChoice> choices,
            CancellationToken cancellationToken) => Task.FromResult(_choices.Dequeue());

        public Task<CliAssistantMultiChoiceResult> SelectManyAsync(
            string title,
            ImmutableArray<CliAssistantChoice> choices,
            CancellationToken cancellationToken) => throw new InvalidOperationException();

        public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ShowSummaryAsync(CliAssistantSummary summary, CancellationToken cancellationToken)
        {
            Summaries.Add(summary);
            return Task.CompletedTask;
        }

        public Task ShowExecutionResultAsync(
            CliMenuExecutionResult result,
            CancellationToken cancellationToken)
        {
            Results.Add(result);
            return Task.CompletedTask;
        }

        public Task WaitForReturnAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class EmptyEnvironment : IEnvironmentVariables
    {
        public string? Get(string name) => null;
    }

    private sealed class InteractiveTerminal(TextWriter output) : IFlowTerminal
    {
        public bool IsInputRedirected => false;

        public bool IsOutputRedirected => false;

        public bool IsErrorRedirected => false;

        public bool SupportsAnsi => true;

        public bool IsConsoleOutput(TextWriter candidate) => ReferenceEquals(output, candidate);

        public int GetWidth() => 100;

        public ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken) =>
            ValueTask.FromException<ConsoleKeyInfo>(new NotSupportedException());
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"flow-cli-document-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
