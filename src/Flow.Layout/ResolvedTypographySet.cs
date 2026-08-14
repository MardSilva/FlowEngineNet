using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Flow.Documents;

namespace Flow.Layout;

public sealed record ResolvedTypographySet
{
    internal ResolvedTypographySet(IEnumerable<KeyValuePair<TypographyRole, ResolvedTypographyStyle>> styles)
    {
        Styles = styles.ToImmutableDictionary();

        foreach (var role in Enum.GetValues<TypographyRole>())
        {
            if (!Styles.ContainsKey(role))
            {
                throw new ArgumentException($"Resolved typography is missing role '{role}'.", nameof(styles));
            }
        }
    }

    public ImmutableDictionary<TypographyRole, ResolvedTypographyStyle> Styles { get; }

    public ResolvedTypographyStyle this[TypographyRole role] => Styles[role];

    public bool TryGetStyle(
        TypographyRole role,
        [NotNullWhen(true)] out ResolvedTypographyStyle? style) =>
        Styles.TryGetValue(role, out style);
}
