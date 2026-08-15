using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Flow.Core;

/// <summary>Represents an immutable semantic path in a Flow document.</summary>
/// <remarks>Anchors use the <c>flow:&lt;node-id&gt;[/&lt;node-id&gt;...]</c> grammar.</remarks>
public sealed class DocumentAnchor : IEquatable<DocumentAnchor>
{
    private const string Prefix = "flow:";
    private const int MaximumSegmentCount = 64;

    private DocumentAnchor(ImmutableArray<NodeId> segments)
    {
        Segments = segments;
        Value = Prefix + string.Join('/', segments.Select(static segment => segment.Value));
    }

    /// <summary>Gets the ordered path segments.</summary>
    public ImmutableArray<NodeId> Segments { get; }

    /// <summary>Gets the final node identifier addressed by this anchor.</summary>
    public NodeId TargetId => Segments[^1];

    /// <summary>Gets the canonical textual representation.</summary>
    public string Value { get; }

    /// <summary>Creates an anchor from one or more semantic path segments.</summary>
    /// <param name="segments">The path from an optional ancestor to the target node.</param>
    /// <returns>A canonical Flow anchor.</returns>
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

    /// <summary>Parses a Flow anchor.</summary>
    /// <param name="value">The canonical anchor text.</param>
    /// <returns>The parsed anchor.</returns>
    /// <exception cref="FormatException">The value does not follow the Flow anchor grammar.</exception>
    public static DocumentAnchor Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!TryParse(value, out var anchor))
        {
            throw new FormatException($"'{value}' is not a valid Flow document anchor.");
        }

        return anchor;
    }

    /// <summary>Attempts to parse a Flow anchor without throwing for invalid syntax.</summary>
    /// <param name="value">The candidate anchor text.</param>
    /// <param name="anchor">The parsed anchor when successful.</param>
    /// <returns><see langword="true" /> when the value is a valid Flow anchor.</returns>
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

    /// <inheritdoc />
    public bool Equals(DocumentAnchor? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DocumentAnchor other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
