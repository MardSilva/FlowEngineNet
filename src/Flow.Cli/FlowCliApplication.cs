using Flow.Documents;
using Flow.Epub;
using Flow.Layout;
using Flow.Rendering.Html;
using Flow.Security;

namespace Flow.Cli;

/// <summary>Coordinates CLI parsing, operations, diagnostics, and process-style exit codes.</summary>
public sealed class FlowCliApplication
{
    private readonly CliCommandParser _parser;
    private readonly CliOperations _operations;
    private readonly IFlowTerminal _terminal;
    private readonly IEnvironmentVariables _environment;
    private readonly ICliMenu _menu;

    public FlowCliApplication(CliCommandParser parser, CliOperations operations)
        : this(
            parser,
            operations,
            new SystemFlowTerminal(),
            new SystemEnvironmentVariables(),
            new SpectreCliMenu())
    {
    }

    internal FlowCliApplication(
        CliCommandParser parser,
        CliOperations operations,
        IFlowTerminal terminal,
        IEnvironmentVariables environment,
        ICliMenu menu)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(menu);
        _parser = parser;
        _operations = operations;
        _terminal = terminal;
        _environment = environment;
        _menu = menu;
    }

    /// <summary>Creates the default local command composition.</summary>
    public static FlowCliApplication CreateDefault() =>
        CreateDefault(new SystemFlowTerminal(), new SystemEnvironmentVariables());

    internal static FlowCliApplication CreateDefault(
        IFlowTerminal terminal,
        IEnvironmentVariables environment) =>
        CreateDefault(terminal, environment, new SpectreCliMenu());

    internal static FlowCliApplication CreateDefault(
        IFlowTerminal terminal,
        IEnvironmentVariables environment,
        ICliMenu menu) =>
        new(
            new CliCommandParser(),
            new CliOperations(
                new FlowJsonDocumentSerializer(),
                new EpubImporter(),
                new EpubPublicationInspector(),
                new DocumentValidator(),
                new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer()),
                new AdaptiveLayoutEngine(),
                new HtmlDocumentRenderer()),
            terminal,
            environment,
            menu);

    /// <summary>Runs one command without taking ownership of the supplied writers.</summary>
    public async Task<int> RunAsync(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var invocationResult = CliInvocationOptionsParser.Parse(arguments);
        var invocationText = new CliTextCatalog(invocationResult.Options?.CultureName);
        if (!invocationResult.IsSuccess)
        {
            await error.WriteLineAsync(invocationText.Diagnostic(
                    invocationResult.DiagnosticCode!,
                    invocationResult.ResourceKey!,
                    invocationResult.Arguments.ToArray()))
                .ConfigureAwait(false);
            return 1;
        }

        var invocation = invocationResult.Options!;
        var text = new CliTextCatalog(invocation.CultureName);
        var presentation = CliPresentationPolicy.Resolve(invocation, _terminal, output, _environment);
        if (invocation.ShowBanner)
        {
            await output.WriteLineAsync(CliBanner.Text).ConfigureAwait(false);
        }

        var parseResult = _parser.Parse(invocation.CommandArguments);
        if (!parseResult.IsSuccess)
        {
            await error.WriteLineAsync(parseResult.Diagnostic!.Format(text)).ConfigureAwait(false);
            return 1;
        }

        try
        {
            if (parseResult.Command is MenuCommand)
            {
                if (presentation.Mode != CliPresentationMode.Rich || !presentation.IsInteractive)
                {
                    await error.WriteLineAsync(text.Diagnostic(
                            CliMenuDiagnosticCodes.RequiresInteractiveTerminal,
                            "ErrorMenuRequiresInteractive"))
                        .ConfigureAwait(false);
                    return 1;
                }

                return await _menu.RunAsync(
                        output,
                        error,
                        text,
                        presentation,
                        ExecuteMenuCommandAsync,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return await _operations.ExecuteAsync(
                    parseResult.Command!,
                    output,
                    error,
                    text,
                    presentation,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync(text.Diagnostic("FLOWCLI_CANCELLED", "ErrorCancelled"))
                .ConfigureAwait(false);
            return 130;
        }
        catch (FlowSerializationException exception)
        {
            await error.WriteLineAsync(
                    $"{exception.Code}: {text.DiagnosticMessage(exception.Code, exception.Message)}")
                .ConfigureAwait(false);
            return 1;
        }
        catch (CliOperationException exception)
        {
            await error.WriteLineAsync(
                    text.Diagnostic(
                        "FLOWCLI_OPERATION_FAILED",
                        exception.ResourceKey,
                        exception.Arguments.ToArray()))
                .ConfigureAwait(false);
            return 1;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException
                                          or NotSupportedException)
        {
            await error.WriteLineAsync(
                    text.Diagnostic("FLOWCLI_OPERATION_FAILED", "ErrorOperationFailed", exception.Message))
                .ConfigureAwait(false);
            return 1;
        }

        async Task<CliMenuExecutionResult> ExecuteMenuCommandAsync(
            CliCommand command,
            CancellationToken token)
        {
            var observer = new CliMenuOperationObserver();
            try
            {
                var exitCode = await _operations.ExecuteAsync(
                        command,
                        output,
                        error,
                        text,
                        presentation,
                        token,
                        observer)
                    .ConfigureAwait(false);
                return new CliMenuExecutionResult(exitCode, observer.WrittenFiles);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (FlowSerializationException exception)
            {
                await error.WriteLineAsync(
                        $"{exception.Code}: {text.DiagnosticMessage(exception.Code, exception.Message)}")
                    .ConfigureAwait(false);
                return new CliMenuExecutionResult(1, observer.WrittenFiles);
            }
            catch (CliOperationException exception)
            {
                await error.WriteLineAsync(text.Diagnostic(
                        "FLOWCLI_OPERATION_FAILED",
                        exception.ResourceKey,
                        exception.Arguments.ToArray()))
                    .ConfigureAwait(false);
                return new CliMenuExecutionResult(1, observer.WrittenFiles);
            }
            catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or ArgumentException
                                              or NotSupportedException)
            {
                await error.WriteLineAsync(
                        text.Diagnostic("FLOWCLI_OPERATION_FAILED", "ErrorOperationFailed", exception.Message))
                    .ConfigureAwait(false);
                return new CliMenuExecutionResult(1, observer.WrittenFiles);
            }
        }
    }

    private sealed class CliMenuOperationObserver : ICliOperationObserver
    {
        private readonly List<string> _writtenFiles = [];

        public System.Collections.Immutable.ImmutableArray<string> WrittenFiles => [.. _writtenFiles];

        public void FileWritten(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var comparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            if (!_writtenFiles.Contains(fullPath, comparer))
            {
                _writtenFiles.Add(fullPath);
            }
        }
    }
}
