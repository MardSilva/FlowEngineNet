using System.Collections.Immutable;
using System.IO.Compression;
using System.Text;

namespace Flow.Cli.Tests;

public sealed class CliEpubAssistantTests
{
    [Fact]
    public async Task InteractiveImportUsesTraditionalOperationAndProducesIdenticalDocumentBytes()
    {
        using var temporary = new TemporaryDirectory();
        var sourcePath = Path.Combine(temporary.Path, "Livro [ação] de teste.epub");
        var directPath = Path.Combine(temporary.Path, "direto.flow.json");
        var interactivePath = Path.Combine(temporary.Path, "interativo [ação].flow.json");
        CreateMinimalEpub(sourcePath);

        using var directOutput = new StringWriter();
        using var directError = new StringWriter();
        var directExit = await FlowCliApplication.CreateDefault().RunAsync(
            ["import", sourcePath, "--output", directPath],
            directOutput,
            directError);

        var view = ScriptedAssistantView.ForImport(sourcePath, interactivePath, []);
        using var interactiveOutput = new StringWriter();
        using var interactiveError = new StringWriter();
        var application = FlowCliApplication.CreateDefault(
            new InteractiveTerminal(interactiveOutput),
            new EmptyEnvironment(),
            new AssistantOnlyMenu(view, "import"));
        var interactiveExit = await application.RunAsync(
            ["--language", "pt-BR", "menu"],
            interactiveOutput,
            interactiveError);

        Assert.Equal(0, directExit);
        Assert.Equal(0, interactiveExit);
        Assert.Empty(directError.ToString());
        Assert.Empty(interactiveError.ToString());
        Assert.Equal(await File.ReadAllBytesAsync(directPath), await File.ReadAllBytesAsync(interactivePath));
        var result = Assert.Single(view.Results);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Path.GetFullPath(interactivePath), Assert.Single(result.WrittenFiles));
        Assert.True(view.WaitedForReturn);
    }

    [Fact]
    public async Task InspectionAssistantWritesSelectedJsonAndReportsIt()
    {
        using var temporary = new TemporaryDirectory();
        var sourcePath = Path.Combine(temporary.Path, "Inspeção [real].epub");
        CreateMinimalEpub(sourcePath);
        var expectedReport = Path.Combine(temporary.Path, "Inspeção [real].inspection.json");
        var view = ScriptedAssistantView.ForInspection(sourcePath, writeJson: true);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var application = FlowCliApplication.CreateDefault(
            new InteractiveTerminal(output),
            new EmptyEnvironment(),
            new AssistantOnlyMenu(view, "epub-inspect"));

        var exitCode = await application.RunAsync(["menu"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        Assert.True(File.Exists(expectedReport));
        Assert.Equal(
            Path.GetFullPath(expectedReport),
            Assert.Single(Assert.Single(view.Results).WrittenFiles));
    }

    [Fact]
    public async Task CancellationAtFinalConfirmationDoesNotExecuteOrWriteFiles()
    {
        using var temporary = new TemporaryDirectory();
        var sourcePath = Path.Combine(temporary.Path, "book.epub");
        var outputPath = Path.Combine(temporary.Path, "book.flow.json");
        CreateMinimalEpub(sourcePath);
        var view = ScriptedAssistantView.ForImport(sourcePath, outputPath, [], confirm: false);
        var executions = 0;
        var assistant = new CliEpubAssistant(
            view,
            new CliTextCatalog("en-US"),
            (_, _, _) =>
            {
                executions++;
                return Task.FromResult(new CliMenuExecutionResult(0, []));
            });

        await assistant.RunAsync("import", CancellationToken.None);

        Assert.Equal(0, executions);
        Assert.False(File.Exists(outputPath));
        Assert.Empty(view.Results);
    }

    [Theory]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("custom-output")]
    [InlineData("reports")]
    public async Task ImportCancellationBeforeConfirmationNeverExecutes(string stage)
    {
        using var temporary = new TemporaryDirectory();
        var sourcePath = Path.Combine(temporary.Path, "book.epub");
        CreateMinimalEpub(sourcePath);
        var view = ScriptedAssistantView.CancelImportAt(stage, sourcePath);
        var executions = 0;
        var assistant = new CliEpubAssistant(
            view,
            new CliTextCatalog("en-US"),
            (_, _, _) =>
            {
                executions++;
                return Task.FromResult(new CliMenuExecutionResult(0, []));
            });

        await assistant.RunAsync("import", CancellationToken.None);

        Assert.Equal(0, executions);
        Assert.Empty(Directory.EnumerateFiles(temporary.Path, "*.json"));
    }

    [Fact]
    public async Task FailedOperationIsNotRepeatedAndWaitsBeforeReturning()
    {
        using var temporary = new TemporaryDirectory();
        var sourcePath = Path.Combine(temporary.Path, "book.epub");
        var outputPath = Path.Combine(temporary.Path, "book.flow.json");
        CreateMinimalEpub(sourcePath);
        var view = ScriptedAssistantView.ForImport(sourcePath, outputPath, []);
        var executions = 0;
        var assistant = new CliEpubAssistant(
            view,
            new CliTextCatalog("pt-BR"),
            (_, _, _) =>
            {
                executions++;
                return Task.FromResult(new CliMenuExecutionResult(1, []));
            });

        await assistant.RunAsync("import", CancellationToken.None);

        Assert.Equal(1, executions);
        Assert.Equal(1, Assert.Single(view.Results).ExitCode);
        Assert.True(view.WaitedForReturn);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void EpubPathValidationRejectsMissingWrongExtensionAndReparsePoint()
    {
        using var temporary = new TemporaryDirectory();
        var textPath = Path.Combine(temporary.Path, "book.txt");
        File.WriteAllText(textPath, "not an epub");

        Assert.False(CliEpubAssistant.TryNormalizeEpubPath(
            Path.Combine(temporary.Path, "missing.epub"),
            out _,
            out var missingError));
        Assert.Equal("AssistantEpubNotFound", missingError);
        Assert.False(CliEpubAssistant.TryNormalizeEpubPath(textPath, out _, out var extensionError));
        Assert.Equal("AssistantEpubExtensionRequired", extensionError);
    }

    private static void CreateMinimalEpub(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, "mimetype", "application/epub+zip");
        WriteEntry(
            archive,
            "META-INF/container.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0">
              <rootfiles><rootfile full-path="EPUB/package.opf" media-type="application/oebps-package+xml" /></rootfiles>
            </container>
            """);
        WriteEntry(
            archive,
            "EPUB/package.opf",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" unique-identifier="id" version="3.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:identifier id="id">urn:test:assistant</dc:identifier>
                <dc:title>Livro de ação</dc:title><dc:language>pt-BR</dc:language>
              </metadata>
              <manifest><item id="chapter" href="chapter.xhtml" media-type="application/xhtml+xml" /></manifest>
              <spine><itemref idref="chapter" /></spine>
            </package>
            """);
        WriteEntry(
            archive,
            "EPUB/chapter.xhtml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <html xmlns="http://www.w3.org/1999/xhtml"><body><h1 id="inicio">Início</h1><p>Conteúdo.</p></body></html>
            """);
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

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
            await new CliEpubAssistant(view, text, executor).RunAsync(commandName, cancellationToken);
            return 0;
        }
    }

    private sealed class ScriptedAssistantView : ICliAssistantView
    {
        private readonly Queue<CliAssistantTextResult> _texts = [];
        private readonly Queue<CliAssistantChoiceResult> _choices = [];
        private readonly Queue<CliAssistantMultiChoiceResult> _multiChoices = [];

        public List<CliMenuExecutionResult> Results { get; } = [];

        public bool WaitedForReturn { get; private set; }

        public static ScriptedAssistantView ForImport(
            string sourcePath,
            string outputPath,
            ImmutableArray<string> reports,
            bool confirm = true)
        {
            var view = new ScriptedAssistantView();
            view._texts.Enqueue(CliAssistantTextResult.Entered(sourcePath));
            view._texts.Enqueue(CliAssistantTextResult.Entered(outputPath));
            view._choices.Enqueue(new CliAssistantChoiceResult(CliAssistantChoiceAction.Select, "custom"));
            view._multiChoices.Enqueue(new CliAssistantMultiChoiceResult(false, reports));
            view._choices.Enqueue(new CliAssistantChoiceResult(
                CliAssistantChoiceAction.Select,
                confirm ? "run" : "back"));
            return view;
        }

        public static ScriptedAssistantView ForInspection(string sourcePath, bool writeJson)
        {
            var view = new ScriptedAssistantView();
            view._texts.Enqueue(CliAssistantTextResult.Entered(sourcePath));
            view._choices.Enqueue(new CliAssistantChoiceResult(
                CliAssistantChoiceAction.Select,
                writeJson ? "json" : "none"));
            view._choices.Enqueue(new CliAssistantChoiceResult(CliAssistantChoiceAction.Select, "run"));
            return view;
        }

        public static ScriptedAssistantView CancelImportAt(string stage, string sourcePath)
        {
            var view = new ScriptedAssistantView();
            if (stage == "source")
            {
                view._texts.Enqueue(CliAssistantTextResult.Cancelled);
                return view;
            }

            view._texts.Enqueue(CliAssistantTextResult.Entered(sourcePath));
            if (stage == "destination")
            {
                view._choices.Enqueue(new CliAssistantChoiceResult(CliAssistantChoiceAction.Cancel));
                return view;
            }

            if (stage == "custom-output")
            {
                view._choices.Enqueue(new CliAssistantChoiceResult(CliAssistantChoiceAction.Select, "custom"));
                view._texts.Enqueue(CliAssistantTextResult.Cancelled);
                return view;
            }

            view._choices.Enqueue(new CliAssistantChoiceResult(CliAssistantChoiceAction.Select, "automatic"));
            view._multiChoices.Enqueue(new CliAssistantMultiChoiceResult(true, []));
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
            CancellationToken cancellationToken) => Task.FromResult(_multiChoices.Dequeue());

        public Task ShowMessageAsync(string title, string message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ShowSummaryAsync(CliAssistantSummary summary, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ShowExecutionResultAsync(
            CliMenuExecutionResult result,
            CancellationToken cancellationToken)
        {
            Results.Add(result);
            return Task.CompletedTask;
        }

        public Task WaitForReturnAsync(CancellationToken cancellationToken)
        {
            WaitedForReturn = true;
            return Task.CompletedTask;
        }
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
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"flow-cli-assistant-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
