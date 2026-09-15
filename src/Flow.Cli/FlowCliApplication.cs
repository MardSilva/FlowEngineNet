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

    public FlowCliApplication(CliCommandParser parser, CliOperations operations)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(operations);
        _parser = parser;
        _operations = operations;
    }

    /// <summary>Creates the default local command composition.</summary>
    public static FlowCliApplication CreateDefault() =>
        new(
            new CliCommandParser(),
            new CliOperations(
                new FlowJsonDocumentSerializer(),
                new EpubImporter(),
                new EpubPublicationInspector(),
                new DocumentValidator(),
                new Sha256DocumentIntegrityService(new FlowDocumentCanonicalizer()),
                new AdaptiveLayoutEngine(),
                new HtmlDocumentRenderer()));

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

        var parseResult = _parser.Parse(arguments);
        if (!parseResult.IsSuccess)
        {
            await error.WriteLineAsync(parseResult.Error).ConfigureAwait(false);
            return 1;
        }

        try
        {
            return await _operations.ExecuteAsync(parseResult.Command!, output, error, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await error.WriteLineAsync("FLOWCLI_CANCELLED: The operation was cancelled; no partial final output was kept.")
                .ConfigureAwait(false);
            return 130;
        }
        catch (FlowSerializationException exception)
        {
            await error.WriteLineAsync($"{exception.Code}: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException
                                          or NotSupportedException)
        {
            await error.WriteLineAsync($"FLOWCLI_OPERATION_FAILED: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
    }
}
