# Flow CLI 0.2

English | [Português (Brasil)](pt-BR/cli.md)

The `flow` executable is a deliberately small composition layer over the document serializer, validator, canonicalizer, layout engine, and HTML renderer. Command parsing is implemented independently from command operations and uses no external CLI framework.

The help output identifies the CLI and `.flow.json` representation as experimental. Stable `FLOWCLI_*` prefixes distinguish command, option, value, and operation failures. Exit code `0` means the requested operation completed, `1` means command/input/I/O failure, `2` means semantic validation or automatic qualification failed, and `130` means cancellation.

## Local tool package

`Flow.Cli` is packable as the framework-dependent .NET tool `FlowEngineNet.Tool`; the installed command is `flow`. The package is not published. To exercise the same distribution path used by CI without changing the global tool list, run from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\smoke-test-cli.ps1 -Configuration Release
```

Use `pwsh` on PowerShell 7 or Linux. The script obtains the package identity from MSBuild, packs the current source, creates a NuGet configuration containing only that local package source, and installs it with `--tool-path`. It then verifies English and Brazilian Portuguese help, `sample`, `inspect`, `validate`, `hash`, and standalone HTML rendering through the installed launcher. Cleanup removes the isolated tool installation even when a command fails.

The deterministic summary is written to `artifacts/cli-smoke/smoke-result.json`; build products remain under the ignored `artifacts/` directory. The script requires the .NET 10 SDK selected by `global.json`. It does not publish, sign or install a machine-wide tool.

For a complete local release candidate, run `eng/build-release-artifacts.ps1`. It performs two independent package builds, normalizes unsigned NuGet ZIP metadata, requires byte-for-byte agreement, generates CycloneDX 1.5 and SHA-256 evidence, validates the package structure and installs the final package from a local-only source. The resulting files stay under `artifacts/release/` and are not uploaded or published. See [local release artifacts](release-artifacts.md).

`eng/invoke-release-dry-run.ps1` validates the checked-in version/tag plan and adds unsigned in-toto/SLSA provenance without creating a tag or release. CI builds one canonical candidate on Ubuntu, retains it for one day, and passes that exact `.nupkg` to isolated Ubuntu and Windows validators. `eng/verify-release-artifacts.ps1` checks its hashes and runs the installed CLI; the final comparison rejects a different revision, SDK, identity or package hash. Nothing is sent to a package feed.

## Global output options

Global options appear before the command:

```text
flow [--language <en-US|pt-BR>] [--banner] [--no-color] <command>
```

`--language` selects the resource catalog for human-readable terminal output. The initial catalogs are `en-US` and `pt-BR`. Omitting the option selects `en-US`; an unsupported value fails with `FLOWCLI_INVALID_VALUE` and an English fallback message. Commands, option names, paths, serialized fields and diagnostic codes do not change with the language.

Every stable EPUB, document-validation and Flow JSON diagnostic code currently has a `pt-BR` summary. The CLI prints the original technical detail immediately afterward, so paths, IDs, rejected values and parser messages are not lost. A code introduced without a catalog entry falls back to its original message. This localization affects only terminal text: diagnostic and inspection JSON remains deterministic and independent of `--language`.

`--banner` prints an optional FIGlet-style ASCII heading. On an interactive terminal with ANSI support, the CLI can use a richer layout that adapts to the available width. Redirected output, captured output and terminals without ANSI support receive deterministic plain text instead. `--no-color` removes color without discarding the spacing and hierarchy of the interactive layout. These presentation options do not affect generated files, canonical bytes or hashes.

## Interactive command preview

`flow menu` opens with a short introduction to the project, its EPUB-to-Flow workflow and the experimental status of the current format. The blue welcome panel appears once per session, before the keyboard-driven command catalog. Use the arrow keys and Enter to choose a category or command. Each command preview shows its purpose, direct syntax, required inputs, file effects and safety notes. `Back` returns to the previous level, `Exit` closes the menu, and Esc cancels it. Ctrl+C keeps the established cancellation behavior and exit code `130`.

The menu is read-only at this stage: choosing a command displays information and never runs the operation. Redirected input or output, captured output and unsupported terminals are refused with `FLOWCLI_MENU_REQUIRES_INTERACTIVE`; use `flow help` and the equivalent direct command in automation. `--no-color` and `NO_COLOR` keep the interactive layout while removing color.

## Command-specific help

`flow help` presents the command catalog in five stable groups: Getting started, EPUB books, Flow documents, Corpus and quality, and Maintenance. The general view shows only each command name and a short summary, so long signatures remain readable even in an 80-column terminal. Narrow interactive terminals stack summaries below command names; wider terminals place them side by side. The plain variant carries the same information without ANSI sequences, borders or cursor control.

Use either form below for the complete typed details of one command:

```text
flow help <command>
flow <command> --help
```

The detailed view shows purpose, usage, positional arguments, required and optional options, safe examples, file effects, safety notes, and relevant exit codes. It is available for every public command in en-US and pt-BR. Help is resolved before required command arguments, so asking for it never starts the described operation or creates output. An unknown help target returns `FLOWCLI_UNKNOWN_HELP_COMMAND` and exit code `1`.

## Commands

```text
flow menu
flow sample [output]
flow import <book.epub> [--output <book.flow.json>] [--diagnostics-json <report.json>] [--fidelity-report <fidelity.json>]
flow epub-inspect <book.epub> [--json <report.json>]
flow epub-inventory <directory> --output <catalog.json> --repository-root <absolute-directory> [--force]
flow epub-inventory-qualify <directory> --report <report.json> --repository-root <absolute-directory> --legal-use --drm-free [--force] [--resume]
flow epub-inventory-matrix <qualification.json> --qualification-sha256 <hash> --output <matrix.json> --repository-root <absolute-directory> [--force] [--resume]
flow epub-inventory-review <directory> --qualification <qualification.json> --qualification-sha256 <hash> --output <absolute-directory> --repository-root <absolute-directory> --legal-use --drm-free [--ui-language <auto|en|pt-PT|pt-BR>] [--force] [--resume]
flow corpus <manifest.json> --repository-root <directory> --report <report.json> [--external-root <directory>] [--baseline <baseline.json>] [--force] [--resume]
flow epub-qualify <book.epub> --candidate-id <id> --sha256 <hash> --report <report.json> --repository-root <absolute-directory> --legal-use --drm-free [--repetitions <n>] [--include-environment] [--force] [--resume]
flow epub-review <book.epub> --candidate-id <id> --sha256 <hash> --output <absolute-directory> --repository-root <absolute-directory> --legal-use --drm-free [--ui-language <auto|en|pt-PT|pt-BR>] [--force] [--resume]
flow execution-status <destination> [--json <report.json>] [--force]
flow execution-clean <destination> --execution-id <32-hex-id>
flow inspect <document>
flow validate <document>
flow hash <document>
flow render <document> --html <output> --width <n> --height <n>
flow render <document> --html-book <output-directory> [--ui-language <auto|en|pt-PT|pt-BR>]
```

`sample` writes “The Flow Experiment” to the optional output path or to `sample.flow.json` in the current directory. The command serializes the same typed sample factory used to generate the committed reference document.

`import` accepts a security-bounded EPUB and writes a deterministic `.flow.json`. Without `--output`, the destination stays beside the source EPUB and receives a portable `snake_case` name derived from the imported title. Accents are folded (`é` becomes `e`), punctuation and repeated whitespace become one underscore, the stem is limited to 96 characters, and an unusable title falls back to the source name or `imported_book`. For example, “Isto é filtro solar: Eclesiastes e a vida debaixo do sol” becomes `isto_e_filtro_solar_eclesiastes_e_a_vida_debaixo_do_sol.flow.json`. An explicit `--output` always wins.

Successful imports print noncanonical timing, archive, asset, spine, node, character, approximate managed-memory, and `.flow.json` size measurements. Timings and sampled memory vary by machine and never enter the document or hash. Pressing `Ctrl+C` produces `FLOWCLI_CANCELLED` and exit code `130`; an existing final output is preserved and temporary output is removed. See [EPUB performance](epub-performance.md).

For example:

```powershell
$epub = 'C:\caminho\para\Meu livro.epub'
dotnet run --project .\src\Flow.Cli -- import $epub
```

may create `C:\caminho\para\meu_livro.flow.json`. The command prints the exact path after a successful atomic write.

Warnings and errors use stable `EPUB*`/`FLOWCLI_*` diagnostics. `--diagnostics-json` writes the deterministic `flow-epub-import-diagnostics-0.1` report, including success, document summary, severity totals, and every EPUB diagnostic. It contains no timestamps, shell metadata, stack traces, or PowerShell capture artifacts. Both readable JSON formats use UTF-8 without BOM and LF line endings; ordinary Unicode such as Portuguese accents, Japanese, and Arabic remains literal, while JSON controls and security-sensitive characters remain escaped. The command validates before writing, uses atomic replacement, reports the imported identity and hash, and never leaves a partial Flow document after a failed import. A requested diagnostics report is still written for a failed import when the destination itself is usable. `EPUB077` means an image reference differed from exactly one manifest path only by letter casing; ambiguous matches are never selected. `EPUB078` identifies a safe local image referenced only through CSS: the reference is accounted for, but the image is not promoted to a semantic Flow asset. `EPUB079` records an authored image alternative recovered from `aria-label`, `aria-labelledby`, `title`, or `figcaption`; `EPUB041` remains reserved for an image with no explicit alternative and calls for human review. `EPUB080` identifies valid XHTML column metadata that is not represented by the current Flow table model; it is distinct from malformed table recovery (`EPUB058`).

`--fidelity-report` writes the separate deterministic `flow-epub-fidelity-0.1` report. It compares typed source/destination counts and classifies outcomes as preserved, transformed, approximated, unsupported, or lost, with localized findings when source evidence permits. It is compatible with `--diagnostics-json`, is written atomically even for a failed/partial import, and never enters `.flow.json` or the canonical hash. Percentages with no source denominator are `null`; the report is an informative conversion audit, not an EPUB conformance or visual-equivalence claim. See [EPUB fidelity report](epub-fidelity.md).

The imported `.flow.json` contains only canonical Flow metadata. Publisher, contributors, extra languages, subjects, dates, rights, cover declarations, accessibility properties, and OPF refinements are available to API callers through the noncanonical `EpubImportResult.MetadataReport`; the CLI does not persist that source report yet.

`epub-inspect` reads only the EPUB container and OPF structure. It reports the EPUB 2/3 family, principal metadata, manifest properties, fallback and media-overlay IDs, linear/non-linear and repeated spine references, navigation documents, archive sizes, resource types, missing resources, unsupported resources, and diagnostics without producing a `FlowDocument`. Current font media types and the observed legacy TrueType alias `application/x-font-truetype` are recognized structurally and receive `EPUB074` because their bytes are not retained. The legacy EPUB 2 `application/oebps-page-map+xml` type is also recognized and receives `EPUB076`; inspection does not open that XML or claim to validate its labels and destinations. `--json` writes the deterministic `flow-epub-inspection-0.1` report even when the publication is invalid enough to return exit code `1`. Spine entries are always emitted by declared position; manifest sorting in JSON is never treated as reading order.

`epub-inventory` recursively reads an explicitly supplied private directory under the existing bounded archive and XML limits. It does not convert or modify source files. Identical payloads are collapsed by SHA-256 with a retained copy count. The catalog records neutral IDs, hashes, EPUB family, declared languages, byte/resource/spine counts, protection evidence, status and stable diagnostic codes. It does not store source paths, file names, titles, authors, publisher identifiers or content.

The source and catalog must remain outside the repository, and the catalog cannot be written inside the source directory. `--force` replaces only a JSON file whose format is already `flow-epub-private-inventory-0.1`.

`epub-inventory-qualify` repeats inventory locally, selects only ready or review-required candidates and associates each source by its verified SHA-256. It reuses the corpus executor to run inspection, import, validation, fidelity analysis, Flow JSON round-trip, canonical integrity, mobile and desktop layouts and HTML-book verification twice. Unknown encryption, corrupt archives and structurally unsuitable inputs are recorded as skipped instead of being opened by the semantic pipeline.

The command requires `--legal-use` and `--drm-free`. Its path-free `flow-epub-private-qualification-0.1` report contains only neutral candidate IDs, hashes, counts, completed phases and aggregated diagnostic codes. It excludes file names, physical paths, titles, authors, publisher identifiers, source text and asset bytes. A candidate with measured fidelity loss is failed even when the remaining phases complete. Exit code `0` requires every discovered candidate to be eligible, approved, lossless under the measured profile and deterministic; failed, inconclusive, nondeterministic or skipped entries produce exit code `2` while preserving the complete report.

`epub-inventory-matrix` verifies the exact SHA-256 of a private qualification report and classifies its existing evidence without reopening any publication. The path-free matrix distinguishes approval, approximation, unsupported content, measured loss, broken source references, Flow errors, and cases requiring human review. Automatic classification never completes the human-review field.

`epub-inventory-review` verifies the same report hash, rediscovers the source files by SHA-256, and creates one neutral assisted-review package per qualified candidate. A failure in one candidate does not prevent other packages. The corpus index and checklists omit editorial identity and begin with every human decision pending.

`corpus` reads a `flow-epub-corpus-0.1` manifest, discovers only the permitted local inputs, runs the complete corpus pipeline twice and writes `flow-epub-corpus-qualification-0.1`. `--baseline` compares the observed evidence with an existing reviewed baseline; the command never creates or accepts a replacement baseline. The report excludes physical publication paths. Exit code `2` indicates a failed, skipped or inconclusive publication, non-deterministic repeated evidence, or baseline mismatch.

`epub-qualify` runs the automatic large-publication gate at least twice. The neutral candidate ID, expected SHA-256, absolute repository root and the `--legal-use`/`--drm-free` declarations are mandatory. The source EPUB and report are rejected inside the repository tree. A technically successful run normally reports the overall status `inconclusive`, because human review remains separate; the command still returns `0` when every automatic check passed or passed with warnings. `--include-environment` adds approximate duration, managed-heap and working-set observations to a clearly non-deterministic report section. It is off by default.

`epub-review` creates the transactional review directory with mobile and desktop packages, `review.html`, `review-checklist.json` and `review-manifest.json`. Its output and repository root must be explicit absolute paths, and the output must remain outside the repository. The command verifies the same candidate ID and SHA-256 used by the gate. It does not approve checklist items or infer legal permission.

### Output replacement and interrupted runs

`corpus`, `epub-inventory-qualify`, `epub-inventory-matrix`, `epub-inventory-review`, `epub-qualify`, and `epub-review` do not replace an existing final output by default. `--force` permits replacement, but the final file or directory remains untouched until the new result is complete and ready for its atomic commit. Existing review directories must still contain a recognized Flow review manifest; `--force` never authorizes deletion of an arbitrary directory.

Each command holds a hidden `.<destination>.flow-execution.lock` sidecar for its complete execution. The handle allows read-only status inspection but rejects another writer. The sidecar persists after release and records a random 128-bit execution ID, state, format and SHA-256 fingerprint of the normalized destination. It never contains the destination path, book content or source metadata, and it is excluded from Git, reports, canonicalization and hashes. The execution ID is printed for local log correlation but is intentionally nondeterministic.

An interrupted atomic write leaves its lock in `interrupted` state, or `active` if the process ended before it could update the state, and may also leave a hidden staging, backup or temporary artifact. The next invocation stops and asks for `--resume`. This option takes over only a recognized lock for the exact destination, assigns a new execution ID, removes recognized transaction artifacts and restarts from the source. Partial JSON and HTML are never reused as completed evidence. Use `--force --resume` together when an interrupted attempt was replacing a completed output. Invalid locks and ambiguous or unrecognized backups are preserved for manual inspection.

`execution-status` reads this local state without acquiring the destination for writing. It reports a missing, active, completed, interrupted or invalid lock, the recorded UUID and counts of recognized temporary, staging and backup artifacts. `--json` writes the path-free `flow-cli-execution-status-0.1` report in UTF-8 without BOM and LF. An existing status report requires `--force`; its path cannot be the inspected destination or lock sidecar.

`execution-clean` is an explicit maintenance operation. It requires the exact 32-character execution ID reported by `execution-status`, refuses a lock held by an active writer, and then holds the lock exclusively while cleaning. Only exact destination-bound transaction names are considered. A single recognized review backup is restored when the destination is missing; ambiguous backups, reparse points, copied locks, invalid manifests and unrelated files are preserved. The sidecar itself remains, marked `completed`, so later inspection retains the local execution identity.

`inspect` reports identity, metadata, total nodes, chapters, sections, paragraphs, figures, footnotes, assets, addressable anchors, and presentation availability.

`validate` returns exit code `0` for a structurally valid document and `2` when validation diagnostics contain errors. Parsing, file, and command errors return `1`.

`hash` reports the algorithm, uppercase hexadecimal document hash, and canonicalization profile.

`render` resolves `ReadingMode.Flow` for the requested logical viewport, writes standalone HTML, and reports the source identity, canonical hash, anchor count, and selected viewport category. Width and height use invariant-culture positive numbers.

`render --html-book` writes the additional multi-file `flow-html-book-0.1` package. It uses a deterministic 1024×768 logical layout, keeps the TOC in `toc.html`, creates one file per semantic chapter, externalizes hash-deduplicated assets and typed CSS, and records canonical identity plus payload hashes in `manifest.json`. The directory is assembled beside the target and then moved into place. An arbitrary existing directory, filesystem root, reparse point, existing file, or directory containing the source document is never replaced. The CLI reports generated file/byte totals and render/write durations. See [HTML book package](html-book-package.md).

`--ui-language` controls only renderer-generated interface text. Values are `auto`, `en`, `pt-PT`, and `pt-BR`; matching is case-insensitive. `auto` uses `pt-BR` for Brazilian Portuguese publications, `pt-PT` for other Portuguese language tags, and English for all other or missing publication languages. Authored book content and TOC titles are never translated. The resolved UI language is written to `manifest.json` and reported by the CLI.

The CLI imports the currently supported EPUB subset and reads `.flow.json`; it does not yet expose signing, verification, reader-preference switches, PDF, or paged output. Successful `sample`, `import`, and `render` operations replace their target files.

During development, invoke the executable through the project:

```powershell
dotnet run --project src/Flow.Cli -- sample sample.flow.json
dotnet run --project src/Flow.Cli -- --language pt-BR --banner help
dotnet run --project src/Flow.Cli -- --language pt-BR --banner epub-inventory C:\books --output C:\flow-local\epub-inventory.json --repository-root C:\src\FlowEngineNet
dotnet run --project src/Flow.Cli -- --language pt-BR --banner epub-inventory-qualify C:\books --report C:\flow-local\epub-qualification.json --repository-root C:\src\FlowEngineNet --legal-use --drm-free
dotnet run --project src/Flow.Cli -- epub-inspect book.epub --json inspection.json
dotnet run --project src/Flow.Cli -- corpus epub-corpus.json --repository-root C:\src\FlowEngineNet --report C:\flow-local\corpus.json --force
dotnet run --project src/Flow.Cli -- epub-qualify C:\books\book.epub --candidate-id candidate-001 --sha256 $sha256 --report C:\flow-local\gate.json --repository-root C:\src\FlowEngineNet --legal-use --drm-free
dotnet run --project src/Flow.Cli -- epub-review C:\books\book.epub --candidate-id candidate-001 --sha256 $sha256 --output C:\flow-local\review --repository-root C:\src\FlowEngineNet --legal-use --drm-free --ui-language pt-BR
dotnet run --project src/Flow.Cli -- execution-status C:\flow-local\gate.json --json C:\flow-local\gate-status.json
dotnet run --project src/Flow.Cli -- execution-clean C:\flow-local\gate.json --execution-id 0123456789abcdef0123456789abcdef
dotnet run --project src/Flow.Cli -- --language pt-BR import book.epub --diagnostics-json import-report.json
dotnet run --project src/Flow.Cli -- validate sample.flow.json
dotnet run --project src/Flow.Cli -- render sample.flow.json --html sample.html --width 390 --height 844
dotnet run --project src/Flow.Cli -- render sample.flow.json --html-book sample-book --ui-language pt-PT
```

To keep the generated HTML book beside an imported EPUB, pass that directory explicitly:

```powershell
$flow = 'C:\caminho\para\meu_livro.flow.json'
$book = 'C:\caminho\para\meu_livro_book'
dotnet run --project .\src\Flow.Cli -- render $flow --html-book $book --ui-language pt-PT
```

The current CLI deliberately keeps `import` and `render` as separate operations. Importing does not create the HTML book automatically; a future Reader is expected to hide this pipeline when opening supported source formats directly.

Generate separate package directories when more than one interface language is required. A package intentionally has one `index.html` and one coherent UI language rather than parallel `index_en.html`, `index_pt.html`, and `index_ptbr.html` entry points sharing ambiguous navigation state.

## Separation and testing

`CliCommandParser` converts argument tokens into typed command records without performing I/O. `CliOperations` performs EPUB inspection/import, serialization, validation, hashing, layout, and rendering. `FlowCliApplication` owns exit-code and error handling. This separation allows integration tests to execute the real operations with isolated temporary files and captured text streams without starting a subprocess.
