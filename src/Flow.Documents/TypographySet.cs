using System.Collections.Immutable;

namespace Flow.Documents;

public sealed record TypographySet
{
    public TypographySet()
        : this([])
    {
    }

    public TypographySet(IEnumerable<KeyValuePair<TypographyRole, TypographyStyle>> styles)
    {
        ArgumentNullException.ThrowIfNull(styles);

        var builder = ImmutableDictionary.CreateBuilder<TypographyRole, TypographyStyle>();
        foreach (var pair in styles)
        {
            ArgumentNullException.ThrowIfNull(pair.Value);

            if (!Enum.IsDefined(pair.Key))
            {
                throw new ArgumentOutOfRangeException(nameof(styles), pair.Key, "The typography role is not defined.");
            }

            if (!builder.TryAdd(pair.Key, pair.Value))
            {
                throw new ArgumentException($"Typography role '{pair.Key}' occurs more than once.", nameof(styles));
            }
        }

        Styles = builder.ToImmutable();
    }

    public ImmutableDictionary<TypographyRole, TypographyStyle> Styles { get; }

    public TypographyStyle? this[TypographyRole role] =>
        Styles.TryGetValue(role, out var style) ? style : null;

    public bool TryGetStyle(TypographyRole role, out TypographyStyle? style) =>
        Styles.TryGetValue(role, out style);
}
