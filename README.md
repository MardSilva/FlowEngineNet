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
- `flow-c14n-0.1` canonicalization and SHA-256 document hashes;
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
- [Canonicalization and hashing](docs/canonicalization.md)
- [Experimental document signatures](docs/signatures.md)
- [Experimental EPUB import](docs/epub-import.md)
- [EPUB fidelity report](docs/epub-fidelity.md)
- [Experimental EPUB corpus catalog](docs/epub-corpus.md)
- [Public EPUB corpus coverage matrix](docs/epub-corpus-matrix.md)
- [0.1 conformance profile](docs/conformance.md)
- [Known limitations](docs/known-limitations.md)
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

The EPUB work now includes measured progress and cancellation, a deterministic legal-corpus catalog, bounded offline discovery, end-to-end execution, optional local EPUBCheck evidence, reviewed baselines for three project-owned fixtures, a typed structural/reference audit, and transactional material for assisted local review. The review package provides mobile and desktop HTML, beginning/middle/end samples, feature shortcuts, hashes and a separate human checklist without copying licensed material into Git. The first local large-publication run completed deterministic import, validation, round-trip, integrity, layout, HTML and structural auditing, but failed its fidelity gate because inline image occurrences still lack a semantic Flow representation. Human review and that fidelity correction remain open. This does not justify a universal EPUB-support, performance, or accessibility-conformance claim. See [EPUB performance, progress, and cancellation](docs/epub-performance.md), the [experimental EPUB corpus](docs/epub-corpus.md), and its [public coverage matrix](docs/epub-corpus-matrix.md).

The qualified fixtures and the locally supplied real publication now complete that end-to-end path for the documented subset: import without measured silent loss, a valid `FlowDocument`, round-trip, layout, inspection, and rendering. Broader EPUB coverage and the large-book gate remain open, so this is not yet an “open any EPUB” guarantee.

## CLI quick start

To see the current end-to-end result without generating anything, open the committed mobile and desktop files:

```powershell
Start-Process samples/SampleBook/mobile.html
Start-Process samples/SampleBook/desktop.html
```

To reproduce the pipeline from the semantic source:

```powershell
dotnet run --project src/Flow.Cli -- epub-inspect path/to/book.epub --json epub-report.json
dotnet run --project src/Flow.Cli -- import path/to/book.epub --diagnostics-json import-report.json --fidelity-report fidelity.json
dotnet run --project src/Flow.Cli -- inspect samples/SampleBook/sample.flow.json
dotnet run --project src/Flow.Cli -- validate samples/SampleBook/sample.flow.json
dotnet run --project src/Flow.Cli -- hash samples/SampleBook/sample.flow.json
dotnet run --project src/Flow.Cli -- render samples/SampleBook/sample.flow.json --html sample.html --width 390 --height 844
dotnet run --project src/Flow.Cli -- render samples/SampleBook/sample.flow.json --html-book sample-book
```

### Import a real EPUB and generate its HTML book

Pass the complete EPUB path to `flow import`. Quotes are recommended because book and directory names frequently contain spaces:

```powershell
Set-Location 'C:\caminho\para\FlowEngineNet'

$epub = 'C:\caminho\para\Meu livro.epub'
dotnet run --project .\src\Flow.Cli -- import $epub
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

dotnet run --project .\src\Flow.Cli -- validate $flow
dotnet run --project .\src\Flow.Cli -- render $flow --html-book $book --ui-language pt-PT
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

All `0.x` APIs, JSON fields, canonicalization rules, and layout profiles are experimental and may change. Canonicalization changes require a new named profile so that an existing document hash never silently acquires different semantics. No packages are published for 0.1; see the complete [known limitations](docs/known-limitations.md).

## License

Licensed under the [MIT License](LICENSE).
