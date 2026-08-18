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
    string? Resource = null);

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
}
