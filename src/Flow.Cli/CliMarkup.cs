using Spectre.Console;

namespace Flow.Cli;

/// <summary>Provides the only supported path for writing untrusted values through Spectre markup.</summary>
internal static class CliMarkup
{
    public static string EscapeExternal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Markup.Escape(value);
    }

    public static void WriteEscapedLine(IAnsiConsole console, string value)
    {
        ArgumentNullException.ThrowIfNull(console);
        console.MarkupLine(EscapeExternal(value));
    }
}
