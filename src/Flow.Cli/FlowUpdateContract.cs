namespace Flow.Cli;

internal static class FlowUpdateContract
{
    public static readonly Uri ReleasesEndpoint = new(
        "https://api.github.com/repos/MardSilva/FlowEngineNet/releases");

    public static readonly Uri ProjectReleasesPage = new(
        "https://github.com/MardSilva/FlowEngineNet/releases");

    public const string RepositoryOwner = "MardSilva";
    public const string RepositoryName = "FlowEngineNet";
    public const string UserAgentProduct = "FlowEngineNet-UpdateCheck";
    public const int MaximumResponseBytes = 2 * 1024 * 1024;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
}
