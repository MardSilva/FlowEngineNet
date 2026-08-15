namespace Flow.Epub;

public enum EpubDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record EpubDiagnostic(
    string Code,
    EpubDiagnosticSeverity Severity,
    string Message,
    string? Resource = null);

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
}
