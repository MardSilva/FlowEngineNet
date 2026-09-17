namespace Flow.Cli;

internal sealed class CliOperationException : Exception
{
    public CliOperationException(string resourceKey, params object?[] arguments)
        : base(resourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        ResourceKey = resourceKey;
        Arguments = arguments;
    }

    public CliOperationException(string resourceKey, Exception innerException, params object?[] arguments)
        : base(resourceKey, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKey);
        ResourceKey = resourceKey;
        Arguments = arguments;
    }

    public string ResourceKey { get; }

    public IReadOnlyList<object?> Arguments { get; }
}
