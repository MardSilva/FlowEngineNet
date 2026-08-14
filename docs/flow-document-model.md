# Flow document model

## Status

The first Flow 0.1 semantic model, stable anchors, document index, and structural validator are implemented. They represent and validate content without depending on layout or rendering technology.

## Identity

`DocumentIdentity` contains a typed `DocumentId`, an optional version, and an optional previous-version ID. A `DocumentId` is an absolute URI, allowing the experimental Flow convention:

```text
urn:flow:document:550e8400-e29b-41d4-a716-446655440000
```

Flow 0.1 does not claim that this convention is a global identifier standard.

## Stable IDs

Every block-level `DocumentNode` has a `NodeId`. Assets use the separate `AssetId` type. These identifiers are value objects rather than unstructured strings.

The initial stable-ID rules are intentionally conservative:

- length from 1 through 128 characters;
- first character is a lowercase ASCII letter;
- remaining characters are lowercase ASCII letters, digits, hyphens, underscores, or periods;
- IDs do not encode page numbers, coordinates, fonts, viewport dimensions, or renderer state.

Comparison is ordinal and case-sensitive. Uppercase characters are invalid rather than normalized, so an ID has one canonical spelling. Constructors reject syntactically invalid IDs, while `DocumentValidator` detects duplicate IDs across the complete tree.

Syntax alone cannot determine whether a human chose a name such as `page-two` for visual reasons. The architectural guarantee is that `NodeId` belongs to `Flow.Core`, has no dependency on layout, and is never generated from pages, coordinates, fonts, or viewport state. Producers are required to derive IDs from canonical semantic structure or preserve them from a trusted source.

## Stable anchors and index

`DocumentAnchor` uses an ordinal, case-sensitive grammar:

```text
flow:<node-id>[/<node-id>...]
```

For example:

```text
flow:chapter-introduction/section-identity/p-002
```

`Parse` throws `FormatException` for malformed anchors, while `TryParse` provides a non-throwing API. An anchor contains from 1 through 64 stable-ID segments and does not allow empty paths, empty segments, query strings, fragments, or escaped visual coordinates.

Every `FlowDocument` builds an immutable `FlowDocumentIndex` once. The index keeps reading-order locations plus hash-based lookup by complete anchor and by `NodeId`. A one-segment anchor can resolve any globally unique node; a hierarchical anchor resolves its exact semantic path. Ambiguous or missing anchors do not resolve.

## Document structure

`FlowDocument` is the immutable aggregate root:

```text
FlowDocument
├── DocumentIdentity
├── DocumentMetadata
├── DocumentContent
│   └── DocumentNode[]
├── FlowAsset dictionary
├── DocumentPresentation (optional)
└── DocumentIntegrity (optional)
```

The root is not a rendered page and contains no device or viewport information. Collections are exposed as `ImmutableArray<T>` or `ImmutableDictionary<TKey, TValue>`. Constructors copy mutable input collections and asset bytes so later caller mutations cannot change an existing document.

## Block nodes

The basic block model supports:

- `Chapter` and `Section` as structural containers;
- `Heading`, with a locally enforced level from 1 through 6;
- `Paragraph` and `BlockQuote`;
- `OrderedList`, `UnorderedList`, and addressable `ListItem` nodes;
- `Figure`, which references a `FlowAsset` through `AssetId`;
- addressable `Caption` and `Footnote` nodes;
- `HorizontalRule` and `CodeBlock`;
- semantic `TableOfContents`, with optional typed entries and stable anchor targets. An empty entry collection reserves automatic generation for a later milestone.

A figure owns its optional caption relationship directly. Whether an asset exists and whether IDs are unique are document-level validation concerns and are deliberately not hidden inside node constructors.

## Inline nodes

Inline content is semantic and never stores raw HTML. The model supports `Text`, `Strong`, `Emphasis`, `Underline`, `Strikethrough`, `InlineCode`, `Link`, `FootnoteReference`, and `LineBreak`.

Formatting nodes contain other inline nodes, allowing nested meaning without an inheritance hierarchy for every combination. A `FootnoteReference` targets a typed `NodeId`; validation requires it to resolve to one unique `Footnote`.

## Validation

`DocumentValidator` returns an immutable `ValidationResult`. Each `ValidationDiagnostic` contains a stable code, severity, message, and optional node/anchor context. A result is valid when it has no error diagnostics.

The current validator checks:

- duplicate and syntactically invalid node IDs;
- structural placement of chapters, sections, list items, captions, and nested footnotes;
- heading-level jumps;
- figure assets;
- malformed and unresolved `flow:` links;
- footnote references;
- explicit TOC targets and maximum depth.

Chapters must be document-root children, sections belong to chapters or other sections, list items belong to lists, and captions belong to figures. Constructors make several invalid states impossible, but the document-wide pass remains necessary for cross-node invariants.

## Presentation and integrity boundaries

`DocumentPresentation` is currently an explicit empty value representing the optional presentation layer. Typed typography properties will be added in the dedicated presentation milestone instead of introducing premature CSS-like strings.

`DocumentIntegrity` can carry an algorithm, hash, and canonicalization version, but this milestone does not compute or verify them. Canonicalization and SHA-256 services remain the responsibility of `Flow.Security`.

Presentation, layout context, renderer constraints, and reader state will not participate in the canonical document hash.

## Deferred behavior

This model intentionally does not yet implement:

- JSON serialization;
- typography and style cascade;
- canonicalization, hashing, or signatures;
- automatic TOC entry generation;
- layout or rendering.

Those behaviors will be layered over the semantic model without adding dependencies from `Flow.Documents` to infrastructure projects.
