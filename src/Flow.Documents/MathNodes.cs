using System.Collections.Immutable;
using Flow.Core;

namespace Flow.Documents;

/// <summary>A safe, renderer-neutral node in a restricted Presentation MathML tree.</summary>
public abstract record MathNode;

/// <summary>Represents textual mathematical content.</summary>
public sealed record MathText : MathNode
{
    public MathText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Value = value;
    }

    public string Value { get; }
}

/// <summary>Represents an allow-listed Presentation MathML element without executable or linking attributes.</summary>
public sealed record MathElement : MathNode
{
    private static readonly ImmutableHashSet<string> AllowedNames = new[]
    {
        "math", "mrow", "mi", "mn", "mo", "mtext", "ms", "mspace", "mfrac", "msqrt", "mroot",
        "msub", "msup", "msubsup", "munder", "mover", "munderover", "mmultiscripts", "mprescripts",
        "none", "mfenced", "menclose", "mtable", "mtr", "mlabeledtr", "mtd", "maligngroup",
        "malignmark", "mstyle", "merror", "semantics", "annotation",
    }.ToImmutableHashSet(StringComparer.Ordinal);

    private static readonly ImmutableHashSet<string> AllowedAttributeNames = new[]
    {
        "accent", "accentunder", "align", "close", "columnalign", "columnlines", "columnspacing",
        "columnspan", "denomalign", "depth", "display", "encoding", "fence", "form", "frame", "height",
        "linethickness", "lspace", "mathbackground", "mathcolor", "mathsize", "mathvariant", "maxsize",
        "minsize", "movablelimits", "notation", "numalign", "open", "rowalign", "rowlines", "rowspacing",
        "rowspan", "rspace", "separator", "separators", "stretchy", "symmetric", "width",
    }.ToImmutableHashSet(StringComparer.Ordinal);

    public MathElement(
        string name,
        IEnumerable<MathNode>? children = null,
        IEnumerable<KeyValuePair<string, string>>? attributes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!AllowedNames.Contains(name))
        {
            throw new ArgumentException($"Math element '{name}' is outside the safe Flow vocabulary.", nameof(name));
        }

        Name = name;
        Children = (children ?? []).ToImmutableArray();
        if (Children.Any(static child => child is null))
        {
            throw new ArgumentException("Math children cannot contain null values.", nameof(children));
        }

        var attributeBuilder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var attribute in attributes ?? [])
        {
            if (!AllowedAttributeNames.Contains(attribute.Key))
            {
                throw new ArgumentException(
                    $"Math attribute '{attribute.Key}' is outside the safe Flow vocabulary.",
                    nameof(attributes));
            }

            ArgumentNullException.ThrowIfNull(attribute.Value);
            if (attribute.Value.Any(char.IsControl))
            {
                throw new ArgumentException("Math attribute values cannot contain control characters.", nameof(attributes));
            }

            attributeBuilder.Add(attribute.Key, attribute.Value);
        }

        Attributes = attributeBuilder.ToImmutable();
    }

    public string Name { get; }

    public ImmutableArray<MathNode> Children { get; }

    public ImmutableSortedDictionary<string, string> Attributes { get; }

    public static bool IsSupportedName(string name) => AllowedNames.Contains(name);

    public static bool IsSupportedAttribute(string name) => AllowedAttributeNames.Contains(name);
}

/// <summary>Represents a block mathematical expression preserved as a safe structural tree.</summary>
public sealed record MathExpression : DocumentNode
{
    public MathExpression(NodeId id, MathElement root, string? alternativeText = null)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.Name != "math")
        {
            throw new ArgumentException("A mathematical expression must have a math root element.", nameof(root));
        }

        Root = root;
        AlternativeText = alternativeText;
    }

    public MathElement Root { get; }

    public string? AlternativeText { get; }
}

/// <summary>Represents an inline mathematical expression preserved as a safe structural tree.</summary>
public sealed record InlineMath : InlineNode
{
    public InlineMath(MathElement root, string? alternativeText = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (root.Name != "math")
        {
            throw new ArgumentException("Inline mathematics must have a math root element.", nameof(root));
        }

        Root = root;
        AlternativeText = alternativeText;
    }

    public MathElement Root { get; }

    public string? AlternativeText { get; }
}
