using Flow.Core;

namespace Flow.Documents;

public sealed record ValidationDiagnostic
{
    public ValidationDiagnostic(
        string code,
        ValidationSeverity severity,
        string message,
        NodeId? nodeId = null,
        DocumentAnchor? anchor = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Severity = severity;
        Message = message;
        NodeId = nodeId;
        Anchor = anchor;
    }

    public string Code { get; }

    public ValidationSeverity Severity { get; }

    public string Message { get; }

    public NodeId? NodeId { get; }

    public DocumentAnchor? Anchor { get; }
}
