# Experimental EPUB import

`Flow.Epub` is an input adapter that maps a deliberately limited EPUB subset to an immutable `FlowDocument`. The adapter began in Flow 0.1 and is connected to the CLI in 0.2.0-alpha.1; it is not an EPUB 3.3 conformance checker or general-purpose reading system.

## Public contract

`IEpubImporter.ImportAsync` accepts a readable EPUB stream and returns an `EpubImportResult`. The result contains an optional document plus immutable diagnostics. `IsSuccess` is true only when a document was produced and no error diagnostic occurred. A document may still be returned with errors when recoverable content exists, allowing callers to inspect the partial result without treating it as a successful import.

The prototype reads:

- `META-INF/container.xml` and its declared package path;
- OPF metadata (`identifier`, `title`, `language`, creators, and description);
- manifest IDs, paths, media types, and properties;
- spine item references in declared reading order, including explicitly diagnosed `linear="no"` items;
- XHTML headings, paragraphs, basic ordered/unordered lists, links, images, figures/captions, block quotes, sections, horizontal rules, and preformatted code;
- common inline emphasis, strong text, underline, strikeout, inline code, and line breaks.

Internal XHTML links are translated to stable Flow anchors when their targets can be represented. IDs on inline-only constructs are explicitly diagnosed and mapped to the nearest containing semantic block. External `http`, `https`, and `mailto` links are retained. Unknown schemes are not activated.

Unsupported block elements produce `EPUB010` and retain recoverable textual content in a paragraph. Unsupported manifest resources produce `EPUB009`. Missing resources, invalid references, and structural validation failures are explicit diagnostics; they are never silently accepted as a successful import.

## Security limits

The importer never extracts entries to the filesystem and performs no network access. It applies these defenses before or while reading:

- archive entry count, input size, per-entry size, total uncompressed size, and compression-ratio limits;
- ordinal duplicate-path detection;
- normalized relative ZIP paths with rejection of absolute paths, backslashes, control characters, URI schemes, and traversal above the archive root;
- `XmlReader` with `DtdProcessing.Prohibit`, a null `XmlResolver`, and a nonzero `MaxCharactersInDocument`;
- bounded stream copies even after ZIP metadata has passed validation;
- scripts and styles are never executed or imported as canonical content.

Defaults are exposed through immutable `EpubImportLimits` so hosts can lower limits for their environment. Raising them should be an explicit trust decision.

## Test EPUB

`tests/Flow.Epub.Tests/MinimalEpubFactory.cs` creates a complete minimal EPUB ZIP in memory. It owns its metadata, container, OPF, two XHTML spine resources, internal link, lists, and one-pixel PNG. This avoids relying on an opaque or third-party binary fixture and makes every byte of the test publication reviewable.

The suite also generates malicious and incomplete variants to verify DTD rejection, traversal rejection, explicit missing-resource diagnostics, unsupported-content preservation, and archive limits.

## Current boundary

The prototype does not yet support CSS interpretation, EPUB navigation documents as Flow table-of-contents nodes, media overlays, scripting, SVG semantics, MathML, tables, audio/video, encryption/DRM, font embedding, EPUB CFI, fixed-layout publications, or full accessibility metadata. These resources are diagnosed when encountered.

The CLI exposes `flow import <book.epub> [--output <book.flow.json>]`. It writes only after a complete, semantically valid import and reports diagnostics without silently accepting a partial result. This closes the first command-line pipeline, but it does not yet constitute the complete workflow “open any `.epub`, edit it, and publish it again.”

The mapping is evaluated against the [W3C EPUB 3.3 specification](https://www.w3.org/TR/epub-33/). The next interoperability work should prioritize the standardized EPUB navigation document and accessibility metadata instead of inventing equivalent Flow-only vocabularies.
