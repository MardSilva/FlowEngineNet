using System.Text;
using System.Text.Json;

namespace Flow.Cli;

internal static class FlowUpdateJsonWriter
{
    public static string Serialize(FlowUpdateCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("format", "flow-update-check-0.1");
            writer.WriteString("status", ToKebabCase(result.Status));
            writer.WriteString("channel", ToKebabCase(result.Channel));
            writer.WriteString("currentVersion", result.CurrentVersion);
            writer.WriteString("latestVersion", result.LatestVersion);
            writer.WriteString("installationMethod", InstallationMethod(result.InstallationMethod));
            writer.WriteString("releaseUrl", result.ReleaseUrl.AbsoluteUri);
            if (result.ArtifactName is null)
            {
                writer.WriteNull("artifactName");
            }
            else
            {
                writer.WriteString("artifactName", result.ArtifactName);
            }

            if (result.ArtifactSha256 is null)
            {
                writer.WriteNull("artifactSha256");
            }
            else
            {
                writer.WriteString("artifactSha256", result.ArtifactSha256);
            }

            writer.WriteString("recommendedAction", RecommendedAction(result.InstallationMethod));
            if (result.InstallationMethod == FlowInstallationMethod.DotNetTool)
            {
                writer.WriteString(
                    "recommendedCommand",
                    $"dotnet tool update --global FlowEngineNet.Tool --version {result.LatestVersion}");
            }
            else
            {
                writer.WriteNull("recommendedCommand");
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray())
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n') + "\n";
    }

    private static string RecommendedAction(FlowInstallationMethod method) => method switch
    {
        FlowInstallationMethod.Msi => "run-msi",
        FlowInstallationMethod.DotNetTool => "update-dotnet-tool",
        FlowInstallationMethod.Portable => "replace-portable-files",
        _ => "review-release",
    };

    private static string InstallationMethod(FlowInstallationMethod method) => method switch
    {
        FlowInstallationMethod.Msi => "msi",
        FlowInstallationMethod.DotNetTool => "dotnet-tool",
        FlowInstallationMethod.Portable => "portable",
        _ => "unknown",
    };

    private static string ToKebabCase<T>(T value) where T : struct, Enum =>
        value.ToString().ToLowerInvariant();
}
