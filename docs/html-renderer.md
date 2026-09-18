# Standalone HTML renderer 0.1

English | [Português (Brasil)](pt-BR/html-renderer.md)

`HtmlDocumentRenderer` is the first adapter that consumes a validated `FlowDocument`, its matching `LayoutDocument`, and the active `UserReadingPreferences`. It produces deterministic UTF-8 HTML5 bytes or writes those same bytes to a `.html` file.

`HtmlBookPackageRenderer` is a second, additive output in the same adapter. It preserves the standalone renderer and splits a book into `index.html`, a logical TOC page, one file per chapter, shared assets/CSS, and a deterministic integrity manifest. Its profile and content-placement rules are documented in [HTML book package](html-book-package.md).

The `LayoutDocument` is authoritative for the resolved typography, theme, margins, columns, and node intentions. Reader preferences are an explicit renderer input so the complete runtime state remains visible at the boundary, but the renderer does not recompute the cascade and therefore cannot discard renderer safety constraints already applied by the layout engine.

## Output profile

The 0.1 output is a standalone document containing:

- an HTML5 doctype, UTF-8 metadata, viewport metadata, title, and language when available;
- a restrictive content security policy with scripts and network-loaded resources disabled;
- all CSS in a single `style` element;
- images embedded as base64 data URIs;
- an `article` root and semantic sectioning, headings, paragraphs, figures, captions, quotations, navigation, lists, code, footnotes, and tables;
- native, escaped MathML markup reconstructed only from the restricted typed Flow math tree;
- inline language spans, bidirectional embedding/isolation/override, and ruby annotations reconstructed as semantic HTML;
- stable semantic `NodeId` values as HTML `id` attributes;
- table-of-contents and footnote links targeting those IDs, with preserved formatted footnote-reference labels when supplied;
- responsive image and small-viewport rules.

When `DocumentPresentation.Cover` identifies a valid figure, the same safe responsive figure output receives `data-publication-role="cover"`. The marker contains no source SVG, coordinates, dimensions, EPUB path, or CLR type name. It lets the standalone renderer and future multi-file book package distinguish the publication cover without changing canonical content or its hash.

Semantic tables render as `table`, `caption`, `thead`, repeated `tbody`, `tfoot`, `tr`, `th`, and `td`. Stable IDs remain on every addressable table part. Non-default spans become `colspan`/`rowspan`; header scope uses the HTML tokens `row`, `col`, `rowgroup`, or `colgroup`; and `headers` contains the escaped stable IDs of validated header cells. Empty cells remain empty elements. Renderer CSS supplies only safe responsive defaults and does not feed dimensions or a calculated grid back into `FlowDocument`.

No CLR namespace, record name, or internal C# class name is emitted. Typed semantic role names such as `heading-2` may appear in `data-typography` attributes because they are renderer-facing semantics, not implementation type names.

This is not a WCAG conformance claim. The output has semantic and escaping tests, but Flow 0.1 has not completed a browser, screen-reader, right-to-left, vertical-writing, or accessibility audit.

## Safety rules

Text and attribute values are HTML-encoded. Font-family values are encoded as CSS code points before entering the style element. Only Flow anchors, local stable-ID fragments, and absolute `http`, `https`, or `mailto` links become anchors. An unsupported or unsafe target keeps its visible inline content but is emitted without a link.

MathML output is never copied from EPUB XML. The renderer emits only allow-listed element and attribute names from `MathElement`, escapes all text and values, adds the MathML namespace at each expression root, and uses the available textual alternative as an accessible label. It does not execute or reinterpret the expression.

Internationalization markup is also reconstructed from typed values rather than copied attributes. `LanguageSpan` becomes an escaped `span lang`; embedding becomes `span dir`, isolation becomes `bdi dir`, override becomes `bdo dir`, and ruby nodes become `ruby`, `rt`, and `rp`. Direction controls reading semantics only; text alignment remains in the separate presentation cascade.

The renderer validates that:

- the source `FlowDocument` is semantically valid;
- document identity and version match the layout;
- `ReadingMode.Flow` is used;
- every semantic node occurs exactly once in the layout tree;
- every layout node references the corresponding node instance from the source document.

## Determinism

The writer uses explicit ordering, invariant numeric formatting, `\n` line endings, fixed CSS declaration order, and UTF-8 without a byte-order mark. The same document, layout, preferences, and renderer version therefore produce the same bytes. Renderer output is runtime presentation and does not participate in `flow-c14n-0.2` or `DocumentHash`.

Optional per-node author typography is resolved by the layout cascade before rendering. The HTML adapter reconstructs safe inline declarations from `ResolvedTypographyStyle`; it never receives or copies EPUB CSS source text, selectors, URLs, or arbitrary declarations. Reader preferences and renderer safety constraints therefore retain precedence over imported author typography.
