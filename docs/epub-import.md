# Experimental EPUB import

`Flow.Epub` is an input adapter that maps a deliberately limited EPUB subset to an immutable `FlowDocument`. The adapter began in Flow 0.1 and is connected to the CLI in 0.2.0-alpha.1; it is not an EPUB 3.3 conformance checker or general-purpose reading system.

## Public contract

`IEpubImporter.ImportAsync` accepts a readable EPUB stream and returns an `EpubImportResult`. The result contains an optional document plus immutable diagnostics. `IsSuccess` is true only when a document was produced and no error diagnostic occurred. A document may still be returned with errors when recoverable content exists, allowing callers to inspect the partial result without treating it as a successful import.

The progress overload reports typed noncanonical phases and the reference result includes optional `EpubImportMetrics`. Progress, runtime measurements, cancellation, memory caveats, host limits, and the optional external load gate are documented in [EPUB performance](epub-performance.md). These observations cannot alter stable IDs, reading order, diagnostics, serialization, or hashes.

`IEpubPublicationInspector.InspectAsync` is the non-converting entry point. It returns an immutable `EpubPublicationInspection` containing `EpubPackageInfo`, normalized manifest/spine entries, detected EPUB 3 Navigation Documents or EPUB 2 NCX resources, aggregate archive/resource sizes, media-type counts, and diagnostics. It deliberately does not parse spine XHTML or construct a `FlowDocument`.

The prototype reads:

- `META-INF/container.xml` and its declared package path;
- OPF metadata (`identifier`, `title`, `language`, creators, and description);
- manifest IDs, paths, media types, and properties;
- spine item references in declared reading order, including explicitly diagnosed `linear="no"` items;
- XHTML headings, paragraphs, basic ordered/unordered lists, links, images, figures/captions, block quotes, sections, horizontal rules, and preformatted code;
- common inline emphasis, strong text, underline, strikeout, inline code, and line breaks;
- safe block and inline Presentation MathML structure with textual alternatives when supplied;
- inherited and local `lang`/`xml:lang`, typed `dir`, `bdi`/`bdo`, and simple `ruby`/`rt`/`rp` semantics;
- EPUB 3 Navigation Documents with `nav epub:type="toc"`, including nested entries and inline label formatting;
- EPUB 2 NCX navigation as a fallback, including nested `navPoint` entries.

## OPF metadata mapping

The importer resolves the OPF package `unique-identifier` against all usable `dc:identifier` elements instead of assuming the first identifier is canonical. It reads EPUB 2 `opf:scheme`, `opf:role`, `opf:file-as`, and `meta name="cover"`, plus EPUB 3 `meta property`, `refines`, `scheme`, title refinements, creator/contributor roles, `dcterms:modified`, `cover-image`, and recognized schema.org accessibility properties.

Only fields already defined by canonical `DocumentMetadata` are copied into the `FlowDocument`: selected main title, selected subtitle, first valid language, creators with no role or the `aut` role, and first description. Source order is preserved for authors. A title refined as `main` takes precedence over untyped titles; `display-seq` and then source order break ties. A title refined as `subtitle` supplies the subtitle. Multiple valid languages remain in the source report while the first becomes the document language.

`EpubImportResult.MetadataReport` exposes an immutable, typed, deliberately noncanonical `EpubMetadataReport`. It retains identifiers, all titles and refinements, creators, contributors and roles, publishers, languages, descriptions, subjects, dates, rights, modified time, cover declaration, accessibility values, and raw typed EPUB 3 metadata properties with their `refines` relationship. The report is not attached to `FlowDocument`, is not written to `.flow.json`, and does not affect canonical bytes, identity, or hashes. CLI import currently writes the canonical document and diagnostics; callers that need source provenance must retain the API result separately.

Metadata diagnostics use `EPUB023` for invalid values, `EPUB024` for orphan or ambiguous refinements, `EPUB025` for conflicting declarations, `EPUB026` for structurally invalid BCP 47 language tags, and `EPUB027` for absent or unresolved identifiers. Recoverable metadata faults are warnings and use deterministic fallbacks. A declared cover that is absent from the archive remains a missing-resource error.

## Manifest and spine processing

The OPF spine is the only reading-order authority. The importer never uses ZIP entry order, manifest order, filenames, or alphabetical sorting to arrange semantic content. Every `itemref` receives its zero-based declared position. Missing `linear` and explicit `linear="yes"` are primary reading-order content; `linear="no"` is supplementary content, remains at its declared position, and produces an explicit diagnostic. Invalid linear values are diagnosed and conservatively treated as `yes`.

Manifest properties, `fallback`, and `media-overlay` attributes are retained. A spine item is imported directly only when its selected resource exists and is valid XHTML. For a missing or non-XHTML resource, the importer follows manifest fallback IDs in declared links until it finds existing XHTML. It does not execute or interpret intermediate resources. Broken links and cycles terminate resolution with explicit diagnostics. A repeated `idref` remains repeated in reading order and receives a deterministic occurrence suffix so semantic IDs remain unique.

`EpubImportResult.ProcessingReport` contains a noncanonical `EpubPackageProcessingReport`. Its manifest entries preserve OPF declaration order and expose ID, normalized path, media type, properties, fallback, media overlay, and archive availability. Its spine decisions preserve exact spine position and classify each result as `Included`, `Substituted`, or `Excluded`, with a typed reason, linear/supplemental role, repeated-reference flag, selected resource, and complete fallback chain. Like the metadata report, this evidence is not written to `.flow.json` and does not affect canonical identity or hashes.

## Fidelity analysis

`IEpubFidelityAnalyzer` produces a typed, noncanonical `EpubFidelityReport` from the import result. It reconciles source measurements, destination semantic counts, package/spine decisions, `EpubSourceMap`, and diagnostics without changing `FlowDocument`. Status counts partition each measured source quantity into preserved, transformed, approximated, unsupported, or lost units. Zero-source percentages are `N/A`, not `100%`. Diagnostic-derived findings remain separately traceable and do not replace the original diagnostics.

The CLI option `--fidelity-report <fidelity.json>` writes the deterministic report beside `--diagnostics-json` when requested, including a partial report after failed import. The exact metric definitions, percentage formula, JSON contract, and nonconformance disclaimer are documented in [EPUB fidelity report](epub-fidelity.md).

Media overlays are never played or fetched. `EPUB031` reports each declaration and unresolved overlay reference. Other processing diagnostics are `EPUB028` circular fallback, `EPUB029` broken fallback, `EPUB030` repeated spine item, `EPUB032` invalid linear value, and `EPUB033` duplicate manifest resource path. Unsupported manifest properties are retained in the report and diagnosed rather than silently interpreted.

Internal XHTML links are translated to stable Flow anchors when their targets can be represented. IDs on inline-only constructs are explicitly diagnosed and mapped to the nearest containing semantic block. External `http`, `https`, and `mailto` links are retained. Unknown schemes are not activated.

## Images, covers, and asset safety

Raster image bytes are identified independently of file extension and OPF declarations. The importer accepts JPEG, PNG, static GIF, and WebP headers, reads their dimensions without platform-specific imaging libraries, and stores the detected MIME type in `FlowAsset`. This keeps detection deterministic on Windows and Linux. WebP is retained because the current standalone HTML renderer can embed it; another output adapter remains responsible for declaring or providing its own WebP capability.

EPUB 2 cover metadata and the EPUB 3 `cover-image` manifest property use deterministic precedence rules. A safely imported cover is loaded even when no XHTML figure references it, and `EpubCoverMetadata.AssetId` records its deduplicated Flow asset in the noncanonical metadata report. When imported reading content references that same asset through `img` or XHTML-embedded SVG `image`, the resulting `Figure` is identified by the optional typed `DocumentPresentation.Cover` intent. That intent survives readable `.flow.json` round trips but, like all presentation, remains outside canonical bytes and hashes. A cover asset with no semantic occurrence in the spine remains an asset/report association rather than causing invented reading content.

`img`, `figure`/`figcaption`, and `picture`/`source` are supported. XHTML-embedded `<svg><image href>` and legacy `xlink:href` resolve only safe local manifest resources, including normalized dot segments and percent-encoding. SVG composition is not copied into Flow: each referenced raster becomes an ordered `Figure`, while title, description, alt, XHTML fallback text, and a verified publication title provide its textual alternative. A surrounding `figcaption` remains the first figure's caption. Multiple SVG images, unrepresented vector graphics, conflicting/missing/external references, and failed associations are diagnosed rather than silently flattened. A safe XHTML `img` fallback is used when the SVG reference cannot produce an asset.

A `picture` considers unconditional supported sources in document order and then its required `img` fallback. Media queries cannot be evaluated during semantic import without a viewport, so conditional sources are diagnosed and skipped. For a multi-candidate `srcset`, the first candidate is selected deterministically and the unrepresented responsive choice is diagnosed. Missing textual alternatives produce `EPUB041`; an explicitly empty XHTML `alt` remains a valid decorative declaration.

Manifest image fallback chains are followed without executing intermediate resources. Missing, invalid, oversized, animated, or unsupported primary images may select the next safe image fallback, reported as `EPUB044`. No URL is fetched: absolute and unsafe sources are rejected, while recoverable alt text becomes a paragraph when no image survives.

Every candidate is bounded by `MaximumImageBytes`, `MaximumImageWidth`, `MaximumImageHeight`, and `MaximumImagePixels`. Declared MIME is compared with bytes; `EPUB039` reports a mismatch and the detected safe type wins. Assets with identical SHA-256 bytes reuse one `AssetId` (`EPUB045`) regardless of filename, so repeated figures and a matching cover do not duplicate canonical bytes.

SVG is treated only as a passive image asset, never as trusted markup. XML DTDs and entities remain prohibited. A deterministic sanitizer removes comments/processing instructions, foreign elements, `script`, `foreignObject`, `style`, embedded-object and animation elements, event handlers, style attributes, external/data references, and nonlocal CSS `url(...)` references. `EPUB062` lists the classes of removed content. The sanitized bytes—not the untrusted source bytes—become the `FlowAsset`, deduplication input, canonical asset bytes, and later HTML data URI. A document that cannot be parsed as an SVG root is rejected and may use a declared raster fallback or XHTML alt text. Flow does not interpret SVG graphical semantics, and sanitization can reduce visual fidelity.

Image diagnostics are `EPUB038` invalid bytes, `EPUB039` false/mismatched MIME, `EPUB040` excessive dimensions, `EPUB041` absent alternative text, `EPUB042` unsupported or animated format, `EPUB043` unsafe/invalid SVG content, `EPUB044` manifest fallback, `EPUB045` byte deduplication, `EPUB046` excessive image bytes, `EPUB062` sanitized SVG assets, `EPUB067` invalid embedded-SVG image reference, `EPUB068` XHTML image fallback, and `EPUB069` loss of SVG composition semantics. Repeated embedded-SVG findings are aggregated by code, message, and resource. Missing archive or manifest references continue to use `EPUB008`.

## SVG and MathML safety profile

MathML in the standard namespace maps to immutable `MathExpression` or `InlineMath` nodes. The importer retains a restricted Presentation MathML element vocabulary, safe nonlinking attributes, text nodes, source child order, `alttext`, and plain-text/TeX annotations as textual alternatives. It never evaluates the formula or invents an interpretation. Foreign/unknown wrappers are flattened only when safe children can remain; `script`, `annotation-xml`, event handlers, links, styles, and other excluded attributes/content are removed. Aggregated `EPUB063` diagnostics identify each class of semantic loss.

The import diagnostics form the unsupported-resource report for this profile: `EPUB062` means an SVG survived only after sanitization; `EPUB063` means a MathML construct was flattened or removed; `EPUB009`, `EPUB010`, and the existing image diagnostics continue to describe unsupported manifest resources, XHTML constructs, and image fallbacks. None of these findings is silently converted into trusted markup.

## Safe typed CSS subset

The importer reads internal manifest-declared `text/css` stylesheets, XHTML `style` elements, and `style` attributes. External or unsafe stylesheet URLs are never fetched. The parser implements only simple element, class, ID, and compound selectors without combinators or pseudo-selectors. Within that subset it applies inheritance, selector specificity, source order, and the final precedence of a style attribute.

Only `font-family`, `font-size`, `font-weight`, `font-style`, unitless/percentage `line-height`, `text-align`, `text-transform`, `text-decoration`, `letter-spacing`, paragraph block margins, and `text-indent` can survive. Values are parsed into `TypographyStyle`, `Length`, and enums. The selected first semantic font family is stored rather than a CSS fallback expression. No selector, declaration, stylesheet text, CSS variable, or URL enters `FlowDocument`.

Class and ID rules are represented accurately through `DocumentPresentation.NodeTypography`, an immutable map from stable `NodeId` to typed partial styles. This avoids incorrectly promoting one paragraph's class to the global `Body` role. The map is optional presentation, is serialized for development round trips, and remains excluded from `flow-c14n-0.1` and document hashes. Layout resolves each node through Flow defaults, author role/node presentation, user preferences, and renderer safety constraints in that order.

Paragraph `margin-top`, `margin-bottom`, `margin-block-start`, `margin-block-end`, and the vertical components of `margin` become semantic before/after spacing only on paragraphs. Horizontal physical margins, positioning, coordinates, columns, floats, generated content, page rules, media queries, custom properties, `@` rules, `!important`, CSS URLs, and JavaScript expressions are outside the subset. Responsive or physical behavior remains a renderer concern.

Inline Flow nodes currently have no stable independent presentation identity. A rule that directly styles `span`, `a`, `strong`, or another inline-only source target is therefore diagnosed rather than widened incorrectly to its whole paragraph. Inherited container typography still reaches representable descendant blocks.

CSS diagnostics are aggregated by resource and finding: `EPUB047` invalid stylesheet, `EPUB048` unsupported selector, `EPUB049` unsupported property/construct, `EPUB050` invalid typed value, `EPUB051` blocked URL or stylesheet, and `EPUB052` inline target without an independently representable Flow presentation node. Repeated inline declarations produce one diagnostic with an occurrence count instead of thousands of identical warnings. Stylesheets are bounded by `MaximumStylesheetBytes`.

## Footnotes and endnotes

The importer recognizes EPUB `noteref`, `footnote`, and `endnote` semantics together with the DPUB-ARIA `doc-noteref` and `doc-footnote` roles. Analysis runs across every selected XHTML spine resource after stable IDs have been allocated, so a call in one chapter can target a note in another chapter and forward references do not depend on ZIP or traversal order. Percent-encoded fragments and relative paths use the same safe normalization as ordinary internal links.

Each uniquely resolved call becomes a `FootnoteReference` targeting the note's stable `NodeId`. Its visible XHTML children are retained as an immutable inline label, including supported nested formatting. A note becomes an addressable `Footnote`; an `li` endnote remains within its source list through a `ListItem` containing that footnote. Multiple references may intentionally share the same target.

Explicit backlinks identified by `epub:type="backlink"`, `role="doc-backlink"`, or `rel="backlink"` are checked against source IDs of valid note references. A valid backlink is retained as an ordinary internal Flow `Link`/`DocumentAnchor`; Flow 0.1 does not yet have a distinct backlink inline node. Unmarked ordinary links from a note are still preserved and resolved normally but are not inferred to be editorial backlinks.

Malformed note relationships do not create invalid `FootnoteReference` nodes. Their visible labels remain in reading order and diagnostics retain the rejected destination: `EPUB053` orphan reference, `EPUB054` note without a valid reference, `EPUB055` missing explicit backlink, `EPUB056` cyclic note-to-note references, and `EPUB057` ambiguous destination. Cycles are retained because every individual target is valid, but the navigation risk is explicit. Duplicate source IDs continue to produce `EPUB034` as well as an ambiguity diagnostic when used as a note destination.

## Semantic tables

XHTML `table`, `caption`, `thead`, repeated `tbody`, `tfoot`, `tr`, `th`, and `td` map to the corresponding immutable Flow table nodes. Source row-group, row, and cell order is retained. Cells may contain supported block/inline content or remain intentionally empty. Positive `colspan` and `rowspan` values are preserved as integers without calculating boxes or rendered coordinates.

Header-cell `scope` accepts `row`, `col`, `rowgroup`, and `colgroup`. Space-separated `headers` tokens resolve through the stable EPUB source-ID map and are retained only when they target a `th` in the same table. This lets validation and standalone HTML preserve explicit accessible associations without guessing relationships from visual placement. Multiple `tbody` groups remain distinct and ordered.

Malformed structures use deterministic semantic recovery rather than flattening the whole table. Direct rows form implicit bodies; cells outside a row form a recovery row; unexpected visible content inside a table, row group, or row is wrapped in generated cells; multiple heads/feet merge their rows in source order; and additional captions become recovery-cell content after the primary caption. Empty cells are never dropped. Invalid spans fall back to one, and broken header associations are omitted so the produced `FlowDocument` remains valid while the original value survives in a diagnostic.

Table diagnostics are `EPUB058` invalid hierarchy/recovery, `EPUB059` invalid span, `EPUB060` invalid or misplaced scope, and `EPUB061` unresolved or foreign `headers` target. Column metadata (`colgroup`/`col`), HTML table-layout algorithms, inferred accessibility associations, calculated grids, widths, and heights are not imported.

## Source traceability and stable IDs

`EpubImportResult.SourceMap` exposes an immutable, typed `EpubSourceMap`. Each `EpubSourceLocation` records the normalized archive resource path, optional decoded XHTML fragment, semantic `NodeId`, and one-based occurrence. Callers can resolve an exact path/fragment, resolve a relative reference against its containing XHTML, or retrieve every source occurrence associated with a Flow node. Resource-only locations point to their imported `Chapter`.

Relative references use the same safe archive-path normalization as the importer. Empty paths target the current XHTML, dot segments are normalized without allowing traversal above the archive root, cross-chapter paths are supported, and percent-encoded fragments are decoded before ordinal lookup. Links without fragments resolve to the target chapter. Absolute `http`, `https`, and `mailto` links remain ordinary external Flow links and therefore do not resolve through the source map.

Discovery and spine order define deterministic precedence. When the same fragment occurs twice in one XHTML, both occurrences remain in `Locations`, with distinct occurrence values and stable Flow IDs where both elements are semantic nodes; forward link resolution selects the first. Repeated spine resources follow the same first-occurrence rule. If different EPUB IDs normalize to the same legal Flow ID, the importer assigns deterministic numeric suffixes rather than merging nodes.

Source ID findings use `EPUB034` for duplicates in one XHTML, `EPUB035` for values that are not XML NCNames, `EPUB036` for normalized Flow-ID collisions, and `EPUB037` when a source location could not produce a semantic Flow node. Invalid source IDs are retained when safe so percent-encoded links can still resolve; diagnostics make the source defect explicit.

The source map is provenance evidence owned by the adapter, not semantic book content. It is not attached to `FlowDocument`, serialized to `.flow.json`, canonicalized, laid out, or hashed. Importers that need it after persistence must store it separately. The semantic `NodeId` values it references do survive deterministic re-import, Flow JSON round-trip, layout, and HTML rendering.

## XHTML structure and mixed content

XHTML traversal follows `XNode` source order. A `section` becomes a Flow `Section`; `article` also becomes a `Section`, with a diagnostic because Flow does not yet distinguish the article role. `main`, `aside`, `header`, `footer`, `address`, `div`, `details`, and `summary` are transparent structural containers: their text runs and supported descendants remain in their original order without introducing a false container type.

Definition lists currently have no canonical Flow node. The `dl`, `dt`, and `dd` relationship is diagnosed, while each ordered term/definition text run and nested block is preserved. The importer creates a paragraph only for an actual run of inline content found at block level; it never flattens an arbitrary container through `element.Value`. Consequently, nested paragraphs, lists, sections, and mixed text are neither duplicated nor reordered.

Inline `abbr`, `cite`, `q`, `sub`, `sup`, `mark`, and `time` retain their visible text, nested inline formatting, and whitespace. Their specialized semantics and attributes such as `title` or `datetime` are not representable by the current Flow inline model, so the loss is explicit. Neutral `span` remains transparent. Inline text whitespace is preserved verbatim, `<pre>` remains exact code text, and indentation containing only whitespace between block elements is ignored as source formatting rather than book content.

Unsupported semantic wrappers and unknown elements use aggregated `EPUB010` diagnostics: one finding per resource, element name, and conversion behavior, including the occurrence count. Their children are recursively converted in order. Scripts and styles remain the deliberate exception: they are diagnosed and discarded rather than executed or copied into canonical content. Foreign-namespace elements are also traversed transparently and diagnosed; their local names are not treated as trusted XHTML semantics.

Language and direction declarations are inherited through transparent XHTML containers and attached to the nearest generated inline semantic range. Local `lang` or `xml:lang` changes remain nested ranges inside a paragraph; when both occur on the same source element, valid `xml:lang` takes precedence and a conflict produces `EPUB065`. Invalid tags use `EPUB026`. `dir=ltr`, `rtl`, and `auto` become typed directions; `bdi` creates isolation, while `bdo` creates an override that requires explicit LTR/RTL. Invalid or missing override directions use `EPUB064` and preserve the visible children without inventing direction.

Simple ruby retains base content, `rt`, and `rp` in source order. Orphan `rt`/`rp`, nested ruby, or ruby without both base and annotation is flattened with `EPUB066`, preserving supported text. The importer does not infer pronunciation, generate annotations, or treat ruby placement as canonical semantics.

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

The project fixture now uses fixed ZIP entry timestamps so its size and SHA-256 can be recorded in the deterministic [`flow-epub-corpus-0.1` catalog](epub-corpus.md). Corpus metadata is interoperability evidence only and never enters `FlowDocument` or canonical bytes.

The suite also generates EPUB 2/3, malicious, incomplete, Navigation Document, NCX, long-spine, repeated-spine, fallback, supplementary-content, nested XHTML, definition-list, mixed-content, multilingual/bidirectional/ruby content, whitespace, unknown-element, duplicate-ID, invalid-ID, normalized-ID-collision, cross-document footnotes/endnotes, multiple note references, backlinks, note cycles, regular/irregular/accessibility/malformed tables, JPEG/PNG/GIF/WebP/SVG, XHTML/SVG raster covers, safe/malicious MathML, false-MIME, repeated-image, cover, and broken-image variants. It verifies version/navigation detection, DTD and traversal rejection, exact OPF reading order independent of ZIP order, XHTML child order, non-duplication, whitespace fidelity, inline language inheritance/override, bidirectional modes, ruby recovery, aggregated diagnostics, TOC precedence, relative/dot-segment/percent-encoded destinations, note/table/math resolution and rendering, image signatures/dimensions/limits/deduplication, SVG sanitization and wrapper recovery, MathML active-content removal, missing and unsupported resources, circular/broken fallbacks, media overlays, deterministic reports/source maps, and archive limits. Integration tests prove that semantic IDs remain stable through repeated import, JSON round-trip, layout, and HTML rendering.

## Current boundary

The prototype does not yet support landmarks/page-list navigation semantics, CSS beyond the documented typed subset, non-XHTML spine representations beyond selecting an XHTML fallback, media-overlay playback, scripting, SVG graphical semantics, unrestricted/Content MathML, audio/video, encryption/DRM, font embedding, EPUB CFI, fixed-layout publications, or full accessibility metadata. AVIF, animated GIF, responsive CSS/media-query evaluation, image maps, SVG active/external content, `annotation-xml`, table column metadata, and automatic header inference are not imported. Backlinks survive as ordinary Flow links because there is no dedicated backlink node, and unmarked links are not guessed to be backlinks. Definition-list relationships and the specialized semantics of article, main, aside, header/footer, address, details/summary, abbreviation, citation, inline quotation, subscript/superscript, highlighting, and machine-readable time remain diagnostic preservation rather than canonical Flow nodes. Accessibility metadata is retained for inspection but is not yet validated as a complete accessibility claim or mapped into the canonical Flow model. Metadata links, vocabulary-prefix expansion, alternate-script refinements, collection metadata, and rendition properties are not fully interpreted. NCX labels are imported as text because NCX does not provide the XHTML inline-label model. TOC nesting deeper than six levels is diagnosed and clamped to the maximum representable Flow level.

The CLI exposes `flow epub-inspect <book.epub> [--json <report.json>]` and `flow import <book.epub> [--output <book.flow.json>] [--diagnostics-json <report.json>] [--fidelity-report <fidelity.json>]`. Inspection inventories publication structure without conversion. Import writes only after a complete, semantically valid result and reports diagnostics without silently accepting a partial result. When `--output` is omitted, the imported OPF title produces a portable lowercase file name with ASCII letters, digits, and underscores beside the source EPUB. Optional diagnostic and fidelity reports are deterministic UTF-8 JSON and remain available even when conversion fails. This closes the first command-line pipeline, but it does not yet constitute the complete workflow “open any `.epub`, edit it, and publish it again.”

The mapping is evaluated against the [W3C EPUB 3.3 specification](https://www.w3.org/TR/epub-33/). The next interoperability work should prioritize accessibility metadata and content fallback behavior instead of inventing equivalent Flow-only vocabularies.
