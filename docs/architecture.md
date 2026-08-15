# Architecture

## Guiding invariant

`Document != Layout`. Canonical identity and semantic content must not depend on viewport dimensions, pagination, typography selected by a reader, operating system, or renderer technology.

## Project boundaries

- `Flow.Core` contains renderer-independent primitives and shared contracts.
- `Flow.Documents` contains the semantic document model, serialization, anchors, and validation.
- `Flow.Layout` owns reader state, resolves the style cascade, and transforms documents plus environmental context into renderer-independent layout decisions.
- `Flow.Rendering` defines rendering contracts.
- `Flow.Rendering.Html` produces standalone semantic HTML and CSS.
- `Flow.Security` owns canonicalization, hashing, and experimental signing abstractions.
- `Flow.Epub` maps a deliberately limited EPUB subset into the semantic model.
- `Flow.Cli` is the command-line composition root.

Dependencies point inward: domain projects never reference presentation or infrastructure projects. The CLI may compose all projects, while HTML and EPUB remain adapters at the edge.

## Rendering pipeline

An importer or serializer creates a `FlowDocument`. The layout engine combines that immutable model with a `LayoutContext`. A renderer consumes the resulting layout without changing canonical content.

`AdaptiveLayoutEngine` implements the flowing-reading stage. It validates the source document, resolves the style cascade, chooses a viewport profile, and recursively projects semantic nodes into `LayoutNode` values while preserving every `NodeId`. `LayoutProfile` carries constraints such as column count, margins, maximum content width, and responsive-figure behavior; it never contains measured positions or pages. See [adaptive-layout.md](adaptive-layout.md).

`ReadingMode.Paged` and `ReadingMode.Print` are reserved extension points and are intentionally unsupported in the alpha.4 engine.

## HTML rendering

`Flow.Rendering` defines the renderer boundary and immutable rendered bytes. `Flow.Rendering.Html` implements the first adapter: deterministic standalone HTML5 with embedded CSS and assets. It consumes resolved layout state without mutating or recanonicalizing the source document. Before rendering, it verifies semantic validity, document/layout identity, and the one-to-one relationship between semantic and layout nodes. See [html-renderer.md](html-renderer.md).

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

## EPUB interoperability

EPUB is an input adapter, not a dependency of the core model. Unsupported constructs must produce diagnostics rather than disappear silently.
