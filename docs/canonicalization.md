# Canonicalization and document hash

## Status

Flow 0.1 uses the experimental canonicalization profile `flow-c14n-0.1`. This profile is versioned independently from the human-readable interchange format `flow-json-0.1`.

The `.flow.json` representation is not a standardized file format. It is a deterministic, indented development format that can round-trip the current document model, including optional presentation and stored integrity metadata.

## Canonical byte rules

`flow-c14n-0.1` produces compact UTF-8 JSON with:

- no byte-order mark;
- no insignificant whitespace;
- fixed property order defined by this profile;
- ordinal ordering of assets by `AssetId.Value`;
- semantic array order preserved for authors, document nodes, inline nodes, list items, TOC entries, and child nodes;
- JSON string escaping provided by the stable .NET `Utf8JsonWriter` contract;
- asset bytes encoded as standard Base64 inside JSON strings;
- no normalization, trimming, case folding, or locale-sensitive comparison of canonical strings.

The root starts with `"canonicalization":"flow-c14n-0.1"` as domain separation. Implementations must not insert additional fields into this profile without assigning a new canonicalization version.

## Canonical fields

The following fields participate, in the listed order.

### Identity

- `DocumentIdentity.Id`
- `DocumentIdentity.Version`, including explicit null
- `DocumentIdentity.PreviousVersionId`, including explicit null

### Metadata

- `DocumentMetadata.Title`
- `DocumentMetadata.Language`, including explicit null
- `DocumentMetadata.Authors`, preserving author order
- `DocumentMetadata.Subtitle`, including explicit null
- `DocumentMetadata.Description`, including explicit null

### Semantic tree

Every node contributes its node-type discriminator and stable `NodeId`, followed by all semantic properties for that type:

- structural child order for chapters, sections, block quotes, list items, and footnotes;
- heading level and inline content;
- paragraph and caption inline content;
- ordered-list start and list-item order;
- unordered-list item order;
- figure asset reference, alternative text, and caption;
- table caption/head/body/foot relationships, row and cell order, cell kind, positive column/row spans, header scope, header-ID references, and cell block content;
- block and inline mathematical element names, ordinal attribute names/values, child order, token text, and optional textual alternatives;
- normalized inline language tags, bidirectional direction/mode, ruby base/annotation/fallback node kinds, and their child order;
- code text and optional language;
- TOC title, maximum depth, entry order, entry level, target anchor, and label;
- inline text and formatting structure;
- inline code;
- link target and children;
- footnote target ID and, when present, its ordered inline reference label;
- line-break presence.

### Assets

Assets are sorted ordinally by stable asset ID. Each asset contributes:

- `FlowAsset.Id`
- `FlowAsset.MediaType`
- `FlowAsset.FileName`
- complete asset bytes encoded as Base64

Both the semantic figure-to-asset reference and the referenced asset record therefore participate.

## Explicit exclusions

The following never participate in `flow-c14n-0.1`:

- `DocumentPresentation`, role/node typography, presentation intentions, and author theme;
- `UserReadingPreferences`;
- resolved reading styles and Flow defaults;
- renderer safety constraints;
- layout context, viewport, device class, coordinates, or dimensions;
- pagination and reading mode;
- renderer output or renderer implementation;
- `DocumentIntegrity`, because including a stored hash in its own input would be recursive.
- `DocumentSignature`, which is external evidence over the completed canonical bytes and is not part of `FlowDocument`.

Changing any excluded value must leave canonical bytes and the document hash unchanged.

## Hash profile

`Sha256DocumentIntegrityService` computes SHA-256 over the exact canonical bytes. `DocumentHash` reports:

- algorithm: `SHA-256`;
- hash: 64 uppercase hexadecimal characters;
- canonicalization version: `flow-c14n-0.1`.

No Flow-specific cryptographic primitive is introduced.

The experimental signature service applies standard RSA-PSS/SHA-256 directly to these canonical bytes. Signature verification and trust semantics are documented separately in [signatures.md](signatures.md).

## Relationship to JSON Canonicalization Scheme

[RFC 8785](https://www.rfc-editor.org/rfc/rfc8785.html) defines the JSON Canonicalization Scheme (JCS), including deterministic primitive serialization, lexicographic property sorting, UTF-8 output, and removal of whitespace. `flow-c14n-0.1` is not declared JCS-compatible: it uses a versioned semantic projection and fixed schema order rather than lexicographic property sorting.

JCS could replace the final JSON byte-normalization layer in a future profile. It would not remove the need for a Flow projection that decides which document fields are canonical and excludes reader/presentation state. Any such change requires a new canonicalization version and conformance vectors.

Flow 0.1 has no independent implementation or published interoperability vectors outside this repository. It also performs no Unicode normalization and holds complete canonical bytes in memory. See [known limitations](known-limitations.md).
