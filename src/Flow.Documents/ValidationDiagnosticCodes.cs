namespace Flow.Documents;

public static class ValidationDiagnosticCodes
{
    public const string DuplicateNodeId = "FLOW_DUPLICATE_NODE_ID";
    public const string InvalidNodeId = "FLOW_INVALID_NODE_ID";
    public const string InvalidHierarchy = "FLOW_INVALID_HIERARCHY";
    public const string InvalidHeadingLevel = "FLOW_INVALID_HEADING_LEVEL";
    public const string MissingFigureAsset = "FLOW_MISSING_FIGURE_ASSET";
    public const string InvalidAnchor = "FLOW_INVALID_ANCHOR";
    public const string UnresolvedAnchor = "FLOW_UNRESOLVED_ANCHOR";
    public const string UnresolvedFootnoteReference = "FLOW_UNRESOLVED_FOOTNOTE_REFERENCE";
    public const string InvalidTableOfContentsTarget = "FLOW_INVALID_TOC_TARGET";
    public const string UnresolvedTableHeaderReference = "FLOW_UNRESOLVED_TABLE_HEADER_REFERENCE";
    public const string InvalidLanguageTag = "FLOW_INVALID_LANGUAGE_TAG";
    public const string InvalidBidirectionalStructure = "FLOW_INVALID_BIDIRECTIONAL_STRUCTURE";
    public const string InvalidRubyStructure = "FLOW_INVALID_RUBY_STRUCTURE";
}
