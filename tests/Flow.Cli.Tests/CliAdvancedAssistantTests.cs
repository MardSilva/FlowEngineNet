using System.Collections.Immutable;

namespace Flow.Cli.Tests;

public sealed class CliAdvancedAssistantTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "flow-cli-advanced-tests",
        Guid.NewGuid().ToString("N"));

    public CliAdvancedAssistantTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task LegalDeclarationsAreNeverInferredFromRunConfirmation()
    {
        var source = CreateFile("book.epub");
        var view = new ScriptedAssistantView(
            texts:
            [
                source,
                "candidate-001",
                new string('a', 64),
                Path.Combine(_root, "gate.json"),
                _root,
            ],
            choices:
            [
                "no", // custom repetition count
                "cancel", // legal-use declaration
            ]);
        CliCommand? executed = null;

        await new CliAdvancedAssistant(
                view,
                new CliTextCatalog("en-US"),
                (command, _, _) =>
                {
                    executed = command;
                    return Task.FromResult(new CliMenuExecutionResult(0, []));
                })
            .RunAsync("epub-qualify", CancellationToken.None);

        Assert.Null(executed);
        Assert.Empty(view.Summaries);
        Assert.Contains(view.Messages, item => item.Message.Contains("declarations", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GatePreservesTypedIdentityHashAndExecutionOptions()
    {
        var source = CreateFile("book.epub");
        var hash = new string('b', 64);
        var report = Path.Combine(_root, "gate.json");
        var view = new ScriptedAssistantView(
            texts: [source, "candidate-001", hash, report, _root, "3"],
            choices: ["yes", "declare", "declare", "yes", "yes", "yes", "run"]);
        CliCommand? executed = null;

        await new CliAdvancedAssistant(
                view,
                new CliTextCatalog("pt-BR"),
                (command, _, _) =>
                {
                    executed = command;
                    return Task.FromResult(new CliMenuExecutionResult(0, [report]));
                })
            .RunAsync("epub-qualify", CancellationToken.None);

        var gate = Assert.IsType<QualifyEpubCommand>(executed);
        Assert.Equal("candidate-001", gate.CandidateId.Value);
        Assert.Equal(hash.ToUpperInvariant(), gate.ExpectedSourceSha256.Value);
        Assert.Equal(3, gate.RepetitionCount);
        Assert.True(gate.IncludeEnvironment);
        Assert.True(gate.Force);
        Assert.True(gate.Resume);
        Assert.Single(view.Summaries);
        Assert.Equal(1, view.Messages.Count(message =>
            message.Message.Contains("declara", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1, view.WaitCount);
    }

    [Fact]
    public async Task CorpusUsesOptionalPathsAndSameTypedCommand()
    {
        var manifest = CreateFile("corpus.json");
        var externalRoot = Directory.CreateDirectory(Path.Combine(_root, "public-books")).FullName;
        var report = Path.Combine(_root, "corpus-report.json");
        var view = new ScriptedAssistantView(
            texts: [manifest, _root, report, externalRoot],
            choices: ["yes", "no", "no", "yes", "run"]);
        CliCommand? executed = null;

        await new CliAdvancedAssistant(
                view,
                new CliTextCatalog("en-US"),
                (command, _, _) =>
                {
                    executed = command;
                    return Task.FromResult(new CliMenuExecutionResult(0, [report]));
                })
            .RunAsync("corpus", CancellationToken.None);

        var corpus = Assert.IsType<CorpusCommand>(executed);
        Assert.Equal(Path.GetFullPath(manifest), corpus.ManifestPath);
        Assert.Equal(Path.GetFullPath(externalRoot), corpus.ExternalCorpusRoot);
        Assert.Null(corpus.AcceptedBaselinePath);
        Assert.False(corpus.Force);
        Assert.True(corpus.Resume);
    }

    [Fact]
    public async Task CleanupRequiresExactIdAgainBeforeNormalConfirmation()
    {
        const string executionId = "0123456789abcdef0123456789abcdef";
        var destination = Path.Combine(_root, "gate.json");
        var view = new ScriptedAssistantView(
            texts: [destination, executionId, "ffffffffffffffffffffffffffffffff"],
            choices: []);
        CliCommand? executed = null;

        await new CliAdvancedAssistant(
                view,
                new CliTextCatalog("en-US"),
                (command, _, _) =>
                {
                    executed = command;
                    return Task.FromResult(new CliMenuExecutionResult(0, []));
                })
            .RunAsync("execution-clean", CancellationToken.None);

        Assert.Null(executed);
        Assert.Contains(view.Messages, message =>
            message.Message.Contains("Nothing was cleaned", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CleanupForwardsOnlyTypedDestinationAndConfirmedExecutionId()
    {
        const string executionId = "0123456789abcdef0123456789abcdef";
        var destination = Path.Combine(_root, "gate.json");
        var view = new ScriptedAssistantView(
            texts: [destination, executionId, executionId],
            choices: ["run"]);
        CliCommand? executed = null;

        await new CliAdvancedAssistant(
                view,
                new CliTextCatalog("pt-BR"),
                (command, _, _) =>
                {
                    executed = command;
                    return Task.FromResult(new CliMenuExecutionResult(0, []));
                })
            .RunAsync("execution-clean", CancellationToken.None);

        var cleanup = Assert.IsType<ExecutionCleanCommand>(executed);
        Assert.Equal(Path.GetFullPath(destination), cleanup.DestinationPath);
        Assert.Equal(Guid.ParseExact(executionId, "N"), cleanup.ExecutionId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "fixture");
        return path;
    }

    private sealed class ScriptedAssistantView : ICliAssistantView
    {
        private readonly Queue<string> _texts;
        private readonly Queue<string> _choices;

        public ScriptedAssistantView(IEnumerable<string> texts, IEnumerable<string> choices)
        {
            _texts = new Queue<string>(texts);
            _choices = new Queue<string>(choices);
        }

        public List<(string Title, string Message)> Messages { get; } = [];

        public List<CliAssistantSummary> Summaries { get; } = [];

        public int WaitCount { get; private set; }

        public Task<CliAssistantTextResult> PromptTextAsync(
            string title,
            string cancellationHint,
            CancellationToken cancellationToken) =>
            Task.FromResult(CliAssistantTextResult.Entered(_texts.Dequeue()));

        public Task<CliAssistantChoiceResult> SelectOneAsync(
            string title,
            ImmutableArray<CliAssistantChoice> choices,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CliAssistantChoiceResult(
                CliAssistantChoiceAction.Select,
                _choices.Dequeue()));

        public Task<CliAssistantMultiChoiceResult> SelectManyAsync(
            string title,
            ImmutableArray<CliAssistantChoice> choices,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Advanced assistants do not use multi-selection.");

        public Task ShowMessageAsync(
            string title,
            string message,
            CancellationToken cancellationToken)
        {
            Messages.Add((title, message));
            return Task.CompletedTask;
        }

        public Task ShowSummaryAsync(
            CliAssistantSummary summary,
            CancellationToken cancellationToken)
        {
            Summaries.Add(summary);
            return Task.CompletedTask;
        }

        public Task ShowExecutionResultAsync(
            CliMenuExecutionResult result,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task WaitForReturnAsync(CancellationToken cancellationToken)
        {
            WaitCount++;
            return Task.CompletedTask;
        }
    }
}
