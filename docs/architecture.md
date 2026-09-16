# Architecture

## Guiding invariant

`Document != Layout`. Canonical identity and semantic content must not depend on viewport dimensions, pagination, typography selected by a reader, operating system, or renderer technology.

## Project boundaries

- `Flow.Core` contains renderer-independent primitives and shared contracts.
- `Flow.Documents` contains the semantic document model, serialization, anchors, and validation.
- `Flow.Layout` owns reader state, resolves the style cascade, and transforms documents plus environmental context into renderer-independent layout decisions.
- `Flow.Rendering` defines rendering contracts.
- `Flow.Rendering.Html` produces standalone semantic HTML and a deterministic multi-file HTML book package.
- `Flow.Security` owns canonicalization, hashing, and experimental signing abstractions.
- `Flow.Epub` maps a deliberately limited EPUB subset into the semantic model.
- `Flow.Epub.Corpus` orchestrates optional end-to-end EPUB evidence across import, validation, integrity, layout, and HTML without adding outward dependencies to `Flow.Epub`.
- `Flow.Cli` is the command-line composition root.

Dependencies point inward: domain projects never reference presentation or infrastructure projects. The CLI may compose all projects, while HTML and EPUB remain adapters at the edge.

The approved direct graph is executable evidence in `ProjectDependencyConformanceTests`: Core has no project dependency; Documents depends only on Core; Layout depends on Core and Documents; Rendering depends on Documents and Layout; HTML depends only on Rendering; Security depends only on Documents; and EPUB depends only on Core and Documents. `Flow.Epub.Corpus` is an outward orchestration layer over Documents, EPUB, Layout, HTML, and Security. Its optional EPUBCheck adapter runs a trusted local process without adding Java or EPUBCheck dependencies to any domain/import project. The CLI composes the same runtime adapters independently. The CLI-to-EPUB reference became intentional when `flow import` was introduced in 0.2.0-alpha.1.

## Rendering pipeline

An importer or serializer creates a `FlowDocument`. The layout engine combines that immutable model with a `LayoutContext`. A renderer consumes the resulting layout without changing canonical content.

`AdaptiveLayoutEngine` implements the flowing-reading stage. It validates the source document, resolves the style cascade, chooses a viewport profile, and recursively projects semantic nodes into `LayoutNode` values while preserving every `NodeId`. `LayoutProfile` carries constraints such as column count, margins, maximum content width, and responsive-figure behavior; it never contains measured positions or pages. See [adaptive-layout.md](adaptive-layout.md).

`ReadingMode.Paged` and `ReadingMode.Print` are reserved extension points and are intentionally unsupported in the 0.1 engine.

## HTML rendering

`Flow.Rendering` defines the renderer boundary and immutable rendered bytes. `Flow.Rendering.Html` implements deterministic standalone HTML5 plus an additive multi-file book package with shared CSS/assets, cross-file anchor rewriting, a renderer-only footnote placement graph, hierarchical TOC, editorial logical progress, localized accessible landmarks, and script-free progressive appearance controls. Both consume resolved layout state without mutating or recanonicalizing the source document. Before rendering, the established standalone mapping verifies semantic validity, document/layout identity, and the one-to-one relationship between semantic and layout nodes. The CLI computes canonical integrity and passes it into the package adapter, avoiding a new HTML-to-Security project dependency. See [html-renderer.md](html-renderer.md) and [html-book-package.md](html-book-package.md).

## Command-line composition

`Flow.Cli` contains a framework-free parser that produces typed command records, operations that compose the domain services, and a thin application boundary for exit codes and diagnostics. It does not place command-line concerns in the document, layout, security, or rendering projects. See [cli.md](cli.md).

## Style cascade

`TypographyResolver` is a pure service in `Flow.Layout`. It resolves styles in this order:

```text
Flow defaults
< document presentation
< user reading preferences
< renderer safety constraints
```

Author styles are partial and immutable. Resolution produces a complete `ResolvedReadingStyle` for all typography roles without modifying the source document. Reader font choices, scaling, spacing, margins, and theme remain runtime state in `Flow.Layout`; they are not properties of `FlowDocument`.

Safety constraints run last and may clamp font sizes, line height, paragraph spacing, and content margins or require an accessible font/theme. Typed lengths are compared only when their units match because conversion requires renderer context.

## Security boundary

`Flow.Documents` owns the deterministic, readable `flow-json-0.1` development serializer. `Flow.Security` independently projects a document into `flow-c14n-0.1` bytes and hashes them with standard SHA-256. Keeping the writers separate prevents formatting or presentation round-trip changes from silently changing the canonical profile.

Canonicalization includes identity, all currently modeled canonical metadata, semantic structure/content, asset references, asset metadata, and complete asset bytes. It excludes `DocumentPresentation`, `DocumentIntegrity`, layout context, `UserReadingPreferences`, resolved styles, renderer constraints, pagination, and renderer output. See [canonicalization.md](canonicalization.md) for the normative 0.1 field list.

The experimental signature layer signs exactly the versioned canonical bytes with standard .NET RSA-PSS/SHA-256 APIs. Keys are supplied and owned by the caller. Flow defines no certificate, PKI, key discovery, revocation, or trust policy, and a successful mathematical verification is not treated as proof of real-world identity. See [signatures.md](signatures.md).

## EPUB interoperability

EPUB is an input adapter, not a dependency of the core model. Unsupported constructs must produce diagnostics rather than disappear silently.

`EpubImporter` reads the container, OPF manifest/spine, and XHTML resources through bounded archive streams. It never extracts files, resolves external XML entities, or performs network access. Conversion preserves spine order and creates stable semantic IDs independent of pagination or visual position. Recoverable unsupported text is retained with a warning; unsafe or structurally missing input produces an error. See [epub-import.md](epub-import.md).
