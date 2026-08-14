# Architecture

## Guiding invariant

`Document != Layout`. Canonical identity and semantic content must not depend on viewport dimensions, pagination, typography selected by a reader, operating system, or renderer technology.

## Project boundaries

- `Flow.Core` contains renderer-independent primitives and shared contracts.
- `Flow.Documents` contains the semantic document model, serialization, anchors, and validation.
- `Flow.Layout` transforms documents plus environmental context into renderer-independent layout decisions.
- `Flow.Rendering` defines rendering contracts.
- `Flow.Rendering.Html` produces standalone semantic HTML and CSS.
- `Flow.Security` owns canonicalization, hashing, and experimental signing abstractions.
- `Flow.Epub` maps a deliberately limited EPUB subset into the semantic model.
- `Flow.Cli` is the command-line composition root.

Dependencies point inward: domain projects never reference presentation or infrastructure projects. The CLI may compose all projects, while HTML and EPUB remain adapters at the edge.

## Rendering pipeline

An importer or serializer creates a `FlowDocument`. The layout engine combines that immutable model with a `LayoutContext`. A renderer consumes the resulting layout without changing canonical content.

## Security boundary

Canonicalization includes identity, selected metadata, assets, and semantic content. It excludes layout context, user reading preferences, and renderer output. Standard .NET cryptographic primitives will be used; Flow will not define cryptographic algorithms.

## EPUB interoperability

EPUB is an input adapter, not a dependency of the core model. Unsupported constructs must produce diagnostics rather than disappear silently.
