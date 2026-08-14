using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Flow.Core;

public sealed class DocumentAnchor : IEquatable<DocumentAnchor>
{
    private const string Prefix = "flow:";
    private const int MaximumSegmentCount = 64;

    private DocumentAnchor(ImmutableArray<NodeId> segments)
    {
        Segments = segments;
        Value = Prefix + string.Join('/', segments.Select(static segment => segment.Value));
    }

    public ImmutableArray<NodeId> Segments { get; }

    public NodeId TargetId => Segments[^1];

    public string Value { get; }

    public static DocumentAnchor Create(IEnumerable<NodeId> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var copiedSegments = segments.ToImmutableArray();
        if (copiedSegments.IsEmpty)
        {
            throw new ArgumentException("A document anchor must contain at least one node ID.", nameof(segments));
        }

        if (copiedSegments.Length > MaximumSegmentCount)
        {
            throw new ArgumentException(
                $"A document anchor cannot contain more than {MaximumSegmentCount} segments.",
                nameof(segments));
        }

        if (copiedSegments.Any(static segment => segment is null))
        {
            throw new ArgumentException("A document anchor cannot contain null segments.", nameof(segments));
        }

        return new DocumentAnchor(copiedSegments);
    }

    public static DocumentAnchor Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!TryParse(value, out var anchor))
        {
            throw new FormatException($"'{value}' is not a valid Flow document anchor.");
        }

        return anchor;
    }

    public static bool TryParse(string? value, [NotNullWhen(true)] out DocumentAnchor? anchor)
    {
        anchor = null;

        if (value is null || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var path = value[Prefix.Length..];
        if (path.Length == 0)
        {
            return false;
        }

        var rawSegments = path.Split('/');
        if (rawSegments.Length > MaximumSegmentCount)
        {
            return false;
        }

        var segments = ImmutableArray.CreateBuilder<NodeId>(rawSegments.Length);
        foreach (var rawSegment in rawSegments)
        {
            if (!NodeId.TryParse(rawSegment, out var segment))
            {
                return false;
            }

            segments.Add(segment);
        }

        anchor = new DocumentAnchor(segments.MoveToImmutable());
        return true;
    }

    public bool Equals(DocumentAnchor? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is DocumentAnchor other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;
}
