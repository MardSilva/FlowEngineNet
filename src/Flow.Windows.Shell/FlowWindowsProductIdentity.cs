using System.Reflection;
using System.Text.Json;

namespace Flow.Windows.Shell;

public enum FlowWindowsIdentityState
{
    Development,
    Distribution,
    InvalidManifest,
}

/// <summary>Public build facts only; never includes machine or publication paths.</summary>
public sealed record FlowWindowsProductIdentity(
    string Version,
    string Architecture,
    FlowWindowsIdentityState State,
    string? Revision = null,
    bool ModifiedSource = false)
{
    public static string PublicVersion { get; } = typeof(FlowWindowsProductIdentity).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    /// <summary>Receives distribution metadata from the host without depending on an installer.</summary>
    public static FlowWindowsProductIdentity Resolve(string architecture, string? manifest)
    {
        var fallback = new FlowWindowsProductIdentity(PublicVersion, architecture,
            manifest is null ? FlowWindowsIdentityState.Development : FlowWindowsIdentityState.InvalidManifest);
        if (manifest is null || manifest.Length > 16384)
        {
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(manifest, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.GetProperty("format").GetString() != "flow-windows-combined-payload-0.1"
                || root.GetProperty("product").GetString() != "Flow Engine .NET"
                || root.GetProperty("version").GetString() != PublicVersion
                || root.GetProperty("architecture").GetString() != architecture)
            {
                return fallback;
            }

            var revision = root.GetProperty("sourceRevision").GetString();
            if (revision is null || revision.Length != 40 || !revision.All(char.IsAsciiHexDigit))
            {
                return fallback;
            }

            return fallback with
            {
                State = FlowWindowsIdentityState.Distribution,
                Revision = revision,
                ModifiedSource = root.GetProperty("sourceTreeDirty").GetBoolean(),
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return fallback;
        }
    }
}
