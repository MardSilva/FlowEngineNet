using System.Reflection;

namespace Flow.Cli;

internal static class CliProductInfo
{
    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        var informationalVersion = typeof(CliProductInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
            return metadataSeparator < 0
                ? informationalVersion
                : informationalVersion[..metadataSeparator];
        }

        return typeof(CliProductInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
