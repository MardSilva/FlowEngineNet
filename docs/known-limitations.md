# Known limitations of Flow 0.1

Flow 0.1 is a research-grade reference implementation. This list is part of the release boundary, not a backlog promise.

## Compatibility and distribution

- All public APIs, `.flow.json` fields, canonicalization rules, renderer output, and diagnostics are experimental and can change during `0.x`.
- `.flow.json` is a development interchange representation, not a registered media type, standard, archival format, or final Flow container.
- No NuGet package, executable package, release artifact, schema registry, compatibility guarantee, or migration tool is published.
- The repository currently pins .NET SDK 10.0.400. Consumers must build from source.

## Semantic model

- The model has restricted Presentation MathML, inline BCP 47-shaped language ranges, bidirectional ranges, and simple ruby. It still has no Content MathML/OpenMath interpretation, equation evaluation, arbitrary extension vocabulary, SVG-semantic, audio, video, citation, bibliography, annotation, change-tracking, form, or scripting nodes.
- Language tags receive structural validation and deterministic casing but are not checked against the complete IANA language-subtag registry. Complex ruby grouping, multiple annotation tracks, ruby placement, language-aware typography, script-specific line breaking, vertical writing, and Unicode Bidirectional Algorithm conformance testing remain outside the model/runtime.
- Tables preserve explicit semantic groups, cells, positive spans, scope, and header references, but do not model column definitions, calculated grids, inferred headers, widths, heights, or layout coordinates. XHTML `rowspan="0"` is diagnosed and conservatively becomes one rather than being expanded from a calculated row-group grid.
- Table-of-contents entries are explicit. Automatic TOC derivation from headings is not implemented.
- Stable IDs are syntactically enforced, but the engine cannot prove that a producer did not derive an ID from a page number or visual position.
- Validation covers the current model, not every publishing or accessibility rule.

## Serialization, integrity, and signatures

- `flow-json-0.1` and `flow-c14n-0.1` have only one implementation and no independent interoperability implementation.
- Canonicalization uses a fixed Flow projection and property order; it is not RFC 8785 JCS and performs no Unicode normalization.
- Complete asset bytes are held in memory and participate in canonicalization, which is unsuitable for very large publications.
- SHA-256 proves byte-level integrity for the profile, not authorship, provenance, legality, archival suitability, or trust.
- RSA-PSS-SHA256 signatures are detached and local. There is no signature serialization, certificate profile, PKI, key discovery, revocation, timestamp, trust store, or identity assertion.

## Layout and rendering

- Only continuous `ReadingMode.Flow` is implemented. `Paged` and `Print` throw `NotSupportedException`.
- `LayoutDocument` carries constraints and intentions; it does not measure glyphs, boxes, line breaks, coordinates, pages, widows/orphans, floats, or print output.
- HTML is the only renderer. There is no Reader application, native UI renderer, PDF renderer, production pagination, or print pipeline.
- The standalone HTML renderer has deterministic tests and semantic markup tests, but no full browser matrix, WCAG 2.2 conformance audit, screen-reader audit, localization audit, or right-to-left/vertical-writing validation.
- The multi-file HTML book is a logical `file://` package, not EPUB export or a standardized Web Publication. It has CSS-only light/dark/sepia and font/scale controls, responsive chapters, landmarks, keyboard-visible focus, nested TOC lists, chapter-local notes, logical chapter progress, explicit `pt-PT`/`pt-BR`/`en` generated UI with deterministic automatic fallback, and basic print rules, but no runtime language switch, search, persistent cross-file progress/preferences, service worker, JavaScript enhancement, full browser/file-URL matrix, screen-reader audit, or accessibility certification. Authored text is never translated. Editorial roles are conservative renderer inferences; Flow does not yet canonically distinguish footnotes from endnotes or record complete provenance for synthesized labels. Browsers without CSS `:has()` retain the complete resolved default reading experience but may ignore the optional appearance switches. Its manifest excludes its own recursively undefined hash while hashing every payload file.
- Font names are suggestions. Fonts are not embedded, so appearance depends on fonts available to the browser.

## EPUB interoperability

- The EPUB importer is a bounded subset prototype, not an EPUB 3.3 conformance checker or general reading system.
- EPUB inspection recognizes package-level EPUB 2/3 structure; import maps EPUB 3 TOC navigation and EPUB 2 NCX fallback to Flow anchors, but neither operation validates the full EPUB specification.
- OPF metadata import maps only title, subtitle, primary language, authors, and description into canonical Flow metadata. Other parsed source properties live only in the noncanonical in-memory `EpubMetadataReport` and are not persisted by `.flow.json` or the CLI.
- Accessibility metadata is retained, not certification-validated or enforced. OPF metadata links, complete vocabulary-prefix expansion, alternate scripts, collections, and rendition metadata remain incomplete.
- TOC hierarchy is limited to six levels. EPUB landmarks and page-list semantics are not represented, and unresolvable/circular TOC destinations remain diagnostics rather than invalid Flow entries.
- Fallback chains can select an existing XHTML representation. Non-XHTML fallback content is not converted, and media overlays are reported but never played.
- Mixed XHTML content preserves text and supported children in source order, including `lang`/`xml:lang`, `dir`, `bdi`, `bdo`, and simple `ruby`/`rt`/`rp`. Article/aside/header/footer/details/definition-list roles and `abbr`/`cite`/`q`/`sub`/`sup`/`mark`/`time` semantics still do not have canonical Flow nodes. Their visible content survives with aggregated diagnostics.
- Repeated spine resources are retained with unique occurrence IDs. An ordinary EPUB path/fragment cannot identify which repeated occurrence it intended, so links resolve deterministically to the first imported occurrence.
- EPUB footnotes/endnotes and explicit backlinks are resolved across imported XHTML resources, but Flow has no distinct backlink node; valid backlinks are ordinary internal links, and unmarked links are not inferred to be backlinks.
- EPUB-to-Flow source locations are available only through the in-memory `EpubSourceMap`. The map is intentionally absent from `.flow.json`, canonical bytes, hashes, and current CLI output, so applications that need durable provenance must persist it separately.
- The optional EPUB fidelity report measures a bounded structural/textual profile. Its aggregate percentage combines unlike source-unit kinds and is only a regression signal; it is not a visual-quality score, EPUB/accessibility conformance result, or publisher comparison. Source constructs without stable fragments may be localized only to a resource, and diagnostic-derived findings can overlap measurement findings.
- JPEG, PNG, static GIF, WebP, and sanitized passive SVG can become assets. SVG sanitization removes active elements, event handlers, styling that may contain active references, and external resource references; it is not a complete SVG graphical interpretation and may change appearance. A restricted typed CSS subset supports simple selectors and typography, but not combinators, pseudo-selectors, inline-fragment styling, media queries, `@` rules, variables, generated content, positioning, floats, columns, physical layout, or remote resources. Flow preserves only its allow-listed Presentation MathML vocabulary and diagnoses flattened or removed constructs; it does not support Content MathML, `annotation-xml`, image maps, AVIF, animated GIF, fixed layout, table column metadata/layout algorithms, audio/video, encryption/DRM, embedded fonts, EPUB CFI, scripts, or complete accessibility metadata.
- `DocumentPresentation.Cover` preserves the figure role in readable `.flow.json` when a spine figure references the OPF-declared cover asset. It is deliberately noncanonical. A declared cover asset that never occurs in imported reading content remains available only as an asset plus the in-memory metadata-report association; the importer does not invent a chapter or figure for it.
- Unsupported recoverable text is flattened with a diagnostic. Some source semantics cannot be represented by the current Flow node set.
- The test EPUB is generated by this project. No broad corpus of publisher EPUBs, EPUBCheck fixtures, malformed real-world books, or accessibility publications has been validated yet.
- The CLI imports only the documented EPUB subset. Flow cannot export or round-trip EPUB, and a successful import is not evidence of complete EPUB conformance.

## CLI and operations

- The CLI inspects/imports the documented EPUB subset and exposes sample, inspect, validate, hash, standalone HTML, and multi-file HTML-book commands.
- It has no signing, verification, EPUB export, preference/theme flags, streaming pipeline, batch mode, or interactive prompts. Typed metadata and package-processing reports are currently API-only; the fidelity report is an optional noncanonical sidecar.
- Output paths are replaced when commands succeed; callers should use copies for experiments.
- CI validates Windows and Linux only. macOS and alternative .NET SDK/runtime implementations are not in the matrix.

## Explicitly out of scope

Flow 0.1 does not implement a Reader, editor, collaboration, cloud service, DRM system, PDF export, production pagination, marketplace, account system, or legally binding signature workflow.
