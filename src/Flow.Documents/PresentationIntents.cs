namespace Flow.Documents;

/// <summary>Identifies the semantic figure that represents a publication cover.</summary>
public sealed record CoverPresentation
{
    public CoverPresentation(Flow.Core.NodeId figureId)
    {
        ArgumentNullException.ThrowIfNull(figureId);
        FigureId = figureId;
    }

    public Flow.Core.NodeId FigureId { get; }
}

public sealed record HeadingPresentation
{
    public HeadingPresentation(bool? keepWithNext = null, bool? avoidBreakAfter = null)
    {
        KeepWithNext = keepWithNext;
        AvoidBreakAfter = avoidBreakAfter;
    }

    public bool? KeepWithNext { get; }

    public bool? AvoidBreakAfter { get; }
}

public sealed record ParagraphPresentation
{
    public ParagraphPresentation(bool? keepTogether = null, bool? keepWithNext = null)
    {
        KeepTogether = keepTogether;
        KeepWithNext = keepWithNext;
    }

    public bool? KeepTogether { get; }

    public bool? KeepWithNext { get; }
}

public sealed record FigurePresentation
{
    public FigurePresentation(
        FigureImportance? importance = null,
        bool? keepWithCaption = null,
        PreferredPlacement? preferredPlacement = null,
        Length? maximumWidth = null)
    {
        PresentationIntentValidation.ValidateEnum(importance, nameof(importance));
        PresentationIntentValidation.ValidateEnum(preferredPlacement, nameof(preferredPlacement));
        PresentationIntentValidation.ValidatePositiveLength(maximumWidth, nameof(maximumWidth));

        Importance = importance;
        KeepWithCaption = keepWithCaption;
        PreferredPlacement = preferredPlacement;
        MaximumWidth = maximumWidth;
    }

    public FigureImportance? Importance { get; }

    public bool? KeepWithCaption { get; }

    public PreferredPlacement? PreferredPlacement { get; }

    public Length? MaximumWidth { get; }
}

public sealed record CaptionPresentation
{
    public CaptionPresentation(bool? keepWithFigure = null)
    {
        KeepWithFigure = keepWithFigure;
    }

    public bool? KeepWithFigure { get; }
}

public sealed record FootnotePresentation
{
    public FootnotePresentation(FootnotePresentationMode? preferredPresentation = null)
    {
        PresentationIntentValidation.ValidateEnum(preferredPresentation, nameof(preferredPresentation));
        PreferredPresentation = preferredPresentation;
    }

    public FootnotePresentationMode? PreferredPresentation { get; }
}

public sealed record CodeBlockPresentation
{
    public CodeBlockPresentation(bool? avoidSplit = null, bool? preserveWhitespace = null)
    {
        AvoidSplit = avoidSplit;
        PreserveWhitespace = preserveWhitespace;
    }

    public bool? AvoidSplit { get; }

    public bool? PreserveWhitespace { get; }
}

public sealed record TableOfContentsPresentation
{
    public TableOfContentsPresentation(
        bool? generateFromDocumentStructure = null,
        TableOfContentsLeaderStyle? leaderStyle = null)
    {
        PresentationIntentValidation.ValidateEnum(leaderStyle, nameof(leaderStyle));

        GenerateFromDocumentStructure = generateFromDocumentStructure;
        LeaderStyle = leaderStyle;
    }

    public bool? GenerateFromDocumentStructure { get; }

    public TableOfContentsLeaderStyle? LeaderStyle { get; }
}
