namespace Flow.Windows.Shell;

/// <summary>Persists reconstructible application-shell preferences.</summary>
public interface IFlowWindowsSettingsStore
{
    public Task<FlowWindowsSettings> LoadAsync(
        FlowWindowsSettings fallback,
        CancellationToken cancellationToken = default);

    public Task SaveAsync(FlowWindowsSettings settings, CancellationToken cancellationToken = default);
}
