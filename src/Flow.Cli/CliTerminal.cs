using Spectre.Console;

namespace Flow.Cli;

/// <summary>Abstracts terminal capabilities and keyboard input from the process console.</summary>
internal interface IFlowTerminal
{
    public bool IsInputRedirected { get; }

    public bool IsOutputRedirected { get; }

    public bool IsErrorRedirected { get; }

    public bool SupportsAnsi { get; }

    public bool SupportsCursorControl => SupportsAnsi;

    public bool SupportsUnicode => true;

    public bool IsConsoleOutput(TextWriter output);

    public int GetWidth();

    public ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken);

    public void RestoreTerminalState()
    {
    }
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

    public bool SupportsCursorControl => SupportsAnsi
        && !IsOutputRedirected
        && !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase);

    public bool SupportsUnicode
    {
        get
        {
            try
            {
                return Console.OutputEncoding.CodePage == 65001 || !OperatingSystem.IsWindows();
            }
            catch (Exception exception) when (exception is IOException
                                              or InvalidOperationException
                                              or NotSupportedException
                                              or System.Security.SecurityException)
            {
                return false;
            }
        }
    }

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

    public void RestoreTerminalState()
    {
        try
        {
            Console.ResetColor();
            if (!IsOutputRedirected)
            {
                Console.CursorVisible = true;
            }
        }
        catch (Exception exception) when (exception is IOException
                                          or InvalidOperationException
                                          or NotSupportedException
                                          or PlatformNotSupportedException
                                          or System.Security.SecurityException)
        {
            // Restoring a limited host is best effort and must not hide the command result.
        }
    }
}
