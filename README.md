# Flow Engine .NET

> **Experimental:** Flow 0.x is a research project, not a standardized file format. Do not use it yet for archival, legal, or production-critical documents.

Flow Engine .NET explores a document model in which canonical identity and semantic content remain independent from viewport, typography, layout, pagination, and renderer technology.

The central invariant is:

```text
Document != Layout != Rendering
```

The project is not a replacement for PDF or EPUB. Version 0.1 tests engine foundations; it does not provide a complete reader, editor, or publishing workflow.

## Current milestone

The current development milestone is **`0.2.0-alpha.1`**, focused on real EPUB workflows.

Implemented:

- immutable semantic document and inline/block node model;
- typed stable identifiers, document anchors, node indexing, and structural validation;
- typed, optional presentation and typography intentions;
- reader-preference cascade with renderer safety constraints;
- deterministic experimental `.flow.json` serialization;
- versioned canonicalization (`flow-c14n-0.2`, with a legacy 0.1 compatibility writer) and SHA-256 document hashes;
- experimental local RSA-PSS-SHA256 signatures over canonical bytes;
- a diagnostic-first, security-bounded EPUB import prototype;
- non-converting EPUB 2/3 package inspection with deterministic JSON reports;
- EPUB 3 Navigation Document import with EPUB 2 NCX fallback and stable Flow anchors;
- expanded OPF metadata import with a typed, noncanonical source report;
- deterministic EPUB resource/fragment traceability to stable Flow node IDs;
- byte-validated EPUB covers and JPEG/PNG/static-GIF/WebP/SVG assets, with deterministic SVG sanitization and hash deduplication;
- XHTML/SVG cover wrappers resolved to safe raster `Figure` nodes with a typed, noncanonical cover intent;
- a safe EPUB CSS subset translated into typed, noncanonical role/node presentation;
- exact spine-order processing with XHTML fallback chains and typed inclusion decisions;
- order-preserving XHTML mixed-content conversion with aggregated semantic-loss diagnostics;
- immutable semantic tables with captions, row groups, spans, header associations, EPUB import, and accessible HTML output;
- restricted structural MathML preservation for block and inline expressions, with safe HTML output and explicit semantic-loss diagnostics;
- canonical inline language ranges, typed bidirectional embedding/isolation/override, and Japanese ruby annotations imported from XHTML;
- renderer-independent adaptive layout for `ReadingMode.Flow`;
- deterministic standalone HTML5 rendering with embedded CSS and assets;
- deterministic, script-free HTML book packages with hierarchical TOC, chapter-local notes, editorial progress, explicit `pt-PT`/`pt-BR`/`en` UI, book-like responsive themes and navigation, deduplicated assets, shared CSS, and integrity manifest;
- the five-chapter “The Flow Experiment” reference book;
- a lightweight CLI for sampling, inspection, validation, hashing, and HTML rendering;
- readable UTF-8 JSON output, deterministic EPUB diagnostic reports, and title-derived portable `.flow.json` names;
- a typed, deterministic EPUB fidelity sidecar with explicit preserved/transformed/approximated/unsupported/lost evidence;
- deterministic noncanonical EPUB metadata, package-processing, and source-map sidecars for editorial diagnostics;
- small, medium, and large viewport profiles;
- responsive figures, semantic ID preservation, and typed layout intentions;
- unit and semantic-conformance tests;
- typed EPUB progress and runtime metrics, cooperative cancellation, and atomic cancellation-safe CLI outputs.

Not implemented yet:

- renderers other than HTML;
- broad EPUB interoperability and EPUB export;
- paged and print layout modes;
- reader or editor applications;
- certificate trust, PKI, provenance, DRM, or cloud publishing services.

`ReadingMode.Paged` and `ReadingMode.Print` are reserved contracts and deliberately fail until their behavior is properly specified and tested.

## Pipeline

```text
Importer / .flow.json
        |
        v
  FlowDocument          canonical identity and semantic content
        |
        v
  LayoutDocument        runtime styles and adaptive layout intentions
        |
        v
     Renderer           future adapter: HTML, native UI, print, etc.
```

Presentation, reader preferences, viewport state, layout decisions, and renderer output do not participate in the canonical document hash.

## Flow alongside EPUB and PDF

Flow is a normalized semantic model, not a replacement for EPUB's ZIP container or a reason to force readers to convert every book manually. EPUB remains a good distribution format; Flow adds a stable representation that renderers, validators, annotations, accessibility tools, hashes, and signatures can share without depending on the source format.

The intended Reader experience is:

1. Open an EPUB directly on any supported platform.
2. Import and validate it transparently, preferably in memory.
3. Keep a compact cache only when that improves startup time or offline use.
4. Present the same reading and annotation model for EPUB, future PDF import, and native Flow documents.

The readable `.flow.json` format is currently an experimental interchange and debugging format. It is deliberately verbose and is not intended to be the final package installed on a constrained reading device. A compact Flow container may be investigated later, but only if it provides value beyond ZIP compression—for example, normalized semantics across source formats, stable IDs and annotations, integrity data, and renderer-independent assets. It must be cross-platform and must not make a Windows-only converter part of the normal reading workflow.

### Localization boundary

Flow keeps three kinds of text separate:

- authored content, such as a title written as “Table of contents” in the EPUB, is preserved and is never translated silently;
- generated Reader or renderer interface text, such as navigation labels and actions, can be localized according to an explicit UI language;
- stable diagnostic codes and serialized model names remain language-neutral, while their human-readable messages may be localized independently.

The HTML book package supports `auto`, `pt-PT`, `pt-BR`, and `en` for generated interface text. `auto` selects Brazilian Portuguese for `pt-BR`, European Portuguese for other `pt` language tags, and English as the deterministic fallback. Authored navigation titles remain unchanged even when the interface uses another language. Recording explicit source provenance for every potentially synthesized label remains future work.

## Adaptive Flow layout

`AdaptiveLayoutEngine` converts an immutable `FlowDocument` plus a `LayoutContext` into an immutable `LayoutDocument` without generating HTML, CSS, coordinates, or pages.

| Viewport | Logical width | Default columns | Margin | Maximum content width |
| --- | ---: | ---: | ---: | ---: |
| Small | `< 600 px` | 1 | 16 px | 100% |
| Medium | `600–1199 px` | 1 | 32 px | 800 px |
| Large | `>= 1200 px` | 1 | 64 px | 1120 px |

Two columns are available only on a large viewport and only when the host explicitly sets `AllowTwoColumns`. Reader margin preferences and compatible renderer safety limits are resolved into the final layout profile.

## Minimal example

```csharp
using Flow.Core;
using Flow.Documents;
using Flow.Layout;

var document = new FlowDocument(
    new DocumentIdentity(new DocumentId("urn:flow:example:hello"), version: "1"),
    new DocumentMetadata("Hello Flow", language: "en"),
    new DocumentContent(
    [
        new Heading(new NodeId("welcome"), 1, [new Text("Welcome")]),
        new Paragraph(
            new NodeId("introduction"),
            [new Text("This content remains independent from its layout.")]),
    ]));

var context = new LayoutContext(
    viewportWidth: 390,
    viewportHeight: 844,
    deviceClass: DeviceClass.Phone,
    readingMode: ReadingMode.Flow);

LayoutDocument layout = new AdaptiveLayoutEngine().Layout(document, context);

Console.WriteLine(layout.Profile.ViewportCategory); // Small
Console.WriteLine(layout.Profile.ColumnCount);      // 1
Console.WriteLine(layout.Nodes[0].SemanticId);      // welcome
```

The layout operation validates the semantic document first. Invalid IDs, hierarchy, anchors, asset references, footnotes, or table-of-contents destinations prevent a layout from being produced. EPUB import recognizes cross-document footnotes/endnotes and multiple references, preserves formatted call labels, and reports broken, ambiguous, unreferenced, cyclic, or invalid-backlink relationships explicitly.

## Solution structure

| Project | Responsibility | Status |
| --- | --- | --- |
| `Flow.Core` | Stable IDs, anchors, and shared primitives | Implemented |
| `Flow.Documents` | Semantic model, validation, presentation, and `.flow.json` | Implemented for 0.1 |
| `Flow.Layout` | Style cascade and adaptive Flow layout | Implemented for 0.1 |
| `Flow.Security` | Canonicalization, hashing, and local RSA signature proof of concept | Implemented for 0.1 |
| `Flow.Rendering` | Renderer contracts and immutable rendered output | Implemented for 0.1 |
| `Flow.Rendering.Html` | Deterministic standalone HTML and multi-file HTML book adapter | Expanded in 0.2 alpha |
| `Flow.Epub` | Package inspection and diagnostic-first EPUB-to-`FlowDocument` adapter | Expanded subset in 0.2 alpha |
| `Flow.Cli` | EPUB inspection/import, sample, inspect, validate, hash, and HTML rendering commands | EPUB CLI integration in 0.2 alpha |

Dependencies point inward: the document domain does not reference layout or renderer projects. EPUB and HTML remain adapters at the edge. The approved direct dependency graph is enforced by `Flow.Conformance.Tests`.

## Requirements and build

The repository pins **.NET SDK 10.0.401** through `global.json`.

```powershell
dotnet restore Flow.sln
dotnet format Flow.sln --verify-no-changes
dotnet build Flow.sln
dotnet test Flow.sln --no-build
```

The build emits XML documentation files for public assemblies. The numbered [0.1 conformance profile](docs/conformance.md) and feature suites run on Windows and Linux in GitHub Actions.

The final clean-directory review passes 134 tests with zero build warnings; see the [0.1 release review](docs/0.1-release-review.md).

## Documentation

- [Architecture](docs/architecture.md)
- [Semantic document model](docs/flow-document-model.md)
- [Adaptive layout](docs/adaptive-layout.md)
- [Standalone HTML renderer](docs/html-renderer.md)
- [HTML book package](docs/html-book-package.md)
- [Command-line interface](docs/cli.md)
- [Local release artifacts](docs/release-artifacts.md)
- [Canonicalization and hashing](docs/canonicalization.md)
- [Experimental document signatures](docs/signatures.md)
- [Experimental EPUB import](docs/epub-import.md)
- [EPUB fidelity report](docs/epub-fidelity.md)
- [Experimental EPUB corpus catalog](docs/epub-corpus.md)
- [Public EPUB corpus coverage matrix](docs/epub-corpus-matrix.md)
- [0.1 conformance profile](docs/conformance.md)
- [Known limitations](docs/known-limitations.md)
- [Resolved and reduced limitations](docs/resolved-limitations.md)
- [0.1 release review](docs/0.1-release-review.md)
- [Roadmap](docs/roadmap.md)
- [Research findings](docs/research-findings.md)

## Roadmap direction

The real EPUB cycle now follows these increments:

1. import EPUB through the CLI into a valid, deterministic `.flow.json`;
2. expand accessibility metadata and media fallbacks (TOC navigation, notes, tables, ruby, inline languages, bidirectional semantics, MathML, and a safe typed CSS subset are now imported);
3. generate a self-contained HTML book with its own TOC and chapter files;
4. validate fidelity and performance against legal real-world publications;
5. pass the large-book gate before beginning the PDF importer.

The EPUB work now includes measured progress and cancellation, a deterministic legal-corpus catalog, bounded offline discovery, end-to-end execution, optional local EPUBCheck evidence, reviewed baselines for three project-owned fixtures, a typed structural/reference audit, and transactional material for assisted local review. The review package provides mobile and desktop HTML, beginning/middle/end samples, feature shortcuts, hashes and a separate human checklist without copying licensed material into Git. After the first local large-publication run exposed missing inline images and approximate reference evidence, the importer began preserving those images as ordered figures and the auditor began classifying links through their nearest mapped semantic ancestor. Two final automatic runs on the same verified input produced identical stable evidence, reconciled all 56 image occurrences, classified and resolved all 146 internal links, and resolved all 120 footnote backlinks. A focused renderer correction then contained covers, figures, long headings and tables within the reading surface, gave wide tables their own scroll region, and made note targets visible. Human review passed the beginning, middle, end, TOC, images, links, notes and tables at an exact 390 x 844 CSS-pixel viewport and at 1600 x 1000. This is evidence for one verified real publication, not a universal EPUB-support, performance, or accessibility-conformance claim. See [EPUB performance, progress, and cancellation](docs/epub-performance.md), the [experimental EPUB corpus](docs/epub-corpus.md), and its [public coverage matrix](docs/epub-corpus-matrix.md).

The qualified fixtures and the locally supplied real publication now complete that end-to-end path for the documented subset: import without measured silent loss, a valid `FlowDocument`, round-trip, layout, inspection, rendering and assisted human review. Broader EPUB coverage remains open, so this is not yet an “open any EPUB” guarantee. The [Flow 0.2 EPUB cycle review](docs/0.2-epub-cycle-review.md) records the evidence and the wording that is safe to use publicly.

## CLI quick start

The CLI keeps command names, option names, JSON fields and diagnostic codes invariant. Human-readable help, CLI errors, labels and summaries can be selected with the global `--language` option. The first catalogs are `en-US` and `pt-BR`; omitting the option selects `en-US`, while an unsupported value is rejected with an English fallback error. `--banner` adds an optional FIGlet-style ASCII heading, while normal output remains plain and contains no required ANSI color sequences:

```powershell
dotnet run --project src/Flow.Cli -- --language pt-BR --banner help
dotnet run --project src/Flow.Cli -- --language en-US --no-color help
```

Global options must precede the command. The banner can accompany any operation:

```powershell
dotnet run --project src/Flow.Cli -- --language pt-BR --banner epub-inspect C:\books\book.epub
```

### Test the packaged CLI

The repository can also build `Flow.Cli` as the local .NET tool `FlowEngineNet.Tool`, whose command is `flow`. Nothing is published to NuGet yet. The distribution smoke test creates the package, installs it in an isolated directory, runs representative commands through the installed launcher, and uninstalls it without changing the user's global tools:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\smoke-test-cli.ps1 -Configuration Release
```

On PowerShell 7, including Linux, use `pwsh` instead of `powershell.exe`. A successful run leaves its report and package under `artifacts/cli-smoke/`, which is ignored by Git. GitHub Actions runs the same smoke test on Windows and Ubuntu after restore, formatting, build and tests.

This check requires the .NET 10 SDK selected by `global.json`. It proves that the locally built framework-dependent package can be installed, started and used for the basic document workflow; it is not a signed release or an operating-system installer.

To assemble the complete local release candidate, including checksums, a CycloneDX SBOM and a validation manifest:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\build-release-artifacts.ps1 -Configuration Release
```

The command performs two independent packs and accepts the package only when the normalized bytes match. It writes the result under `artifacts/release/` and does not publish anything. See [local release artifacts](docs/release-artifacts.md) for the exact contents, validation rules and reproducibility boundary.

The checked-in `eng/release-plan.json` also supports a versioned dry-run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\invoke-release-dry-run.ps1
```

The plan explicitly disables publication. After the ordinary Windows/Linux validation matrix passes, CI builds one canonical candidate on Ubuntu and retains it for one day. Ubuntu and Windows then install and exercise that exact `.nupkg`; a final job requires both validation reports to name the same revision, SDK, package identity and SHA-256. The candidate is transferred only between workflow jobs and is never published to NuGet, attached to a GitHub Release or associated with a created tag.

Global options must appear before the command. They affect terminal text only: generated `.flow.json`, evidence JSON, canonical bytes, hashes and stable diagnostic codes do not change. In `pt-BR`, every current EPUB, document-validation and Flow JSON code receives a short Portuguese summary followed by its original technical detail. This keeps paths, IDs and rejected values available for troubleshooting without changing deterministic reports.

To see the current end-to-end result without generating anything, open the committed mobile and desktop files:

```powershell
Start-Process samples/SampleBook/mobile.html
Start-Process samples/SampleBook/desktop.html
```

To reproduce the pipeline from the semantic source:

```powershell
dotnet run --project src/Flow.Cli -- epub-inspect path/to/book.epub --json epub-report.json
dotnet run --project src/Flow.Cli -- import path/to/book.epub --diagnostics-json import-report.json --fidelity-report fidelity.json
dotnet run --project src/Flow.Cli -- corpus path/to/epub-corpus.json --repository-root path/to/repository --report path/to/corpus-report.json
dotnet run --project src/Flow.Cli -- inspect samples/SampleBook/sample.flow.json
dotnet run --project src/Flow.Cli -- validate samples/SampleBook/sample.flow.json
dotnet run --project src/Flow.Cli -- hash samples/SampleBook/sample.flow.json
dotnet run --project src/Flow.Cli -- render samples/SampleBook/sample.flow.json --html sample.html --width 390 --height 844
dotnet run --project src/Flow.Cli -- render samples/SampleBook/sample.flow.json --html-book sample-book
```

### Qualify and review a real EPUB

The corpus, automatic gate and assisted review are available through the CLI. They remain evidence workflows, not EPUB conformance certification. A real publication must be DRM-free and legally available to the caller. Flow records those declarations but cannot verify publication rights.

### Inventory a private EPUB directory

`epub-inventory` recursively discovers local `.epub` files without converting, copying, renaming or writing to them. It hashes every readable candidate, deduplicates identical payloads, inspects EPUB version, languages, reading order and resource counts, and separates corrupt, structurally unsuitable, protected and review-required candidates. Known EPUB font-obfuscation algorithms are reported for review rather than automatically called DRM.

The catalog must stay outside both the repository and the source directory:

```powershell
dotnet run --project src/Flow.Cli -- `
  --language pt-BR `
  --banner `
  epub-inventory C:\books `
  --output C:\flow-local\epub-inventory.json `
  --repository-root C:\src\FlowEngineNet
```

The deterministic JSON contains neutral candidate IDs, SHA-256 values, sizes, languages, structural counts, statuses and diagnostic codes. It excludes file and directory names, physical paths, titles, authors, publisher identifiers and publication content. An existing recognized catalog requires `--force`; unrelated files are never replaced.

To qualify every eligible candidate through the complete Flow pipeline twice, keep the report outside the repository and source directory and make the legal declarations explicitly:

```powershell
dotnet run --project src/Flow.Cli -- `
  --language pt-BR `
  --banner `
  epub-inventory-qualify C:\books `
  --report C:\flow-local\epub-qualification.json `
  --repository-root C:\src\FlowEngineNet `
  --legal-use `
  --drm-free
```

The qualification report contains neutral IDs, source and canonical hashes, phase and semantic counts, aggregated diagnostic codes and the repeated-run result. It omits physical paths, file names, editorial metadata and publication text. Protected, corrupt or structurally unsuitable candidates appear as explicitly skipped entries. The current private run covered six eligible publications twice: all completed the 12 automatic phases with stable evidence and no measured lost units. Embedded fonts remain typography approximations because Flow retains the authored family name but not the font bytes. Image-only links now retain safe typed destinations and no longer appear as unsupported evidence. This automatic batch is regression evidence, not human review or EPUB conformance certification.

The qualification report can then be classified without opening the EPUB files again. The command requires the exact SHA-256 of the input report and writes a separate private matrix:

```powershell
$qualification = 'C:\flow-local\epub-qualification.json'
$qualificationHash = (Get-FileHash $qualification -Algorithm SHA256).Hash

dotnet run --project src/Flow.Cli -- `
  --language pt-BR `
  epub-inventory-matrix $qualification `
  --qualification-sha256 $qualificationHash `
  --output C:\flow-local\epub-difference-matrix.json `
  --repository-root C:\src\FlowEngineNet
```

The matrix distinguishes approval, approximation, unsupported content, content loss, broken source references, Flow errors and cases that need human review. Automatic classification never completes the human-review field. Keep this file outside Git as well: it contains exact source and report hashes even though it omits editorial identity and book content.

To prepare assisted visual review for every qualified candidate, pass the same qualification file and verified hash. The command rediscovers the EPUBs by SHA-256 and creates neutral candidate directories; it does not persist book names or source paths in `corpus-review.json`.

```powershell
dotnet run --project src/Flow.Cli -- `
  --language pt-BR `
  epub-inventory-review C:\books `
  --qualification $qualification `
  --qualification-sha256 $qualificationHash `
  --output C:\flow-local\epub-visual-review `
  --repository-root C:\src\FlowEngineNet `
  --legal-use --drm-free --ui-language pt-BR
```

Open `index.html` in the output directory. Each candidate includes mobile and desktop books, beginning/middle/end samples and shortcuts for the TOC, images, linked figures, notes, tables, ruby, bidirectional text, SVG, MathML and the chapter associated with the largest diagnostic group when those features exist. The generated checklist remains inconclusive until a person records a decision.

In the current private corpus, all six candidates contain explicit automatic approximations and none has measured content loss, unsupported content or a Flow error. Embedded OTF/TTF resources use `EPUB074`, not the generic `EPUB009`: an installed font with the authored family name may be selected by the browser, otherwise the browser or reader uses its fallback. Flow does not inspect operating-system fonts during import because that would make evidence depend on the machine running the command.

Keep the EPUB, gate report and generated review package outside the repository:

```powershell
$epub = 'C:\books\book.epub'
$sha256 = (Get-FileHash $epub -Algorithm SHA256).Hash
$repository = (Get-Location).Path

dotnet run --project .\src\Flow.Cli -- --language pt-BR epub-qualify $epub `
  --candidate-id candidate-001 `
  --sha256 $sha256 `
  --report 'C:\flow-local\candidate-001-gate.json' `
  --repository-root $repository `
  --legal-use --drm-free

dotnet run --project .\src\Flow.Cli -- --language pt-BR epub-review $epub `
  --candidate-id candidate-001 `
  --sha256 $sha256 `
  --output 'C:\flow-local\candidate-001-review' `
  --repository-root $repository `
  --legal-use --drm-free --ui-language pt-BR
```

The gate runs the automatic pipeline at least twice. Its overall JSON status normally remains `inconclusive` until a person completes the separate checklist, but the command returns `0` when every automatic check passes. `epub-review` generates `review.html`, `review-checklist.json`, mobile and desktop packages. It never approves review decisions automatically.

These operational commands refuse to replace existing reports or review directories unless `--force` is present. A hidden destination-specific lock prevents concurrent Flow processes from writing the same output. The CLI prints a random execution ID for log correlation, while the lock stores only that ID, its state and a SHA-256 fingerprint of the normalized destination. It contains no book data or physical path and does not enter deterministic evidence. If a process is interrupted, rerun it with `--resume`; add `--force` as well when the interrupted operation was replacing a completed result. Recovery restarts processing from the verified EPUB and never treats partial JSON or HTML as complete.

Use `flow execution-status <destination>` to inspect that state without changing it. If abandoned transaction artifacts need removal, copy the exact ID shown by the status command into `flow execution-clean <destination> --execution-id <id>`. Cleanup refuses active, invalid or mismatched locks and never deletes unrelated files.

### Import a real EPUB and generate its HTML book

Pass the complete EPUB path to `flow import`. Quotes are recommended because book and directory names frequently contain spaces:

```powershell
Set-Location 'C:\caminho\para\FlowEngineNet'

$epub = 'C:\caminho\para\Meu livro.epub'
dotnet run --project .\src\Flow.Cli -- --language pt-BR import $epub
```

When `--output` is omitted, the CLI creates the `.flow.json` beside the EPUB. Its portable file name is derived from the book title, while the original title—including accents—remains in the document metadata. For the example above, the result can be:

```text
C:\caminho\para\meu_livro.flow.json
```

For a noisy publication, pass `--diagnostics-json <report.json>`. The terminal then shows totals, counts by stable diagnostic code, and at most 40 detail lines; the JSON file still contains every finding.

The optional `--metadata-json`, `--processing-json`, and `--source-map-json` sidecars expose the source evidence retained by the importer without adding it to the canonical Flow document:

```powershell
dotnet run --project .\src\Flow.Cli -- import $epub `
  --metadata-json '.\metadata.json' `
  --processing-json '.\processing.json' `
  --source-map-json '.\source-map.json'
```

The metadata report keeps OPF values and refinements. The processing report explains every manifest and spine decision. The source map connects EPUB resource paths and fragments to stable Flow node IDs. These files are deterministic diagnostic/editorial evidence; changing or deleting them does not change the `.flow.json` or its hash.

The CLI prints the exact generated path. Validate it and create the multi-file HTML book in a directory beside the EPUB:

```powershell
$flow = 'C:\caminho\para\meu_livro.flow.json'
$book = 'C:\caminho\para\meu_livro_book'

dotnet run --project .\src\Flow.Cli -- --language pt-BR validate $flow
dotnet run --project .\src\Flow.Cli -- --language pt-BR render $flow --html-book $book --ui-language pt-BR
Start-Process "$book\index.html"
```

This produces, side by side:

```text
C:\caminho\para\Meu livro.epub
C:\caminho\para\meu_livro.flow.json
C:\caminho\para\meu_livro_book\
```

`import` and `render` are intentionally separate today: importing creates the Flow document; rendering creates the `book` directory only when requested. This makes failures and intermediate validation visible. A future Reader should perform these steps transparently when opening an EPUB, without requiring the user to run this CLI workflow.

`--ui-language` changes only controls and navigation generated by Flow. It does not translate the title, chapters, TOC labels, notes, or other authored content. Supported values are `auto`, `pt-PT`, `pt-BR`, and `en`. To publish more than one interface language, render separate directories instead of duplicating indexes inside one package:

```powershell
dotnet run --project .\src\Flow.Cli -- render $flow --html-book 'D:\Livros\meu_livro_pt' --ui-language pt-PT
dotnet run --project .\src\Flow.Cli -- render $flow --html-book 'D:\Livros\meu_livro_ptbr' --ui-language pt-BR
dotnet run --project .\src\Flow.Cli -- render $flow --html-book 'D:\Livros\meu_livro_en' --ui-language en
```

To choose the output paths explicitly:

```powershell
dotnet run --project .\src\Flow.Cli -- import $epub --output 'D:\Livros\meu_livro.flow.json'
dotnet run --project .\src\Flow.Cli -- render 'D:\Livros\meu_livro.flow.json' --html-book 'D:\Livros\meu_livro_book'
```

The detailed [sample walkthrough](samples/SampleBook/README.md) explains prerequisites, expected output, what to compare, safe experiments, limitations, and how the committed evidence is verified.

## Versioning and compatibility

All `0.x` APIs, JSON fields, canonicalization rules, and layout profiles are experimental and may change. Canonicalization changes require a new named profile so that an existing document hash never silently acquires different semantics. The CLI can be packed and installed locally for testing, but no package is published yet; see the complete [known limitations](docs/known-limitations.md).

## License

Licensed under the [MIT License](LICENSE).
