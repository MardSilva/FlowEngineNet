using System.Collections.Immutable;
using Flow.Application;

namespace Flow.Windows.Shell;

/// <summary>Builds copy-only CLI representations for visual operations.</summary>
public static class FlowWindowsCommandDisplayService
{
    public static ImmutableArray<FlowCommandDisplay> Create(
        FlowWindowsOperationRequest request,
        FlowCommandShell shell)
    {
        ArgumentNullException.ThrowIfNull(request);
        var commands = ImmutableArray.CreateBuilder<FlowCommandDisplay>();
        switch (request.Operation)
        {
            case FlowWindowsOperationKind.Inspect:
                commands.Add(Format(shell, ["epub-inspect", request.SourcePath]));
                break;
            case FlowWindowsOperationKind.Validate:
                commands.Add(Format(shell, ["validate", request.SourcePath]));
                break;
            case FlowWindowsOperationKind.Import:
                var output = request.DocumentOutputPath
                             ?? throw new ArgumentException("An import display requires an output path.", nameof(request));
                var import = new List<string> { "import", request.SourcePath, "--output", output };
                if (request.WriteDiagnosticsReport)
                {
                    import.AddRange(["--diagnostics-json", Path.ChangeExtension(output, ".diagnostics.json")]);
                }

                commands.Add(Format(shell, import));
                if (request.WriteHtmlBook)
                {
                    var directory = Path.Combine(
                        Path.GetDirectoryName(output)!,
                        Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(output)) + "_book");
                    commands.Add(Format(shell,
                    [
                        "render", output, "--html-book", directory, "--ui-language", MapLanguage(request.HtmlLanguage),
                    ]));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }

        return commands.ToImmutable();
    }

    private static FlowCommandDisplay Format(FlowCommandShell shell, IEnumerable<string> arguments) =>
        FlowCommandDisplayFormatter.Format(shell, "flow", arguments);

    private static string MapLanguage(FlowWindowsHtmlLanguage language) => language switch
    {
        FlowWindowsHtmlLanguage.Automatic => "auto",
        FlowWindowsHtmlLanguage.English => "en",
        FlowWindowsHtmlLanguage.PortugueseBrazil => "pt-BR",
        FlowWindowsHtmlLanguage.PortuguesePortugal => "pt-PT",
        _ => throw new ArgumentOutOfRangeException(nameof(language)),
    };
}
