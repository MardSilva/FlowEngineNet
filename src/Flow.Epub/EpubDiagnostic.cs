namespace Flow.Epub;

/// <summary>Classifies the effect of an EPUB import diagnostic.</summary>
public enum EpubDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>Describes one EPUB structural, security, or fidelity finding.</summary>
public sealed record EpubDiagnostic(
    string Code,
    EpubDiagnosticSeverity Severity,
    string Message,
    string? Resource = null,
    int Count = 1)
{
    public int Count { get; init; } = Count > 0
        ? Count
        : throw new ArgumentOutOfRangeException(nameof(Count), "A diagnostic count must be positive.");
}

/// <summary>Defines stable diagnostic codes emitted by the EPUB import prototype.</summary>
public static class EpubDiagnosticCodes
{
    public const string InvalidArchive = "EPUB001";
    public const string ArchiveLimitExceeded = "EPUB002";
    public const string UnsafePath = "EPUB003";
    public const string MissingContainer = "EPUB004";
    public const string InvalidXml = "EPUB005";
    public const string MissingPackage = "EPUB006";
    public const string InvalidPackage = "EPUB007";
    public const string MissingResource = "EPUB008";
    public const string UnsupportedResource = "EPUB009";
    public const string UnsupportedElement = "EPUB010";
    public const string InvalidReference = "EPUB011";
    public const string MetadataFallback = "EPUB012";
    public const string NonLinearSpineItem = "EPUB013";
    public const string DocumentValidation = "EPUB014";
    public const string UnsupportedVersion = "EPUB015";
    public const string MultipleTableOfContents = "EPUB016";
    public const string InvalidTableOfContents = "EPUB017";
    public const string MissingTableOfContentsTarget = "EPUB018";
    public const string EmptyTableOfContentsEntry = "EPUB019";
    public const string InvalidTableOfContentsLevel = "EPUB020";
    public const string CircularTableOfContentsReference = "EPUB021";
    public const string TableOfContentsConflict = "EPUB022";
    public const string InvalidMetadata = "EPUB023";
    public const string OrphanMetadataRefinement = "EPUB024";
    public const string MetadataConflict = "EPUB025";
    public const string InvalidLanguage = "EPUB026";
    public const string MissingIdentifier = "EPUB027";
    public const string CircularFallback = "EPUB028";
    public const string BrokenFallback = "EPUB029";
    public const string RepeatedSpineItem = "EPUB030";
    public const string UnsupportedMediaOverlay = "EPUB031";
    public const string InvalidSpineLinearity = "EPUB032";
    public const string DuplicateManifestResource = "EPUB033";
    public const string DuplicateSourceId = "EPUB034";
    public const string InvalidSourceId = "EPUB035";
    public const string SourceIdCollision = "EPUB036";
    public const string UnmappedSourceLocation = "EPUB037";
    public const string InvalidImageData = "EPUB038";
    public const string ImageMediaTypeMismatch = "EPUB039";
    public const string ImageDimensionsExceeded = "EPUB040";
    public const string MissingImageAlternativeText = "EPUB041";
    public const string UnsupportedImageFormat = "EPUB042";
    public const string UnsafeSvg = "EPUB043";
    public const string ImageFallbackUsed = "EPUB044";
    public const string ImageDeduplicated = "EPUB045";
    public const string ImageBytesExceeded = "EPUB046";
    public const string InvalidStylesheet = "EPUB047";
    public const string UnsupportedCssSelector = "EPUB048";
    public const string UnsupportedCssProperty = "EPUB049";
    public const string SanitizedSvg = "EPUB062";
    public const string MathSemanticLoss = "EPUB063";
    public const string InvalidTextDirection = "EPUB064";
    public const string ConflictingInlineLanguage = "EPUB065";
    public const string InvalidRubyStructure = "EPUB066";
    public const string InvalidSvgImageReference = "EPUB067";
    public const string SvgImageFallbackUsed = "EPUB068";
    public const string SvgImageSemanticLoss = "EPUB069";
    public const string UnsupportedManifestProperty = "EPUB070";
    public const string HeadingLevelNormalized = "EPUB071";
    public const string NoteResourceFallbackUsed = "EPUB072";
    public const string LinkedImageTargetNotRepresentable = "EPUB073";
    public const string EmbeddedFontBytesNotPreserved = "EPUB074";
    public const string TransparentContainerTransformed = "EPUB075";
    public const string InvalidCssValue = "EPUB050";
    public const string ExternalStylesheetBlocked = "EPUB051";
    public const string CssTargetNotRepresentable = "EPUB052";
    public const string OrphanNoteReference = "EPUB053";
    public const string UnreferencedNote = "EPUB054";
    public const string MissingNoteBacklink = "EPUB055";
    public const string CircularNoteReference = "EPUB056";
    public const string AmbiguousNoteDestination = "EPUB057";
    public const string InvalidTableStructure = "EPUB058";
    public const string InvalidTableSpan = "EPUB059";
    public const string InvalidTableScope = "EPUB060";
    public const string MissingTableHeader = "EPUB061";
}
