using System.Collections.Immutable;

namespace Flow.Cli;

internal enum CliCommandGroup
{
    Start,
    EpubBooks,
    FlowDocuments,
    CorpusQuality,
    Maintenance,
}

internal enum CliRequirement
{
    Optional,
    Required,
    Conditional,
}

internal sealed record CliArgumentDescriptor(
    string Syntax,
    CliRequirement Requirement,
    string DescriptionResourceKey);

internal sealed record CliOptionDescriptor(
    string Name,
    string? ValueSyntax,
    CliRequirement Requirement,
    string DescriptionResourceKey)
{
    public string Syntax => ValueSyntax is null ? Name : $"{Name} {ValueSyntax}";
}

internal sealed record CliExitCodeDescriptor(int Code, string DescriptionResourceKey);

internal sealed record CliCommandDescriptor(
    CliCommandGroup Group,
    string Name,
    string TitleResourceKey,
    string DescriptionResourceKey,
    string Usage,
    ImmutableArray<CliArgumentDescriptor> Arguments,
    ImmutableArray<CliOptionDescriptor> Options,
    ImmutableArray<string> Examples,
    string FileEffectsResourceKey,
    ImmutableArray<string> SecurityResourceKeys,
    ImmutableArray<CliExitCodeDescriptor> ExitCodes,
    bool AvailableInInteractiveMenu);

/// <summary>Describes the stable command surface independently from parsing and presentation.</summary>
internal static class CliCommandCatalog
{
    public static ImmutableArray<CliCommandDescriptor> All { get; } =
    [
        Command(
            CliCommandGroup.Start,
            "help",
            "Command_help_Title",
            "Command_help_Description",
            "flow help [command]",
            [Argument("command", CliRequirement.Optional, "CommandArgument_Command")],
            [],
            ["flow help", "flow help import"],
            "CommandEffect_None",
            [],
            ExitCodes(0, 1),
            availableInMenu: false),
        Command(
            CliCommandGroup.Start,
            "sample",
            "Command_sample_Title",
            "Command_sample_Description",
            "flow sample [output]",
            [Argument("output", CliRequirement.Optional, "CommandArgument_FlowOutput")],
            [],
            ["flow sample sample.flow.json"],
            "CommandEffect_Sample",
            [],
            ExitCodes(0, 1, 130)),
        Command(
            CliCommandGroup.EpubBooks,
            "import",
            "Command_import_Title",
            "Command_import_Description",
            "flow import <book.epub> [--output <book.flow.json>] [--diagnostics-json <report.json>] [--fidelity-report <fidelity.json>] [--metadata-json <metadata.json>] [--processing-json <processing.json>] [--source-map-json <source-map.json>]",
            [Argument("book.epub", CliRequirement.Required, "CommandArgument_Epub")],
            [
                Option("--output", "<book.flow.json>", CliRequirement.Optional, "CommandOption_OutputFlow"),
                Option("--diagnostics-json", "<report.json>", CliRequirement.Optional, "CommandOption_DiagnosticsJson"),
                Option("--fidelity-report", "<fidelity.json>", CliRequirement.Optional, "CommandOption_FidelityReport"),
                Option("--metadata-json", "<metadata.json>", CliRequirement.Optional, "CommandOption_MetadataJson"),
                Option("--processing-json", "<processing.json>", CliRequirement.Optional, "CommandOption_ProcessingJson"),
                Option("--source-map-json", "<source-map.json>", CliRequirement.Optional, "CommandOption_SourceMapJson"),
            ],
            ["flow import book.epub --output book.flow.json --diagnostics-json diagnostics.json --fidelity-report fidelity.json --metadata-json metadata.json --processing-json processing.json --source-map-json source-map.json"],
            "CommandEffect_Import",
            ["CommandSecurity_Epub"],
            ExitCodes(0, 1, 2, 130)),
        Command(
            CliCommandGroup.EpubBooks,
            "epub-inspect",
            "Command_epub_inspect_Title",
            "Command_epub_inspect_Description",
            "flow epub-inspect <book.epub> [--json <report.json>]",
            [Argument("book.epub", CliRequirement.Required, "CommandArgument_Epub")],
            [Option("--json", "<report.json>", CliRequirement.Optional, "CommandOption_JsonReport")],
            ["flow epub-inspect book.epub --json inspection.json"],
            "CommandEffect_EpubInspect",
            ["CommandSecurity_Epub"],
            ExitCodes(0, 1, 2, 130)),
        Command(
            CliCommandGroup.CorpusQuality,
            "epub-inventory",
            "Command_epub_inventory_Title",
            "Command_epub_inventory_Description",
            "flow epub-inventory <directory> --output <catalog.json> --repository-root <absolute-directory> [--force]",
            [Argument("directory", CliRequirement.Required, "CommandArgument_EpubDirectory")],
            [
                Option("--output", "<catalog.json>", CliRequirement.Required, "CommandOption_OutputCatalog"),
                Option("--repository-root", "<absolute-directory>", CliRequirement.Required, "CommandOption_RepositoryRoot"),
                Option("--force", null, CliRequirement.Optional, "CommandOption_Force"),
            ],
            ["flow epub-inventory books --output inventory.json --repository-root repository --force"],
            "CommandEffect_Inventory",
            ["CommandSecurity_PrivateCorpus", "CommandSecurity_Force"],
            ExitCodes(0, 1, 130)),
        Command(
            CliCommandGroup.CorpusQuality,
            "epub-inventory-qualify",
            "Command_epub_inventory_qualify_Title",
            "Command_epub_inventory_qualify_Description",
            "flow epub-inventory-qualify <directory> --report <report.json> --repository-root <absolute-directory> --legal-use --drm-free [--force] [--resume]",
            [Argument("directory", CliRequirement.Required, "CommandArgument_EpubDirectory")],
            [
                Option("--report", "<report.json>", CliRequirement.Required, "CommandOption_Report"),
                Option("--repository-root", "<absolute-directory>", CliRequirement.Required, "CommandOption_RepositoryRoot"),
                Option("--legal-use", null, CliRequirement.Required, "CommandOption_LegalUse"),
                Option("--drm-free", null, CliRequirement.Required, "CommandOption_DrmFree"),
                Option("--force", null, CliRequirement.Optional, "CommandOption_Force"),
                Option("--resume", null, CliRequirement.Optional, "CommandOption_Resume"),
            ],
            ["flow epub-inventory-qualify books --report qualification.json --repository-root repository --legal-use --drm-free --force --resume"],
            "CommandEffect_Qualification",
            ["CommandSecurity_Declarations", "CommandSecurity_PrivateCorpus", "CommandSecurity_ForceResume"],
            ExitCodes(0, 1, 2, 130)),
        Command(
            CliCommandGroup.CorpusQuality,
            "epub-inventory-matrix",
            "Command_epub_inventory_matrix_Title",
            "Command_epub_inventory_matrix_Description",
            "flow epub-inventory-matrix <qualification.json> --qualification-sha256 <hash> --output <matrix.json> --repository-root <absolute-directory> [--force] [--resume]",
            [Argument("qualification.json", CliRequirement.Required, "CommandArgument_Qualification")],
            [
                Option("--qualification-sha256", "<hash>", CliRequirement.Required, "CommandOption_QualificationSha256"),
                Option("--output", "<matrix.json>", CliRequirement.Required, "CommandOption_OutputMatrix"),
                Option("--repository-root", "<absolute-directory>", CliRequirement.Required, "CommandOption_RepositoryRoot"),
                Option("--force", null, CliRequirement.Optional, "CommandOption_Force"),
                Option("--resume", null, CliRequirement.Optional, "CommandOption_Resume"),
            ],
            ["flow epub-inventory-matrix qualification.json --qualification-sha256 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa --output matrix.json --repository-root repository --force --resume"],
            "CommandEffect_Matrix",
            ["CommandSecurity_HashBinding", "CommandSecurity_ForceResume"],
            ExitCodes(0, 1, 2, 130)),
        Command(
            CliCommandGroup.CorpusQuality,
            "epub-inventory-review",
            "Command_epub_inventory_review_Title",
            "Command_epub_inventory_review_Description",
            "flow epub-inventory-review <directory> --qualification <qualification.json> --qualification-sha256 <hash> --output <absolute-directory> --repository-root <absolute-directory> --legal-use --drm-free [--ui-language <auto|en|pt-PT|pt-BR>] [--force] [--resume]",
            [Argument("directory", CliRequirement.Required, "CommandArgument_EpubDirectory")],
            [
                Option("--qualification", "<qualification.json>", CliRequirement.Required, "CommandOption_Qualification"),
                Option("--qualification-sha256", "<hash>", CliRequirement.Required, "CommandOption_QualificationSha256"),
                Option("--output", "<absolute-directory>", CliRequirement.Required, "CommandOption_OutputDirectory"),
                Option("--repository-root", "<absolute-directory>", CliRequirement.Required, "CommandOption_RepositoryRoot"),
                Option("--legal-use", null, CliRequirement.Required, "CommandOption_LegalUse"),
                Option("--drm-free", null, CliRequirement.Required, "CommandOption_DrmFree"),
                Option("--ui-language", "<auto|en|pt-PT|pt-BR>", CliRequirement.Optional, "CommandOption_UiLanguage"),
                Option("--force", null, CliRequirement.Optional, "CommandOption_Force"),
                Option("--resume", null, CliRequirement.Optional, "CommandOption_Resume"),
            ],
            ["flow epub-inventory-review books --qualification qualification.json --qualification-sha256 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa --output review --repository-root repository --legal-use --drm-free --ui-language pt-BR --force --resume"],
            "CommandEffect_InventoryReview",
            ["CommandSecurity_Declarations", "CommandSecurity_PrivateCorpus", "CommandSecurity_ForceResume"],
            ExitCodes(0, 1, 2, 130)),
        Command(
            CliCommandGroup.CorpusQuality,
            "corpus",
            "Command_corpus_Title",
            "Command_corpus_Description",
            "flow corpus <manifest.json> --repository-root <directory> --report <report.json> [--external-root <directory>] [--baseline <baseline.json>] [--force] [--resume]",
            [Argument("manifest.json", CliRequirement.Required, "CommandArgument_CorpusManifest")],
            [
                Option("--repository-root", "<directory>", CliRequirement.Required, "CommandOption_RepositoryRoot"),
                Option("--report", "<report.json>", CliRequirement.Required, "CommandOption_Report"),
                Option("--external-root", "<directory>", CliRequirement.Optional, "CommandOption_ExternalRoot"),
                Option("--baseline", "<baseline.json>", CliRequirement.Optional, "CommandOption_Baseline"),
                Option("--force", null, CliRequirement.Optional, "CommandOption_Force"),
                Option("--resume", null, CliRequirement.Optional, "CommandOption_Resume"),
            ],
            ["flow corpus corpus.json --repository-root repository --report corpus-report.json --external-root public-books --baseline baseline.json --force --resume"],
            "CommandEffect_Corpus",
            ["CommandSecurity_CorpusManifest", "CommandSecurity_ForceResume"],
            ExitCodes(0, 1, 2, 130)),
        Command(
            CliCommandGroup.CorpusQuality,
            "epub-qualify",
            "Command_epub_qualify_Title",
            "Command_epub_qualify_Description",
            "flow epub-qualify <book.epub> --candidate-id <id> --sha256 <hash> --report <report.json> --repository-root <absolute-directory> --legal-use --drm-free [--repetitions <n>] [--include-environment] [--force] [--resume]",
            [Argument("book.epub", CliRequirement.Required, "CommandArgument_Epub")],
            [
                Option("--candidate-id", "<id>", CliRequirement.Required, "CommandOption_CandidateId"),
                Option("--sha256", "<hash>", CliRequirement.Required, "CommandOption_Sha256"),
                Option("--report", "<report.json>", CliRequirement.Required, "CommandOption_Report"),
                Option("--repository-root", "<absolute-directory>", CliRequirement.Required, "CommandOption_RepositoryRoot"),
                Option("--legal-use", null, CliRequirement.Required, "CommandOption_LegalUse"),
                Option("--drm-free", null, CliRequirement.Required, "CommandOption_DrmFree"),
                Option("--repetitions", "<n>", CliRequirement.Optional, "CommandOption_Repetitions"),
                Option("--include-environment", null, CliRequirement.Optional, "CommandOption_IncludeEnvironment"),
                Option("--force", null, CliRequirement.Optional, "CommandOption_Force"),
                Option("--resume", null, CliRequirement.Optional, "CommandOption_Resume"),
            ],
            ["flow epub-qualify book.epub --candidate-id candidate-001 --sha256 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa --report gate.json --repository-root repository --legal-use --drm-free --repetitions 2 --include-environment --force --resume"],
            "CommandEffect_EpubQualify",
            ["CommandSecurity_Declarations", "CommandSecurity_HashBinding", "CommandSecurity_ForceResume"],
            ExitCodes(0, 1, 2, 130)),
        Command(
            CliCommandGroup.CorpusQuality,
            "epub-review",
            "Command_epub_review_Title",
            "Command_epub_review_Description",
            "flow epub-review <book.epub> --candidate-id <id> --sha256 <hash> --output <absolute-directory> --repository-root <absolute-directory> --legal-use --drm-free [--ui-language <auto|en|pt-PT|pt-BR>] [--force] [--resume]",
            [Argument("book.epub", CliRequirement.Required, "CommandArgument_Epub")],
            [
                Option("--candidate-id", "<id>", CliRequirement.Required, "CommandOption_CandidateId"),
                Option("--sha256", "<hash>", CliRequirement.Required, "CommandOption_Sha256"),
                Option("--output", "<absolute-directory>", CliRequirement.Required, "CommandOption_OutputDirectory"),
                Option("--repository-root", "<absolute-directory>", CliRequirement.Required, "CommandOption_RepositoryRoot"),
                Option("--legal-use", null, CliRequirement.Required, "CommandOption_LegalUse"),
                Option("--drm-free", null, CliRequirement.Required, "CommandOption_DrmFree"),
                Option("--ui-language", "<auto|en|pt-PT|pt-BR>", CliRequirement.Optional, "CommandOption_UiLanguage"),
                Option("--force", null, CliRequirement.Optional, "CommandOption_Force"),
                Option("--resume", null, CliRequirement.Optional, "CommandOption_Resume"),
            ],
            ["flow epub-review book.epub --candidate-id candidate-001 --sha256 aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa --output review --repository-root repository --legal-use --drm-free --ui-language en --force --resume"],
            "CommandEffect_EpubReview",
            ["CommandSecurity_Declarations", "CommandSecurity_HashBinding", "CommandSecurity_ForceResume"],
            ExitCodes(0, 1, 130)),
        Command(
            CliCommandGroup.Maintenance,
            "execution-status",
            "Command_execution_status_Title",
            "Command_execution_status_Description",
            "flow execution-status <destination> [--json <report.json>] [--force]",
            [Argument("destination", CliRequirement.Required, "CommandArgument_Destination")],
            [
                Option("--json", "<report.json>", CliRequirement.Optional, "CommandOption_JsonReport"),
                Option("--force", null, CliRequirement.Optional, "CommandOption_Force"),
            ],
            ["flow execution-status output --json status.json --force"],
            "CommandEffect_ExecutionStatus",
            ["CommandSecurity_ExecutionStatus"],
            ExitCodes(0, 1, 2, 130)),
        Command(
            CliCommandGroup.Maintenance,
            "execution-clean",
            "Command_execution_clean_Title",
            "Command_execution_clean_Description",
            "flow execution-clean <destination> --execution-id <32-hex-id>",
            [Argument("destination", CliRequirement.Required, "CommandArgument_Destination")],
            [Option("--execution-id", "<32-hex-id>", CliRequirement.Required, "CommandOption_ExecutionId")],
            ["flow execution-clean output --execution-id 0123456789abcdef0123456789abcdef"],
            "CommandEffect_ExecutionClean",
            ["CommandSecurity_ExecutionClean"],
            ExitCodes(0, 1)),
        Command(
            CliCommandGroup.FlowDocuments,
            "inspect",
            "Command_inspect_Title",
            "Command_inspect_Description",
            "flow inspect <document>",
            [Argument("document", CliRequirement.Required, "CommandArgument_FlowDocument")],
            [],
            ["flow inspect book.flow.json"],
            "CommandEffect_ReadOnly",
            [],
            ExitCodes(0, 1, 130)),
        Command(
            CliCommandGroup.FlowDocuments,
            "validate",
            "Command_validate_Title",
            "Command_validate_Description",
            "flow validate <document>",
            [Argument("document", CliRequirement.Required, "CommandArgument_FlowDocument")],
            [],
            ["flow validate book.flow.json"],
            "CommandEffect_ReadOnly",
            [],
            ExitCodes(0, 1, 2, 130)),
        Command(
            CliCommandGroup.FlowDocuments,
            "hash",
            "Command_hash_Title",
            "Command_hash_Description",
            "flow hash <document>",
            [Argument("document", CliRequirement.Required, "CommandArgument_FlowDocument")],
            [],
            ["flow hash book.flow.json"],
            "CommandEffect_ReadOnly",
            [],
            ExitCodes(0, 1, 130)),
        Command(
            CliCommandGroup.FlowDocuments,
            "render",
            "Command_render_Title",
            "Command_render_Description",
            "flow render <document> (--html <output> --width <n> --height <n> | --html-book <output-directory> [--ui-language <auto|en|pt-PT|pt-BR>])",
            [Argument("document", CliRequirement.Required, "CommandArgument_FlowDocument")],
            [
                Option("--html", "<output>", CliRequirement.Conditional, "CommandOption_Html"),
                Option("--width", "<n>", CliRequirement.Conditional, "CommandOption_Width"),
                Option("--height", "<n>", CliRequirement.Conditional, "CommandOption_Height"),
                Option("--html-book", "<output-directory>", CliRequirement.Conditional, "CommandOption_HtmlBook"),
                Option("--ui-language", "<auto|en|pt-PT|pt-BR>", CliRequirement.Optional, "CommandOption_UiLanguage"),
            ],
            [
                "flow render book.flow.json --html book.html --width 390 --height 844",
                "flow render book.flow.json --html-book book-html --ui-language pt-BR",
            ],
            "CommandEffect_Render",
            ["CommandSecurity_Render"],
            ExitCodes(0, 1, 130)),
    ];

    private static readonly ImmutableDictionary<string, CliCommandDescriptor> ByName =
        All.ToImmutableDictionary(static command => command.Name, StringComparer.Ordinal);

    public static bool TryGet(string name, out CliCommandDescriptor descriptor) =>
        ByName.TryGetValue(name, out descriptor!);

    private static CliCommandDescriptor Command(
        CliCommandGroup group,
        string name,
        string titleResourceKey,
        string descriptionResourceKey,
        string usage,
        ImmutableArray<CliArgumentDescriptor> arguments,
        ImmutableArray<CliOptionDescriptor> options,
        ImmutableArray<string> examples,
        string fileEffectsResourceKey,
        ImmutableArray<string> securityResourceKeys,
        ImmutableArray<CliExitCodeDescriptor> exitCodes,
        bool availableInMenu = true) =>
        new(
            group,
            name,
            titleResourceKey,
            descriptionResourceKey,
            usage,
            arguments,
            options,
            examples,
            fileEffectsResourceKey,
            securityResourceKeys,
            exitCodes,
            availableInMenu);

    private static CliArgumentDescriptor Argument(
        string syntax,
        CliRequirement requirement,
        string descriptionResourceKey) =>
        new(syntax, requirement, descriptionResourceKey);

    private static CliOptionDescriptor Option(
        string name,
        string? valueSyntax,
        CliRequirement requirement,
        string descriptionResourceKey) =>
        new(name, valueSyntax, requirement, descriptionResourceKey);

    private static ImmutableArray<CliExitCodeDescriptor> ExitCodes(params int[] codes) =>
        [.. codes.Select(static code => new CliExitCodeDescriptor(code, $"CommandExitCode_{code}"))];
}
