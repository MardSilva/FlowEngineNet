# Flow Engine .NET

> **Experimental:** Flow 0.x is a research project, not a standardized file format. Do not use it yet for archival, legal, or production-critical documents.

Flow Engine .NET explores a document model in which canonical identity and semantic content remain independent from viewport, typography, layout, pagination, and renderer technology.

The central invariant is:

```text
Document != Layout != Rendering
```

The project is not currently a replacement for PDF or EPUB. Version 0.1 is building and testing the engine foundations needed before a complete reader, editor, or publishing workflow can exist.

## Current milestone

The current development milestone is **`0.1.0-beta.1`**.

Implemented:

- immutable semantic document and inline/block node model;
- typed stable identifiers, document anchors, node indexing, and structural validation;
- typed, optional presentation and typography intentions;
- reader-preference cascade with renderer safety constraints;
- deterministic experimental `.flow.json` serialization;
- `flow-c14n-0.1` canonicalization and SHA-256 document hashes;
- renderer-independent adaptive layout for `ReadingMode.Flow`;
- deterministic standalone HTML5 rendering with embedded CSS and assets;
- small, medium, and large viewport profiles;
- responsive figures, semantic ID preservation, and typed layout intentions;
- unit and semantic-conformance tests.

Not implemented yet:

- renderers other than standalone HTML;
- EPUB import;
- functional CLI commands;
- paged and print layout modes;
- complete sample book, reader, or editor;
- signatures, provenance, DRM, or cloud publishing services.

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

The layout operation validates the semantic document first. Invalid IDs, hierarchy, anchors, asset references, footnotes, or table-of-contents destinations prevent a layout from being produced.

## Solution structure

| Project | Responsibility | Status |
| --- | --- | --- |
| `Flow.Core` | Stable IDs, anchors, and shared primitives | Implemented |
| `Flow.Documents` | Semantic model, validation, presentation, and `.flow.json` | Implemented for 0.1 alpha |
| `Flow.Layout` | Style cascade and adaptive Flow layout | Implemented for alpha.4 |
| `Flow.Security` | Canonicalization and SHA-256 integrity | Implemented for 0.1 alpha |
| `Flow.Rendering` | Renderer contracts and immutable rendered output | Implemented for beta.1 |
| `Flow.Rendering.Html` | Deterministic standalone semantic HTML adapter | Implemented for beta.1 |
| `Flow.Epub` | EPUB import adapter | Planned |
| `Flow.Cli` | Command-line composition root | Foundation only |

Dependencies point inward: the document domain does not reference layout or renderer projects. EPUB and HTML remain adapters at the edge.

## Requirements and build

The repository pins **.NET SDK 10.0.302** through `global.json`.

```powershell
dotnet restore Flow.sln
dotnet format Flow.sln --verify-no-changes
dotnet build Flow.sln
dotnet test Flow.sln --no-build
```

At `0.1.0-beta.1`, the active suites contain 102 passing tests.

## Documentation

- [Architecture](docs/architecture.md)
- [Semantic document model](docs/flow-document-model.md)
- [Adaptive layout](docs/adaptive-layout.md)
- [Standalone HTML renderer](docs/html-renderer.md)
- [Canonicalization and hashing](docs/canonicalization.md)
- [Roadmap](docs/roadmap.md)
- [Research findings](docs/research-findings.md)

## Roadmap direction

The next useful increments are:

1. build a limited, diagnostic-first EPUB importer;
2. connect EPUB import, layout, and HTML rendering in the CLI;
3. create a complete sample book and end-to-end conformance fixture;
4. harden accessibility and interoperability from real EPUB evidence.

A document can only be considered end-to-end usable when an EPUB can be imported without silent semantic loss, represented as a valid `FlowDocument`, laid out, rendered, inspected, and round-tripped through the supported Flow format. The repository has not reached that point yet.

## Versioning and compatibility

All `0.x` APIs, JSON fields, canonicalization rules, and layout profiles are experimental and may change. Canonicalization changes require a new named profile so that an existing document hash never silently acquires different semantics.

## License

Licensed under the [MIT License](LICENSE).
