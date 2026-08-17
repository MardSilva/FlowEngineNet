# Experimental EPUB import

`Flow.Epub` is an input adapter that maps a deliberately limited EPUB subset to an immutable `FlowDocument`. The adapter began in Flow 0.1 and is connected to the CLI in 0.2.0-alpha.1; it is not an EPUB 3.3 conformance checker or general-purpose reading system.

## Public contract

`IEpubImporter.ImportAsync` accepts a readable EPUB stream and returns an `EpubImportResult`. The result contains an optional document plus immutable diagnostics. `IsSuccess` is true only when a document was produced and no error diagnostic occurred. A document may still be returned with errors when recoverable content exists, allowing callers to inspect the partial result without treating it as a successful import.

`IEpubPublicationInspector.InspectAsync` is the non-converting entry point. It returns an immutable `EpubPublicationInspection` containing `EpubPackageInfo`, normalized manifest/spine entries, detected EPUB 3 Navigation Documents or EPUB 2 NCX resources, aggregate archive/resource sizes, media-type counts, and diagnostics. It deliberately does not parse spine XHTML or construct a `FlowDocument`.

The prototype reads:

- `META-INF/container.xml` and its declared package path;
- OPF metadata (`identifier`, `title`, `language`, creators, and description);
- manifest IDs, paths, media types, and properties;
- spine item references in declared reading order, including explicitly diagnosed `linear="no"` items;
- XHTML headings, paragraphs, basic ordered/unordered lists, links, images, figures/captions, block quotes, sections, horizontal rules, and preformatted code;
- common inline emphasis, strong text, underline, strikeout, inline code, and line breaks;
- EPUB 3 Navigation Documents with `nav epub:type="toc"`, including nested entries and inline label formatting;
- EPUB 2 NCX navigation as a fallback, including nested `navPoint` entries.

Internal XHTML links are translated to stable Flow anchors when their targets can be represented. IDs on inline-only constructs are explicitly diagnosed and mapped to the nearest containing semantic block. External `http`, `https`, and `mailto` links are retained. Unknown schemes are not activated.

Navigation destinations are resolved only after stable IDs have been assigned to every imported XHTML resource. Relative links may cross XHTML files, target a whole spine document, or include a fragment. Percent-encoded paths and fragments are decoded with URI rules and then pass the same archive-path and control-character checks as other EPUB references. A successful entry becomes a `TableOfContentsEntry` whose `DocumentAnchor` points to the resolved semantic `NodeId`; the source nesting depth becomes `Level` from 1 through 6. The Flow model represents this hierarchy as an ordered flat entry sequence plus explicit levels.

When both formats exist, a usable EPUB 3 Navigation Document takes precedence over NCX. Within EPUB 3, manifest order and then document order are deterministic; for NCX, the OPF spine `toc` reference is preferred before manifest order. A structurally present but unusable EPUB 3 TOC falls back to NCX. Different usable Navigation Document and NCX structures produce `EPUB022` instead of being reconciled silently.

TOC diagnostics are stable: `EPUB016` multiple TOCs, `EPUB017` malformed TOC structure, `EPUB018` missing destinations, `EPUB019` empty labels, `EPUB020` levels outside 1..6, `EPUB021` circular/self references, and `EPUB022` Navigation Document/NCX conflicts. Empty labels receive a deterministic source-reference label so their destination is not lost. Unresolvable and circular destinations are excluded because admitting them would make the resulting `FlowDocument` invalid; their label and source reference remain visible in diagnostics.

Unsupported block elements produce `EPUB010` and retain recoverable textual content in a paragraph. Unsupported manifest resources produce `EPUB009`. Missing resources, invalid references, and structural validation failures are explicit diagnostics; they are never silently accepted as a successful import.

## Security limits

The importer and inspector never extract entries to the filesystem and perform no network access. Both share the same archive/XML security implementation and apply these defenses before or while reading:

- archive entry count, input size, per-entry size, total uncompressed size, and compression-ratio limits;
- ordinal duplicate-path detection;
- normalized relative ZIP paths with rejection of absolute paths, backslashes, control characters, URI schemes, and traversal above the archive root;
- `XmlReader` with `DtdProcessing.Prohibit`, a null `XmlResolver`, and a nonzero `MaxCharactersInDocument`;
- bounded stream copies even after ZIP metadata has passed validation;
- scripts and styles are never executed or imported as canonical content.

Defaults are exposed through immutable `EpubImportLimits` so hosts can lower limits for their environment. Raising them should be an explicit trust decision.

## Test EPUB

`tests/Flow.Epub.Tests/MinimalEpubFactory.cs` creates a complete minimal EPUB ZIP in memory. It owns its metadata, container, OPF, two XHTML spine resources, internal link, lists, and one-pixel PNG. This avoids relying on an opaque or third-party binary fixture and makes every byte of the test publication reviewable.

The suite also generates EPUB 2/3, malicious, incomplete, Navigation Document, and NCX variants to verify version/navigation detection, DTD rejection, traversal rejection, TOC precedence and diagnostics, percent-encoded destinations, explicit missing-resource diagnostics, unsupported-content preservation, deterministic reports, and archive limits. The navigation integration test serializes the imported document and renders it to HTML, then proves that every TOC link and target retains the same semantic ID.

## Current boundary

The prototype does not yet support landmarks/page-list navigation semantics, CSS interpretation, manifest fallback chains, media overlays, scripting, SVG semantics, MathML, tables, audio/video, encryption/DRM, font embedding, EPUB CFI, fixed-layout publications, or full accessibility metadata. NCX labels are imported as text because NCX does not provide the XHTML inline-label model. TOC nesting deeper than six levels is diagnosed and clamped to the maximum representable Flow level.

The CLI exposes `flow epub-inspect <book.epub> [--json <report.json>]` and `flow import <book.epub> [--output <book.flow.json>]`. Inspection inventories publication structure without conversion. Import writes only after a complete, semantically valid result and reports diagnostics without silently accepting a partial result. This closes the first command-line pipeline, but it does not yet constitute the complete workflow “open any `.epub`, edit it, and publish it again.”

The mapping is evaluated against the [W3C EPUB 3.3 specification](https://www.w3.org/TR/epub-33/). The next interoperability work should prioritize accessibility metadata and content fallback behavior instead of inventing equivalent Flow-only vocabularies.
