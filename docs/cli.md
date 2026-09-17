# Flow CLI 0.2

The `flow` executable is a deliberately small composition layer over the document serializer, validator, canonicalizer, layout engine, and HTML renderer. Command parsing is implemented independently from command operations and uses no external CLI framework.

The help output identifies the CLI and `.flow.json` representation as experimental. Stable `FLOWCLI_*` prefixes distinguish command, option, value, and operation failures. Exit code `0` means the requested operation completed, `1` means command/input/I/O failure, `2` means semantic validation or automatic qualification failed, and `130` means cancellation.

## Global output options

Global options appear before the command:

```text
flow [--language <en-US|pt-BR>] [--banner] [--no-color] <command>
```

`--language` selects the resource catalog for human-readable terminal output. The initial catalogs are `en-US` and `pt-BR`. Omitting the option selects `en-US`; an unsupported value fails with `FLOWCLI_INVALID_VALUE` and an English fallback message. Commands, option names, paths, serialized fields and diagnostic codes do not change with the language.

Every stable EPUB, document-validation and Flow JSON diagnostic code currently has a `pt-BR` summary. The CLI prints the original technical detail immediately afterward, so paths, IDs, rejected values and parser messages are not lost. A code introduced without a catalog entry falls back to its original message. This localization affects only terminal text: diagnostic and inspection JSON remains deterministic and independent of `--language`.

`--banner` prints an optional FIGlet-style ASCII heading. The CLI emits plain text by default and does not require ANSI colors; `--no-color` makes that contract explicit for scripts, redirected output and limited terminals. These presentation options do not affect generated files, canonical bytes or hashes.

## Commands

```text
flow sample [output]
flow import <book.epub> [--output <book.flow.json>] [--diagnostics-json <report.json>] [--fidelity-report <fidelity.json>]
flow epub-inspect <book.epub> [--json <report.json>]
flow corpus <manifest.json> --repository-root <directory> --report <report.json> [--external-root <directory>] [--baseline <baseline.json>] [--force] [--resume]
flow epub-qualify <book.epub> --candidate-id <id> --sha256 <hash> --report <report.json> --repository-root <absolute-directory> --legal-use --drm-free [--repetitions <n>] [--include-environment] [--force] [--resume]
flow epub-review <book.epub> --candidate-id <id> --sha256 <hash> --output <absolute-directory> --repository-root <absolute-directory> --legal-use --drm-free [--ui-language <auto|en|pt-PT|pt-BR>] [--force] [--resume]
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

Warnings and errors use stable `EPUB*`/`FLOWCLI_*` diagnostics. `--diagnostics-json` writes the deterministic `flow-epub-import-diagnostics-0.1` report, including success, document summary, severity totals, and every EPUB diagnostic. It contains no timestamps, shell metadata, stack traces, or PowerShell capture artifacts. Both readable JSON formats use UTF-8 without BOM and LF line endings; ordinary Unicode such as Portuguese accents, Japanese, and Arabic remains literal, while JSON controls and security-sensitive characters remain escaped. The command validates before writing, uses atomic replacement, reports the imported identity and hash, and never leaves a partial Flow document after a failed import. A requested diagnostics report is still written for a failed import when the destination itself is usable.

`--fidelity-report` writes the separate deterministic `flow-epub-fidelity-0.1` report. It compares typed source/destination counts and classifies outcomes as preserved, transformed, approximated, unsupported, or lost, with localized findings when source evidence permits. It is compatible with `--diagnostics-json`, is written atomically even for a failed/partial import, and never enters `.flow.json` or the canonical hash. Percentages with no source denominator are `null`; the report is an informative conversion audit, not an EPUB conformance or visual-equivalence claim. See [EPUB fidelity report](epub-fidelity.md).

The imported `.flow.json` contains only canonical Flow metadata. Publisher, contributors, extra languages, subjects, dates, rights, cover declarations, accessibility properties, and OPF refinements are available to API callers through the noncanonical `EpubImportResult.MetadataReport`; the CLI does not persist that source report yet.

`epub-inspect` reads only the EPUB container and OPF structure. It reports the EPUB 2/3 family, principal metadata, manifest properties, fallback and media-overlay IDs, linear/non-linear and repeated spine references, navigation documents, archive sizes, resource types, missing resources, unsupported resources, and diagnostics without producing a `FlowDocument`. `--json` writes the deterministic `flow-epub-inspection-0.1` report even when the publication is invalid enough to return exit code `1`. Spine entries are always emitted by declared position; manifest sorting in JSON is never treated as reading order.

`corpus` reads a `flow-epub-corpus-0.1` manifest, discovers only the permitted local inputs, runs the complete corpus pipeline twice and writes `flow-epub-corpus-qualification-0.1`. `--baseline` compares the observed evidence with an existing reviewed baseline; the command never creates or accepts a replacement baseline. The report excludes physical publication paths. Exit code `2` indicates a failed, skipped or inconclusive publication, non-deterministic repeated evidence, or baseline mismatch.

`epub-qualify` runs the automatic large-publication gate at least twice. The neutral candidate ID, expected SHA-256, absolute repository root and the `--legal-use`/`--drm-free` declarations are mandatory. The source EPUB and report are rejected inside the repository tree. A technically successful run normally reports the overall status `inconclusive`, because human review remains separate; the command still returns `0` when every automatic check passed or passed with warnings. `--include-environment` adds approximate duration, managed-heap and working-set observations to a clearly non-deterministic report section. It is off by default.

`epub-review` creates the transactional review directory with mobile and desktop packages, `review.html`, `review-checklist.json` and `review-manifest.json`. Its output and repository root must be explicit absolute paths, and the output must remain outside the repository. The command verifies the same candidate ID and SHA-256 used by the gate. It does not approve checklist items or infer legal permission.

### Output replacement and interrupted runs

`corpus`, `epub-qualify`, and `epub-review` do not replace an existing final output by default. `--force` permits replacement, but the final file or directory remains untouched until the new result is complete and ready for its atomic commit. Existing review directories must still contain a recognized Flow review manifest; `--force` never authorizes deletion of an arbitrary directory.

An interrupted atomic write may leave a hidden staging, backup, or temporary artifact beside its destination. The next invocation stops and asks for `--resume`. This option removes only artifacts whose exact destination-specific name contains a valid transaction identifier, restores a recognized review backup when necessary, and restarts the operation from the source. Partial JSON and HTML are never reused as completed evidence. Use `--force --resume` together when an interrupted attempt was replacing a completed output. Ambiguous or unrecognized backups are left untouched and reported as errors.

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
dotnet run --project src/Flow.Cli -- epub-inspect book.epub --json inspection.json
dotnet run --project src/Flow.Cli -- corpus epub-corpus.json --repository-root C:\src\FlowEngineNet --report C:\flow-local\corpus.json --force
dotnet run --project src/Flow.Cli -- epub-qualify C:\books\book.epub --candidate-id candidate-001 --sha256 $sha256 --report C:\flow-local\gate.json --repository-root C:\src\FlowEngineNet --legal-use --drm-free
dotnet run --project src/Flow.Cli -- epub-review C:\books\book.epub --candidate-id candidate-001 --sha256 $sha256 --output C:\flow-local\review --repository-root C:\src\FlowEngineNet --legal-use --drm-free --ui-language pt-BR
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
