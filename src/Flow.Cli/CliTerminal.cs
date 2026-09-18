using Spectre.Console;

namespace Flow.Cli;

/// <summary>Abstracts terminal capabilities and keyboard input from the process console.</summary>
internal interface IFlowTerminal
{
    public bool IsInputRedirected { get; }

    public bool IsOutputRedirected { get; }

    public bool IsErrorRedirected { get; }

    public bool SupportsAnsi { get; }

    public bool IsConsoleOutput(TextWriter output);

    public int GetWidth();

    public ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken);
}

/// <summary>Reads environment variables without coupling presentation policy to process globals.</summary>
internal interface IEnvironmentVariables
{
    public string? Get(string name);
}

internal sealed class SystemEnvironmentVariables : IEnvironmentVariables
{
    public string? Get(string name) => Environment.GetEnvironmentVariable(name);
}

internal static class CliTerminalErrorCodes
{
    public const string InteractiveInputUnavailable = "FLOWCLI_INTERACTIVE_INPUT_UNAVAILABLE";
}

/// <summary>Adapts the process console to the testable terminal boundary.</summary>
internal sealed class SystemFlowTerminal : IFlowTerminal
{
    private static readonly TimeSpan KeyPollingInterval = TimeSpan.FromMilliseconds(25);

    public bool IsInputRedirected => Console.IsInputRedirected;

    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public bool IsErrorRedirected => Console.IsErrorRedirected;

    public bool SupportsAnsi => AnsiConsole.Profile.Capabilities.Ansi;

    public bool IsConsoleOutput(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return ReferenceEquals(output, Console.Out);
    }

    public int GetWidth() => Console.WindowWidth;

    public async ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsInputRedirected)
        {
            throw new InvalidOperationException(CliTerminalErrorCodes.InteractiveInputUnavailable);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Console.KeyAvailable)
            {
                return Console.ReadKey(intercept: true);
            }

            await Task.Delay(KeyPollingInterval, cancellationToken).ConfigureAwait(false);
        }
    }
}
