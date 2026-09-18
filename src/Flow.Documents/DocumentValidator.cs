using System.Collections.Immutable;
using Flow.Core;

namespace Flow.Documents;

/// <summary>Validates document-wide semantic, hierarchy, asset, and reference invariants.</summary>
public sealed class DocumentValidator
{
    /// <summary>Validates a complete immutable document without modifying it.</summary>
    /// <param name="document">The document to validate.</param>
    /// <returns>All diagnostics found in deterministic validation-pass order.</returns>
    public ValidationResult Validate(FlowDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = ImmutableArray.CreateBuilder<ValidationDiagnostic>();

        ValidateIds(document, diagnostics);
        ValidateHierarchy(document, diagnostics);
        ValidateHeadingLevels(document, diagnostics);
        ValidateFigures(document, diagnostics);
        ValidatePresentation(document, diagnostics);
        ValidateReferences(document, diagnostics);
        ValidateTables(document, diagnostics);
        ValidateTableOfContents(document, diagnostics);
        ValidateInternationalization(document, diagnostics);

        return new ValidationResult(diagnostics);
    }

    private static void ValidatePresentation(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        if (document.Presentation?.Cover is not { } cover)
        {
            return;
        }

        if (!document.Index.TryGetUniqueNode(cover.FigureId, out var node) || node is not Figure)
        {
            diagnostics.Add(Error(
                ValidationDiagnosticCodes.InvalidCoverFigure,
                $"Cover intent references '{cover.FigureId}', which is not one unique figure in the document.",
                cover.FigureId));
        }
    }

    private static void ValidateIds(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        foreach (var location in document.Index.Locations)
        {
            if (!NodeId.TryParse(location.Node.Id.Value, out _))
            {
                diagnostics.Add(Error(
                    ValidationDiagnosticCodes.InvalidNodeId,
                    $"Node ID '{location.Node.Id.Value}' is invalid.",
                    location.Node.Id));
            }
        }

        foreach (var duplicateId in document.Index.DuplicateIds)
        {
            diagnostics.Add(Error(
                ValidationDiagnosticCodes.DuplicateNodeId,
                $"Node ID '{duplicateId}' occurs {document.Index.GetLocations(duplicateId).Length} times.",
                duplicateId));
        }
    }

    private static void ValidateHierarchy(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        foreach (var location in document.Index.Locations)
        {
            var isValid = location.Node switch
            {
                Chapter => location.Parent is null,
                Section => location.Parent is Chapter or Section or TableCellNode or TableCaption,
                ListItem => location.Parent is OrderedList or UnorderedList,
                Caption => location.Parent is Figure,
                TableCaption => location.Parent is Table,
                TableHead or TableBody or TableFoot => location.Parent is Table,
                TableRow => location.Parent is TableHead or TableBody or TableFoot,
                TableCellNode => location.Parent is TableRow,
                Footnote when location.Parent is Footnote => false,
                _ => true,
            };

            if (!isValid)
            {
                var parentName = location.Parent?.GetType().Name ?? "the document root";
                diagnostics.Add(Error(
                    ValidationDiagnosticCodes.InvalidHierarchy,
                    $"{location.Node.GetType().Name} '{location.Node.Id}' is not valid inside {parentName}.",
                    location.Node.Id));
            }
        }
    }

    private static void ValidateHeadingLevels(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        IEqualityComparer<DocumentNode> nodeComparer = ReferenceEqualityComparer.Instance;
        Heading? previousRootHeading = null;
        var previousByChapter = new Dictionary<Chapter, Heading>(ReferenceEqualityComparer.Instance);
        var parents = document.Index.Locations.ToDictionary(
            static location => location.Node,
            static location => location.Parent,
            nodeComparer);

        foreach (var location in document.Index.Locations)
        {
            if (location.Node is not Heading heading)
            {
                continue;
            }

            var chapter = FindChapter(location.Parent, parents);
            var previousHeading = chapter is null
                ? previousRootHeading
                : previousByChapter.GetValueOrDefault(chapter);

            if (heading.Level is < 1 or > 6)
            {
                diagnostics.Add(Error(
                    ValidationDiagnosticCodes.InvalidHeadingLevel,
                    $"Heading '{heading.Id}' has level {heading.Level}; valid levels are 1 through 6.",
                    heading.Id));
            }
            else if (previousHeading is not null && heading.Level > previousHeading.Level + 1)
            {
                diagnostics.Add(Error(
                    ValidationDiagnosticCodes.InvalidHeadingLevel,
                    $"Heading '{heading.Id}' jumps from level {previousHeading.Level} to level {heading.Level}.",
                    heading.Id));
            }

            if (chapter is null)
            {
                previousRootHeading = heading;
            }
            else
            {
                previousByChapter[chapter] = heading;
            }
        }

        static Chapter? FindChapter(
            DocumentNode? node,
            IReadOnlyDictionary<DocumentNode, DocumentNode?> parents)
        {
            while (node is not null)
            {
                if (node is Chapter chapter)
                {
                    return chapter;
                }

                node = parents.GetValueOrDefault(node);
            }

            return null;
        }
    }

    private static void ValidateFigures(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        foreach (var figure in document.Index.Locations.Select(static location => location.Node).OfType<Figure>())
        {
            if (!document.Assets.ContainsKey(figure.AssetId))
            {
                diagnostics.Add(Error(
                    ValidationDiagnosticCodes.MissingFigureAsset,
                    $"Figure '{figure.Id}' references missing asset '{figure.AssetId}'.",
                    figure.Id));
            }

            if (figure.Link?.Anchor is { } anchor && !document.TryResolveAnchor(anchor, out _))
            {
                diagnostics.Add(Error(
                    ValidationDiagnosticCodes.UnresolvedAnchor,
                    $"Figure '{figure.Id}' targets unresolved anchor '{anchor}'.",
                    figure.Id,
                    anchor));
            }
        }
    }

    private static void ValidateReferences(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        foreach (var location in document.Index.Locations)
        {
            foreach (var inline in GetInlineDescendants(location.Node))
            {
                switch (inline)
                {
                    case Link link when link.Target.StartsWith("flow:", StringComparison.OrdinalIgnoreCase):
                        ValidateLink(document, location.Node.Id, link, diagnostics);
                        break;
                    case FootnoteReference reference:
                        ValidateFootnoteReference(document, location.Node.Id, reference, diagnostics);
                        break;
                }
            }
        }
    }

    private static void ValidateLink(
        FlowDocument document,
        NodeId sourceId,
        Link link,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        if (!DocumentAnchor.TryParse(link.Target, out var anchor))
        {
            diagnostics.Add(Error(
                ValidationDiagnosticCodes.InvalidAnchor,
                $"Link in node '{sourceId}' contains invalid Flow anchor '{link.Target}'.",
                sourceId));
            return;
        }

        if (!document.TryResolveAnchor(anchor, out _))
        {
            diagnostics.Add(Error(
                ValidationDiagnosticCodes.UnresolvedAnchor,
                $"Link in node '{sourceId}' targets unresolved anchor '{anchor}'.",
                sourceId,
                anchor));
        }
    }

    private static void ValidateFootnoteReference(
        FlowDocument document,
        NodeId sourceId,
        FootnoteReference reference,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        if (!document.Index.TryGetUniqueNode(reference.TargetId, out var target) || target is not Footnote)
        {
            diagnostics.Add(Error(
                ValidationDiagnosticCodes.UnresolvedFootnoteReference,
                $"Footnote reference in node '{sourceId}' does not target one unique footnote '{reference.TargetId}'.",
                sourceId));
        }
    }

    private static void ValidateTableOfContents(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        foreach (var tableOfContents in document.Index.Locations
                     .Select(static location => location.Node)
                     .OfType<TableOfContents>())
        {
            foreach (var entry in tableOfContents.Entries)
            {
                var resolves = document.TryResolveAnchor(entry.Target, out var target);
                var validTargetType = target is Chapter or Section or Heading;
                var validDepth = entry.Level <= tableOfContents.MaximumDepth;

                if (!resolves || !validTargetType || !validDepth)
                {
                    diagnostics.Add(Error(
                        ValidationDiagnosticCodes.InvalidTableOfContentsTarget,
                        $"Table of contents '{tableOfContents.Id}' has an invalid target '{entry.Target}' at level {entry.Level}.",
                        tableOfContents.Id,
                        entry.Target));
                }
            }
        }
    }

    private static void ValidateTables(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        foreach (var table in document.Index.Locations.Select(static location => location.Node).OfType<Table>())
        {
            var tableNodes = Descendants(table).ToArray();
            var headers = tableNodes.OfType<TableHeaderCell>().Select(static cell => cell.Id).ToHashSet();
            foreach (var cell in tableNodes.OfType<TableCellNode>())
            {
                foreach (var headerId in cell.Headers)
                {
                    if (!headers.Contains(headerId))
                    {
                        diagnostics.Add(Error(
                            ValidationDiagnosticCodes.UnresolvedTableHeaderReference,
                            $"Table cell '{cell.Id}' references header '{headerId}', which is not one unique header cell in table '{table.Id}'.",
                            cell.Id));
                    }
                }
            }
        }

        static IEnumerable<DocumentNode> Descendants(DocumentNode root)
        {
            foreach (var child in DocumentNodeTraversal.GetChildren(root))
            {
                if (child is Table)
                {
                    continue;
                }

                yield return child;
                foreach (var descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static IEnumerable<InlineNode> GetInlineDescendants(DocumentNode node)
    {
        IEnumerable<InlineNode> roots = node switch
        {
            Heading heading => heading.Content,
            Paragraph paragraph => paragraph.Content,
            Caption caption => caption.Content,
            TableOfContents tableOfContents => tableOfContents.Title.Concat(
                tableOfContents.Entries.SelectMany(static entry => entry.Label)),
            _ => [],
        };

        foreach (var root in roots)
        {
            yield return root;

            if (root is InlineContainerNode container)
            {
                foreach (var descendant in GetInlineDescendants(container.Children))
                {
                    yield return descendant;
                }
            }
            else if (root is FootnoteReference reference)
            {
                foreach (var descendant in GetInlineDescendants(reference.Label))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static void ValidateInternationalization(
        FlowDocument document,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        foreach (var location in document.Index.Locations)
        {
            foreach (var root in GetInlineRoots(location.Node))
            {
                ValidateInline(root, parent: null, insideRuby: false, location.Node.Id, diagnostics);
            }
        }
    }

    private static void ValidateInline(
        InlineNode node,
        InlineNode? parent,
        bool insideRuby,
        NodeId ownerId,
        ImmutableArray<ValidationDiagnostic>.Builder diagnostics)
    {
        if (node is LanguageSpan languageSpan && !LanguageTag.TryParse(languageSpan.Language.Value, out _))
        {
            diagnostics.Add(Error(
                ValidationDiagnosticCodes.InvalidLanguageTag,
                $"Inline language '{languageSpan.Language.Value}' in node '{ownerId}' is invalid.",
                ownerId));
        }

        if (node is BidirectionalSpan bidirectional
            && (!Enum.IsDefined(bidirectional.Direction)
                || !Enum.IsDefined(bidirectional.Mode)
                || bidirectional.Mode == BidirectionalMode.Override
                && bidirectional.Direction == TextDirection.Auto))
        {
            diagnostics.Add(Error(
                ValidationDiagnosticCodes.InvalidBidirectionalStructure,
                $"Bidirectional span in node '{ownerId}' has an invalid direction/mode combination.",
                ownerId));
        }

        if (node is RubyAnnotation or RubyFallbackParenthesis && parent is not Ruby)
        {
            diagnostics.Add(Error(
                ValidationDiagnosticCodes.InvalidRubyStructure,
                $"{node.GetType().Name} in node '{ownerId}' must be a direct child of Ruby.",
                ownerId));
        }

        if (node is Ruby ruby)
        {
            var hasAnnotation = ruby.Children.Any(static child => child is RubyAnnotation);
            var hasBase = ruby.Children.Any(static child => child is not RubyAnnotation and not RubyFallbackParenthesis);
            if (insideRuby || !hasAnnotation || !hasBase)
            {
                diagnostics.Add(Error(
                    ValidationDiagnosticCodes.InvalidRubyStructure,
                    $"Ruby in node '{ownerId}' must have base content and an annotation and cannot be nested.",
                    ownerId));
            }
        }

        if (node is InlineContainerNode container)
        {
            foreach (var child in container.Children)
            {
                ValidateInline(child, node, insideRuby || node is Ruby, ownerId, diagnostics);
            }
        }
        else if (node is FootnoteReference reference)
        {
            foreach (var child in reference.Label)
            {
                ValidateInline(child, node, insideRuby, ownerId, diagnostics);
            }
        }
    }

    private static IEnumerable<InlineNode> GetInlineRoots(DocumentNode node) => node switch
    {
        Heading heading => heading.Content,
        Paragraph paragraph => paragraph.Content,
        Caption caption => caption.Content,
        TableOfContents tableOfContents => tableOfContents.Title.Concat(
            tableOfContents.Entries.SelectMany(static entry => entry.Label)),
        _ => [],
    };

    private static IEnumerable<InlineNode> GetInlineDescendants(IEnumerable<InlineNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;

            if (root is InlineContainerNode container)
            {
                foreach (var descendant in GetInlineDescendants(container.Children))
                {
                    yield return descendant;
                }
            }
            else if (root is FootnoteReference reference)
            {
                foreach (var descendant in GetInlineDescendants(reference.Label))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static ValidationDiagnostic Error(
        string code,
        string message,
        NodeId? nodeId = null,
        DocumentAnchor? anchor = null) =>
        new(code, ValidationSeverity.Error, message, nodeId, anchor);
}
