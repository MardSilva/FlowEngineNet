namespace Flow.Documents;

public sealed class FlowSerializationException : Exception
{
    public FlowSerializationException(
        string code,
        string message,
        string? path = null,
        long? lineNumber = null,
        long? bytePositionInLine = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        Code = code;
        Path = path;
        LineNumber = lineNumber;
        BytePositionInLine = bytePositionInLine;
    }

    public string Code { get; }

    public string? Path { get; }

    public long? LineNumber { get; }

    public long? BytePositionInLine { get; }
}
