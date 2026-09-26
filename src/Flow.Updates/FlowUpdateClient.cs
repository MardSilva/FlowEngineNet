using System.Reflection;

namespace Flow.Updates;

public enum UpdateChannel
{
    Stable,
    Prerelease,
}

internal static class UpdateProductInfo
{
    public static string Version { get; } = typeof(UpdateProductInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
}

/// <summary>Owns the read-only release transport. Construction never performs a request.</summary>
public sealed class FlowUpdateClient : IFlowUpdateChecker, IDisposable
{
    private readonly HttpUpdateReleaseSource _source;
    private readonly FlowUpdateChecker _checker;

    public FlowUpdateClient(string currentVersion, bool graphicalApplication = false)
    {
        _source = new HttpUpdateReleaseSource();
        _checker = new FlowUpdateChecker(_source, new DefaultInstallationMethodDetector(graphicalApplication), currentVersion);
    }

    internal FlowUpdateClient(string currentVersion, HttpClient transport, IInstallationMethodDetector detector)
    {
        _source = new HttpUpdateReleaseSource(transport);
        _checker = new FlowUpdateChecker(_source, detector, currentVersion);
    }

    public Task<FlowUpdateCheckResult> CheckAsync(UpdateChannel channel, string uiCultureName, CancellationToken cancellationToken = default) =>
        _checker.CheckAsync(channel, uiCultureName, cancellationToken);

    public void Dispose() => _source.Dispose();
}
