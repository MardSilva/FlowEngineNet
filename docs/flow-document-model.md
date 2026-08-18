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

## Presentation and typography

`DocumentPresentation` is optional. A document with `Presentation == null` remains complete and usable. When present, it contains a `TypographySet`, an optional immutable `NodeTypography` map keyed by stable `NodeId`, plus small typed presentation intentions for headings, paragraphs, figures, captions, footnotes, code blocks, and the table of contents. Per-node typography allows an importer to preserve a class/ID-specific author intention without widening it to every node sharing a role.

`TypographySet` maps independent `TypographyRole` values to partial `TypographyStyle` values. The supported roles are body, chapter title, headings 1 through 6, subtitle, TOC title and levels 1 through 3, caption, footnote, block quote, and code. A missing style or property means “unspecified”, allowing defaults and future user preferences to participate in the cascade without mutating the document.

`TypographyStyle` supports:

- one semantic font-family name;
- typed font size, weight, and style;
- unitless line height;
- typed letter spacing;
- alignment and text transformation enums;
- typed underline and line-through decoration flags;
- typed margins and indentation.

`Length` preserves its numeric value and unit as `Pixel`, `RootEm`, `Em`, or `Percent`. It is not a CSS string. Font-family values reject CSS lists and expression punctuation; renderer-specific fallback lists will belong to renderer policy rather than the canonical document model.

Presentation intentions express preferences such as keeping a heading with following content, keeping a figure with its caption, preferred figure placement, maximum semantic width, preserving code whitespace, or choosing a footnote presentation. `BottomOfPage` is a preference that a capable renderer may decline; it stores no page number, position, or computed pagination result.

No presentation type stores coordinates, viewport dimensions, rendered dimensions, page numbers, or device state.

## User preferences and resolved styles

`UserReadingPreferences` belongs to `Flow.Layout`, not `Flow.Documents`. It can override body and heading font families independently, apply global and heading-specific font scales, scale line height, set paragraph spacing and content margins, and choose a reading theme.

`TypographyResolver` applies four immutable layers:

```text
Flow defaults
< DocumentPresentation
< UserReadingPreferences
< RendererSafetyConstraints
```

The result contains complete `ResolvedTypographyStyle` values for every typography role plus optional resolved styles on layout nodes, the resolved content margin, and theme. Partial author role and node properties inherit from Flow defaults. User font choices do not replace the code font unless renderer safety explicitly requires one font for all roles.

Renderer limits are the highest-precedence accessibility layer. They clamp font size and line height and can establish minimum paragraph spacing/content margins, a required font, or a required theme. Length limits only compare values with matching units; a renderer must provide environmental conversion before constraining different units.

Resolution is pure: it allocates a new resolved result and never mutates `FlowDocument`, `DocumentPresentation`, `TypographySet`, or `TypographyStyle`. Preferences, constraints, and resolved values are reader/runtime state and must not participate in canonicalization or document identity.

## Integrity boundary

`DocumentIntegrity` can carry an algorithm, hash, and canonicalization version. `Flow.Security` now provides `FlowDocumentCanonicalizer` and `Sha256DocumentIntegrityService`; the stored integrity record itself is excluded from its hash input to avoid recursion.

`Flow.Security` also provides an experimental local RSA signature proof of concept over the same canonical bytes. `DocumentSignature` is not part of `FlowDocument` and is not serialized in `flow-json-0.1`; key ownership, transport, and trust remain external. See [signatures.md](signatures.md).

Presentation, layout context, renderer constraints, and reader state will not participate in the canonical document hash.

The experimental `flow-json-0.1` serializer preserves presentation and integrity for round-trip development interchange. It is deliberately separate from the compact `flow-c14n-0.1` projection. The exact included and excluded fields are specified in [canonicalization.md](canonicalization.md).

## Deferred behavior

This model intentionally does not yet implement:

- automatic TOC entry generation;
- embedded signature transport or trust policy;
- semantic nodes for tables, mathematics, rich media, per-span language, and annotations.

Those behaviors can be layered over the semantic model without adding dependencies from `Flow.Documents` to infrastructure projects. Adaptive Flow layout, standalone HTML rendering, and a limited EPUB importer already exist in outward projects.
