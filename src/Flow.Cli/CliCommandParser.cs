using System.Globalization;
using Flow.Epub;
using Flow.Rendering.Html;

namespace Flow.Cli;

/// <summary>Parses framework-free command-line arguments into typed Flow CLI commands.</summary>
public sealed class CliCommandParser
{
    private static readonly IReadOnlyDictionary<string, Func<IReadOnlyList<string>, CommandParseResult>> Parsers =
        new Dictionary<string, Func<IReadOnlyList<string>, CommandParseResult>>(StringComparer.Ordinal)
        {
            ["menu"] = ParseMenu,
            ["sample"] = ParseSample,
            ["import"] = ParseImport,
            ["epub-inspect"] = ParseEpubInspect,
            ["epub-inventory"] = ParseEpubInventory,
            ["epub-inventory-qualify"] = ParseEpubInventoryQualify,
            ["epub-inventory-matrix"] = ParseEpubInventoryMatrix,
            ["epub-inventory-review"] = ParseEpubInventoryReview,
            ["corpus"] = ParseCorpus,
            ["epub-qualify"] = ParseEpubQualify,
            ["epub-review"] = ParseEpubReview,
            ["execution-status"] = ParseExecutionStatus,
            ["execution-clean"] = ParseExecutionClean,
            ["inspect"] = static arguments => ParseDocumentCommand(arguments, static path => new InspectCommand(path)),
            ["validate"] = static arguments => ParseDocumentCommand(arguments, static path => new ValidateCommand(path)),
            ["hash"] = static arguments => ParseDocumentCommand(arguments, static path => new HashCommand(path)),
            ["render"] = ParseRender,
        };

    internal static IReadOnlyCollection<string> SupportedCommandNames { get; } =
        ["help", .. Parsers.Keys];

    /// <summary>Parses one complete argument vector.</summary>
    public CommandParseResult Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        if (arguments.Count == 0)
        {
            return CommandParseResult.Success(new HelpCommand());
        }

        if (arguments[0] is "--help" or "-h")
        {
            return arguments.Count == 1
                ? CommandParseResult.Success(new HelpCommand())
                : CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", "flow help [command]");
        }

        if (arguments[0] == "help")
        {
            if (arguments.Count == 1)
            {
                return CommandParseResult.Success(new HelpCommand());
            }

            if (arguments.Count != 2)
            {
                return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", "flow help [command]");
            }

            var requestedCommand = arguments[1] is "--help" or "-h" ? "help" : arguments[1];
            return SupportedCommandNames.Contains(requestedCommand, StringComparer.Ordinal)
                ? CommandParseResult.Success(new HelpCommand(requestedCommand))
                : CommandParseResult.Failure(
                    "FLOWCLI_UNKNOWN_HELP_COMMAND",
                    "ErrorUnknownHelpCommand",
                    requestedCommand);
        }

        if (arguments.Count == 2 && (arguments[1] is "--help" or "-h"))
        {
            return Parsers.ContainsKey(arguments[0])
                ? CommandParseResult.Success(new HelpCommand(arguments[0]))
                : CommandParseResult.Failure(
                    "FLOWCLI_UNKNOWN_HELP_COMMAND",
                    "ErrorUnknownHelpCommand",
                    arguments[0]);
        }

        return Parsers.TryGetValue(arguments[0], out var parser)
            ? parser(arguments)
            : CommandParseResult.Failure(
                "FLOWCLI_UNKNOWN_COMMAND",
                "ErrorUnknownCommand",
                arguments[0]);
    }

    private static CommandParseResult ParseMenu(IReadOnlyList<string> arguments) =>
        arguments.Count == 1
            ? CommandParseResult.Success(new MenuCommand())
            : CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", "flow menu");

    private static CommandParseResult ParseImport(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", ImportUsage);
        }

        string? outputPath = null;
        string? diagnosticsJsonOutputPath = null;
        string? fidelityReportOutputPath = null;
        string? metadataJsonOutputPath = null;
        string? processingJsonOutputPath = null;
        string? sourceMapJsonOutputPath = null;
        for (var index = 2; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                return CommandParseResult.Failure(
                    "FLOWCLI_USAGE",
                    "ErrorOptionRequiresValue",
                    arguments[index],
                    ImportUsage);
            }

            var option = arguments[index];
            var value = arguments[index + 1];
            switch (option)
            {
                case "--output" when outputPath is null:
                    outputPath = value;
                    break;
                case "--diagnostics-json" when diagnosticsJsonOutputPath is null:
                    diagnosticsJsonOutputPath = value;
                    break;
                case "--fidelity-report" when fidelityReportOutputPath is null:
                    fidelityReportOutputPath = value;
                    break;
                case "--metadata-json" when metadataJsonOutputPath is null:
                    metadataJsonOutputPath = value;
                    break;
                case "--processing-json" when processingJsonOutputPath is null:
                    processingJsonOutputPath = value;
                    break;
                case "--source-map-json" when sourceMapJsonOutputPath is null:
                    sourceMapJsonOutputPath = value;
                    break;
                case "--output" or "--diagnostics-json" or "--fidelity-report" or "--metadata-json"
                    or "--processing-json" or "--source-map-json":
                    return CommandParseResult.Failure(
                        "FLOWCLI_DUPLICATE_OPTION",
                        "ErrorDuplicateOption",
                        option);
                default:
                    return CommandParseResult.Failure(
                        "FLOWCLI_UNKNOWN_OPTION",
                        "ErrorUnknownOption",
                        "import",
                        option,
                        ImportUsage);
            }
        }

        return CommandParseResult.Success(
            new ImportEpubCommand(
                arguments[1],
                outputPath,
                diagnosticsJsonOutputPath,
                fidelityReportOutputPath,
                metadataJsonOutputPath,
                processingJsonOutputPath,
                sourceMapJsonOutputPath));
    }

    private static CommandParseResult ParseEpubInspect(IReadOnlyList<string> arguments) =>
        arguments.Count switch
        {
            2 => CommandParseResult.Success(new InspectEpubCommand(arguments[1], null)),
            4 when arguments[2] == "--json" && !string.IsNullOrWhiteSpace(arguments[3]) =>
                CommandParseResult.Success(new InspectEpubCommand(arguments[1], arguments[3])),
            _ => CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", EpubInspectUsage),
        };

    private static CommandParseResult ParseEpubInventory(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", EpubInventoryUsage);
        }

        string? outputPath = null;
        string? repositoryRoot = null;
        var force = false;
        for (var index = 2; index < arguments.Count; index++)
        {
            var option = arguments[index];
            if (option == "--force")
            {
                if (force)
                {
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
                }

                force = true;
                continue;
            }

            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                return CommandParseResult.Failure(
                    "FLOWCLI_USAGE",
                    "ErrorOptionRequiresValue",
                    option,
                    EpubInventoryUsage);
            }

            var value = arguments[index];
            switch (option)
            {
                case "--output" when outputPath is null:
                    outputPath = value;
                    break;
                case "--repository-root" when repositoryRoot is null:
                    repositoryRoot = value;
                    break;
                case "--output" or "--repository-root":
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
                default:
                    return CommandParseResult.Failure(
                        "FLOWCLI_UNKNOWN_OPTION",
                        "ErrorUnknownOption",
                        "epub-inventory",
                        option,
                        EpubInventoryUsage);
            }
        }

        return outputPath is null || repositoryRoot is null
            ? CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", EpubInventoryUsage)
            : CommandParseResult.Success(
                new InventoryEpubCommand(arguments[1], outputPath, repositoryRoot, force));
    }

    private static CommandParseResult ParseCorpus(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", CorpusUsage);
        }

        string? repositoryRoot = null;
        string? reportPath = null;
        string? externalRoot = null;
        string? baselinePath = null;
        var force = false;
        var resume = false;
        for (var index = 2; index < arguments.Count; index++)
        {
            var option = arguments[index];
            switch (option)
            {
                case "--force" when !force:
                    force = true;
                    continue;
                case "--resume" when !resume:
                    resume = true;
                    continue;
                case "--force" or "--resume":
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
            }

            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                return CommandParseResult.Failure(
                    "FLOWCLI_USAGE",
                    "ErrorOptionRequiresValue",
                    option,
                    CorpusUsage);
            }

            var value = arguments[index];
            switch (option)
            {
                case "--repository-root" when repositoryRoot is null:
                    repositoryRoot = value;
                    break;
                case "--report" when reportPath is null:
                    reportPath = value;
                    break;
                case "--external-root" when externalRoot is null:
                    externalRoot = value;
                    break;
                case "--baseline" when baselinePath is null:
                    baselinePath = value;
                    break;
                case "--repository-root" or "--report" or "--external-root" or "--baseline":
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
                default:
                    return CommandParseResult.Failure(
                        "FLOWCLI_UNKNOWN_OPTION",
                        "ErrorUnknownOption",
                        "corpus",
                        option,
                        CorpusUsage);
            }
        }

        return repositoryRoot is null || reportPath is null
            ? CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", CorpusUsage)
            : CommandParseResult.Success(
                new CorpusCommand(arguments[1], repositoryRoot, reportPath, externalRoot, baselinePath, force, resume));
    }

    private static CommandParseResult ParseEpubInventoryQualify(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", EpubInventoryQualifyUsage);
        }

        string? reportPath = null;
        string? repositoryRoot = null;
        var legalUse = false;
        var drmFree = false;
        var force = false;
        var resume = false;
        for (var index = 2; index < arguments.Count; index++)
        {
            var option = arguments[index];
            switch (option)
            {
                case "--legal-use" when !legalUse:
                    legalUse = true;
                    continue;
                case "--drm-free" when !drmFree:
                    drmFree = true;
                    continue;
                case "--force" when !force:
                    force = true;
                    continue;
                case "--resume" when !resume:
                    resume = true;
                    continue;
                case "--legal-use" or "--drm-free" or "--force" or "--resume":
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
            }

            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                return CommandParseResult.Failure(
                    "FLOWCLI_USAGE",
                    "ErrorOptionRequiresValue",
                    option,
                    EpubInventoryQualifyUsage);
            }

            var value = arguments[index];
            switch (option)
            {
                case "--report" when reportPath is null:
                    reportPath = value;
                    break;
                case "--repository-root" when repositoryRoot is null:
                    repositoryRoot = value;
                    break;
                case "--report" or "--repository-root":
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
                default:
                    return CommandParseResult.Failure(
                        "FLOWCLI_UNKNOWN_OPTION",
                        "ErrorUnknownOption",
                        "epub-inventory-qualify",
                        option,
                        EpubInventoryQualifyUsage);
            }
        }

        if (!legalUse || !drmFree)
        {
            return CommandParseResult.Failure("FLOWCLI_DECLARATION_REQUIRED", "ErrorRequiredEpubDeclarations");
        }

        return reportPath is null || repositoryRoot is null
            ? CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", EpubInventoryQualifyUsage)
            : CommandParseResult.Success(
                new QualifyEpubInventoryCommand(arguments[1], reportPath, repositoryRoot, force, resume));
    }

    private static CommandParseResult ParseEpubInventoryMatrix(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", EpubInventoryMatrixUsage);
        }

        string? expectedHash = null;
        string? outputPath = null;
        string? repositoryRoot = null;
        var force = false;
        var resume = false;
        for (var index = 2; index < arguments.Count; index++)
        {
            var option = arguments[index];
            switch (option)
            {
                case "--force" when !force:
                    force = true;
                    continue;
                case "--resume" when !resume:
                    resume = true;
                    continue;
                case "--force" or "--resume":
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
            }

            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                return CommandParseResult.Failure(
                    "FLOWCLI_USAGE",
                    "ErrorOptionRequiresValue",
                    option,
                    EpubInventoryMatrixUsage);
            }

            var value = arguments[index];
            switch (option)
            {
                case "--qualification-sha256" when expectedHash is null:
                    expectedHash = value;
                    break;
                case "--output" when outputPath is null:
                    outputPath = value;
                    break;
                case "--repository-root" when repositoryRoot is null:
                    repositoryRoot = value;
                    break;
                case "--qualification-sha256" or "--output" or "--repository-root":
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
                default:
                    return CommandParseResult.Failure(
                        "FLOWCLI_UNKNOWN_OPTION",
                        "ErrorUnknownOption",
                        "epub-inventory-matrix",
                        option,
                        EpubInventoryMatrixUsage);
            }
        }

        if (expectedHash is null || outputPath is null || repositoryRoot is null)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", EpubInventoryMatrixUsage);
        }

        return EpubCorpusSha256.TryParse(expectedHash, out var sha256)
            ? CommandParseResult.Success(new ClassifyEpubInventoryCommand(
                arguments[1], sha256, outputPath, repositoryRoot, force, resume))
            : CommandParseResult.Failure("FLOWCLI_INVALID_VALUE", "ErrorInvalidQualificationSha256");
    }

    private static CommandParseResult ParseEpubInventoryReview(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", EpubInventoryReviewUsage);
        }

        string? qualificationPath = null;
        string? expectedHash = null;
        string? outputDirectory = null;
        string? repositoryRoot = null;
        var uiLanguage = HtmlBookUiLanguage.Automatic;
        var hasUiLanguage = false;
        var legalUse = false;
        var drmFree = false;
        var force = false;
        var resume = false;
        for (var index = 2; index < arguments.Count; index++)
        {
            var option = arguments[index];
            switch (option)
            {
                case "--legal-use" when !legalUse:
                    legalUse = true;
                    continue;
                case "--drm-free" when !drmFree:
                    drmFree = true;
                    continue;
                case "--force" when !force:
                    force = true;
                    continue;
                case "--resume" when !resume:
                    resume = true;
                    continue;
                case "--legal-use" or "--drm-free" or "--force" or "--resume":
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
            }

            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                return CommandParseResult.Failure(
                    "FLOWCLI_USAGE",
                    "ErrorOptionRequiresValue",
                    option,
                    EpubInventoryReviewUsage);
            }

            var value = arguments[index];
            switch (option)
            {
                case "--qualification" when qualificationPath is null:
                    qualificationPath = value;
                    break;
                case "--qualification-sha256" when expectedHash is null:
                    expectedHash = value;
                    break;
                case "--output" when outputDirectory is null:
                    outputDirectory = value;
                    break;
                case "--repository-root" when repositoryRoot is null:
                    repositoryRoot = value;
                    break;
                case "--ui-language" when !hasUiLanguage:
                    if (!TryParseUiLanguage(value, out uiLanguage))
                    {
                        return CommandParseResult.Failure("FLOWCLI_INVALID_VALUE", "ErrorInvalidUiLanguage");
                    }

                    hasUiLanguage = true;
                    break;
                case "--qualification" or "--qualification-sha256" or "--output" or "--repository-root"
                    or "--ui-language":
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
                default:
                    return CommandParseResult.Failure(
                        "FLOWCLI_UNKNOWN_OPTION",
                        "ErrorUnknownOption",
                        "epub-inventory-review",
                        option,
                        EpubInventoryReviewUsage);
            }
        }

        if (!legalUse || !drmFree)
        {
            return CommandParseResult.Failure("FLOWCLI_DECLARATION_REQUIRED", "ErrorRequiredEpubDeclarations");
        }

        if (qualificationPath is null || expectedHash is null || outputDirectory is null || repositoryRoot is null)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", EpubInventoryReviewUsage);
        }

        return EpubCorpusSha256.TryParse(expectedHash, out var sha256)
            ? CommandParseResult.Success(new ReviewEpubInventoryCommand(
                arguments[1], qualificationPath, sha256, outputDirectory, repositoryRoot, uiLanguage, force, resume))
            : CommandParseResult.Failure("FLOWCLI_INVALID_VALUE", "ErrorInvalidQualificationSha256");
    }

    private static CommandParseResult ParseEpubQualify(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", EpubQualifyUsage);
        }

        string? candidateId = null;
        string? sha256 = null;
        string? reportPath = null;
        string? repositoryRoot = null;
        var repetitions = 2;
        var hasRepetitions = false;
        var legalUse = false;
        var drmFree = false;
        var includeEnvironment = false;
        var force = false;
        var resume = false;
        for (var index = 2; index < arguments.Count; index++)
        {
            var option = arguments[index];
            switch (option)
            {
                case "--legal-use" when !legalUse:
                    legalUse = true;
                    continue;
                case "--drm-free" when !drmFree:
                    drmFree = true;
                    continue;
                case "--include-environment" when !includeEnvironment:
                    includeEnvironment = true;
                    continue;
                case "--force" when !force:
                    force = true;
                    continue;
                case "--resume" when !resume:
                    resume = true;
                    continue;
                case "--legal-use" or "--drm-free" or "--include-environment" or "--force" or "--resume":
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
            }

            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                return CommandParseResult.Failure(
                    "FLOWCLI_USAGE",
                    "ErrorOptionRequiresValue",
                    option,
                    EpubQualifyUsage);
            }

            var value = arguments[index];
            switch (option)
            {
                case "--candidate-id" when candidateId is null:
                    candidateId = value;
                    break;
                case "--sha256" when sha256 is null:
                    sha256 = value;
                    break;
                case "--report" when reportPath is null:
                    reportPath = value;
                    break;
                case "--repository-root" when repositoryRoot is null:
                    repositoryRoot = value;
                    break;
                case "--repetitions" when !hasRepetitions:
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out repetitions)
                        || repetitions < 2)
                    {
                        return CommandParseResult.Failure("FLOWCLI_INVALID_VALUE", "ErrorInvalidRepetitions");
                    }

                    hasRepetitions = true;
                    break;
                case "--candidate-id" or "--sha256" or "--report" or "--repository-root" or "--repetitions":
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
                default:
                    return CommandParseResult.Failure(
                        "FLOWCLI_UNKNOWN_OPTION",
                        "ErrorUnknownOption",
                        "epub-qualify",
                        option,
                        EpubQualifyUsage);
            }
        }

        if (!legalUse || !drmFree)
        {
            return CommandParseResult.Failure("FLOWCLI_DECLARATION_REQUIRED", "ErrorRequiredEpubDeclarations");
        }

        if (candidateId is null || sha256 is null || reportPath is null || repositoryRoot is null)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", EpubQualifyUsage);
        }

        if (!EpubCorpusPublicationId.TryParse(candidateId, out var parsedId))
        {
            return CommandParseResult.Failure("FLOWCLI_INVALID_VALUE", "ErrorInvalidCandidateId", candidateId);
        }

        if (!EpubCorpusSha256.TryParse(sha256, out var parsedHash))
        {
            return CommandParseResult.Failure("FLOWCLI_INVALID_VALUE", "ErrorInvalidSha256");
        }

        return CommandParseResult.Success(
            new QualifyEpubCommand(
                arguments[1],
                parsedId,
                parsedHash,
                reportPath,
                repositoryRoot,
                repetitions,
                includeEnvironment,
                force,
                resume));
    }

    private static CommandParseResult ParseEpubReview(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", EpubReviewUsage);
        }

        string? candidateId = null;
        string? sha256 = null;
        string? outputDirectory = null;
        string? repositoryRoot = null;
        var uiLanguage = HtmlBookUiLanguage.Automatic;
        var hasUiLanguage = false;
        var legalUse = false;
        var drmFree = false;
        var force = false;
        var resume = false;
        for (var index = 2; index < arguments.Count; index++)
        {
            var option = arguments[index];
            switch (option)
            {
                case "--legal-use" when !legalUse:
                    legalUse = true;
                    continue;
                case "--drm-free" when !drmFree:
                    drmFree = true;
                    continue;
                case "--force" when !force:
                    force = true;
                    continue;
                case "--resume" when !resume:
                    resume = true;
                    continue;
                case "--legal-use" or "--drm-free" or "--force" or "--resume":
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
            }

            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                return CommandParseResult.Failure(
                    "FLOWCLI_USAGE",
                    "ErrorOptionRequiresValue",
                    option,
                    EpubReviewUsage);
            }

            var value = arguments[index];
            switch (option)
            {
                case "--candidate-id" when candidateId is null:
                    candidateId = value;
                    break;
                case "--sha256" when sha256 is null:
                    sha256 = value;
                    break;
                case "--output" when outputDirectory is null:
                    outputDirectory = value;
                    break;
                case "--repository-root" when repositoryRoot is null:
                    repositoryRoot = value;
                    break;
                case "--ui-language" when !hasUiLanguage:
                    if (!TryParseUiLanguage(value, out uiLanguage))
                    {
                        return CommandParseResult.Failure("FLOWCLI_INVALID_VALUE", "ErrorInvalidUiLanguage");
                    }

                    hasUiLanguage = true;
                    break;
                case "--candidate-id" or "--sha256" or "--output" or "--repository-root" or "--ui-language":
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
                default:
                    return CommandParseResult.Failure(
                        "FLOWCLI_UNKNOWN_OPTION",
                        "ErrorUnknownOption",
                        "epub-review",
                        option,
                        EpubReviewUsage);
            }
        }

        if (!legalUse || !drmFree)
        {
            return CommandParseResult.Failure("FLOWCLI_DECLARATION_REQUIRED", "ErrorRequiredEpubDeclarations");
        }

        if (candidateId is null || sha256 is null || outputDirectory is null || repositoryRoot is null)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", EpubReviewUsage);
        }

        if (!EpubCorpusPublicationId.TryParse(candidateId, out var parsedId))
        {
            return CommandParseResult.Failure("FLOWCLI_INVALID_VALUE", "ErrorInvalidCandidateId", candidateId);
        }

        if (!EpubCorpusSha256.TryParse(sha256, out var parsedHash))
        {
            return CommandParseResult.Failure("FLOWCLI_INVALID_VALUE", "ErrorInvalidSha256");
        }

        return CommandParseResult.Success(
            new ReviewEpubCommand(
                arguments[1],
                parsedId,
                parsedHash,
                outputDirectory,
                repositoryRoot,
                uiLanguage,
                force,
                resume));
    }

    private static CommandParseResult ParseSample(IReadOnlyList<string> arguments) =>
        arguments.Count switch
        {
            1 => CommandParseResult.Success(new SampleCommand("sample.flow.json")),
            2 => CommandParseResult.Success(new SampleCommand(arguments[1])),
            _ => CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", "flow sample [output]"),
        };

    private static CommandParseResult ParseExecutionStatus(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", ExecutionStatusUsage);
        }

        string? jsonPath = null;
        var force = false;
        for (var index = 2; index < arguments.Count; index++)
        {
            var option = arguments[index];
            if (option == "--force")
            {
                if (force)
                {
                    return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
                }

                force = true;
                continue;
            }

            if (option != "--json")
            {
                return CommandParseResult.Failure(
                    "FLOWCLI_UNKNOWN_OPTION",
                    "ErrorUnknownOption",
                    "execution-status",
                    option,
                    ExecutionStatusUsage);
            }

            if (jsonPath is not null)
            {
                return CommandParseResult.Failure("FLOWCLI_DUPLICATE_OPTION", "ErrorDuplicateOption", option);
            }

            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]))
            {
                return CommandParseResult.Failure(
                    "FLOWCLI_USAGE",
                    "ErrorOptionRequiresValue",
                    option,
                    ExecutionStatusUsage);
            }

            jsonPath = arguments[index];
        }

        return CommandParseResult.Success(new ExecutionStatusCommand(arguments[1], jsonPath, force));
    }

    private static CommandParseResult ParseExecutionClean(IReadOnlyList<string> arguments)
    {
        if (arguments.Count != 4 || arguments[2] != "--execution-id")
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", ExecutionCleanUsage);
        }

        if (!Guid.TryParseExact(arguments[3], "N", out var executionId))
        {
            return CommandParseResult.Failure("FLOWCLI_INVALID_VALUE", "ErrorInvalidExecutionId");
        }

        return CommandParseResult.Success(new ExecutionCleanCommand(arguments[1], executionId));
    }

    private static CommandParseResult ParseDocumentCommand(
        IReadOnlyList<string> arguments,
        Func<string, CliCommand> create) =>
        arguments.Count == 2
            ? CommandParseResult.Success(create(arguments[1]))
            : CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", $"flow {arguments[0]} <document>");

    private static CommandParseResult ParseRender(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 2)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", RenderUsage);
        }

        if (arguments.Skip(2).Contains("--html-book", StringComparer.Ordinal))
        {
            return ParseHtmlBook(arguments);
        }

        string? outputPath = null;
        double? width = null;
        double? height = null;

        for (var index = 2; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count)
            {
                return CommandParseResult.Failure(
                    "FLOWCLI_USAGE",
                    "ErrorOptionRequiresValue",
                    arguments[index],
                    RenderUsage);
            }

            var option = arguments[index];
            var value = arguments[index + 1];
            switch (option)
            {
                case "--html" when outputPath is null:
                    outputPath = value;
                    break;
                case "--width" when width is null:
                    if (!TryParseDimension(value, out var parsedWidth))
                    {
                        return CommandParseResult.Failure(
                            "FLOWCLI_INVALID_VALUE",
                            "ErrorInvalidWidth");
                    }

                    width = parsedWidth;
                    break;
                case "--height" when height is null:
                    if (!TryParseDimension(value, out var parsedHeight))
                    {
                        return CommandParseResult.Failure(
                            "FLOWCLI_INVALID_VALUE",
                            "ErrorInvalidHeight");
                    }

                    height = parsedHeight;
                    break;
                case "--html" or "--width" or "--height":
                    return CommandParseResult.Failure(
                        "FLOWCLI_DUPLICATE_OPTION",
                        "ErrorDuplicateOption",
                        option);
                default:
                    return CommandParseResult.Failure(
                        "FLOWCLI_UNKNOWN_OPTION",
                        "ErrorUnknownOption",
                        "render",
                        option,
                        RenderUsage);
            }
        }

        if (string.IsNullOrWhiteSpace(outputPath) || width is null || height is null)
        {
            return CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", RenderUsage);
        }

        return CommandParseResult.Success(
            new RenderHtmlCommand(arguments[1], outputPath, width.Value, height.Value));
    }

    private static CommandParseResult ParseHtmlBook(IReadOnlyList<string> arguments)
    {
        string? outputDirectory = null;
        var uiLanguage = HtmlBookUiLanguage.Automatic;
        var hasUiLanguage = false;
        for (var index = 2; index < arguments.Count; index += 2)
        {
            if (index + 1 >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index + 1]))
            {
                return CommandParseResult.Failure(
                    "FLOWCLI_USAGE",
                    "ErrorOptionRequiresValue",
                    arguments[index],
                    RenderUsage);
            }

            var option = arguments[index];
            var value = arguments[index + 1];
            switch (option)
            {
                case "--html-book" when outputDirectory is null:
                    outputDirectory = value;
                    break;
                case "--ui-language" when !hasUiLanguage:
                    if (!TryParseUiLanguage(value, out uiLanguage))
                    {
                        return CommandParseResult.Failure(
                            "FLOWCLI_INVALID_VALUE",
                            "ErrorInvalidUiLanguage");
                    }

                    hasUiLanguage = true;
                    break;
                case "--html-book" or "--ui-language":
                    return CommandParseResult.Failure(
                        "FLOWCLI_DUPLICATE_OPTION",
                        "ErrorDuplicateOption",
                        option);
                default:
                    return CommandParseResult.Failure(
                        "FLOWCLI_UNKNOWN_OPTION",
                        "ErrorUnknownOption",
                        "HTML book",
                        option,
                        RenderUsage);
            }
        }

        return outputDirectory is null
            ? CommandParseResult.Failure("FLOWCLI_USAGE", "ErrorUsage", RenderUsage)
            : CommandParseResult.Success(new RenderHtmlBookCommand(arguments[1], outputDirectory, uiLanguage));
    }

    private static bool TryParseUiLanguage(string value, out HtmlBookUiLanguage language)
    {
        language = value.ToLowerInvariant() switch
        {
            "auto" => HtmlBookUiLanguage.Automatic,
            "en" => HtmlBookUiLanguage.English,
            "pt-pt" => HtmlBookUiLanguage.PortuguesePortugal,
            "pt-br" => HtmlBookUiLanguage.PortugueseBrazil,
            _ => (HtmlBookUiLanguage)(-1),
        };
        return Enum.IsDefined(language);
    }

    private static bool TryParseDimension(string value, out double dimension) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out dimension)
        && double.IsFinite(dimension)
        && dimension > 0;

    private const string RenderUsage =
        "flow render <document> (--html <output> --width <n> --height <n> | --html-book <output-directory> [--ui-language <auto|en|pt-PT|pt-BR>])";

    private const string ImportUsage =
        "flow import <book.epub> [--output <book.flow.json>] [--diagnostics-json <report.json>] [--fidelity-report <fidelity.json>] [--metadata-json <metadata.json>] [--processing-json <processing.json>] [--source-map-json <source-map.json>]";

    private const string EpubInspectUsage = "flow epub-inspect <book.epub> [--json <report.json>]";

    private const string EpubInventoryUsage =
        "flow epub-inventory <directory> --output <catalog.json> --repository-root <absolute-directory> [--force]";

    private const string EpubInventoryQualifyUsage =
        "flow epub-inventory-qualify <directory> --report <report.json> --repository-root <absolute-directory> --legal-use --drm-free [--force] [--resume]";

    private const string EpubInventoryMatrixUsage =
        "flow epub-inventory-matrix <qualification.json> --qualification-sha256 <hash> --output <matrix.json> --repository-root <absolute-directory> [--force] [--resume]";

    private const string EpubInventoryReviewUsage =
        "flow epub-inventory-review <directory> --qualification <qualification.json> --qualification-sha256 <hash> --output <absolute-directory> --repository-root <absolute-directory> --legal-use --drm-free [--ui-language <auto|en|pt-PT|pt-BR>] [--force] [--resume]";

    private const string CorpusUsage =
        "flow corpus <manifest.json> --repository-root <directory> --report <report.json> [--external-root <directory>] [--baseline <baseline.json>] [--force] [--resume]";

    private const string EpubQualifyUsage =
        "flow epub-qualify <book.epub> --candidate-id <id> --sha256 <hash> --report <report.json> --repository-root <absolute-directory> --legal-use --drm-free [--repetitions <n>] [--include-environment] [--force] [--resume]";

    private const string EpubReviewUsage =
        "flow epub-review <book.epub> --candidate-id <id> --sha256 <hash> --output <absolute-directory> --repository-root <absolute-directory> --legal-use --drm-free [--ui-language <auto|en|pt-PT|pt-BR>] [--force] [--resume]";

    private const string ExecutionStatusUsage =
        "flow execution-status <destination> [--json <report.json>] [--force]";

    private const string ExecutionCleanUsage =
        "flow execution-clean <destination> --execution-id <32-hex-id>";
}
