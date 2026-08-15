# Standalone HTML renderer 0.1

`HtmlDocumentRenderer` is the first adapter that consumes a validated `FlowDocument`, its matching `LayoutDocument`, and the active `UserReadingPreferences`. It produces deterministic UTF-8 HTML5 bytes or writes those same bytes to a `.html` file.

The `LayoutDocument` is authoritative for the resolved typography, theme, margins, columns, and node intentions. Reader preferences are an explicit renderer input so the complete runtime state remains visible at the boundary, but the renderer does not recompute the cascade and therefore cannot discard renderer safety constraints already applied by the layout engine.

## Output profile

The 0.1 output is a standalone document containing:

- an HTML5 doctype, UTF-8 metadata, viewport metadata, title, and language when available;
- a restrictive content security policy with scripts and network-loaded resources disabled;
- all CSS in a single `style` element;
- images embedded as base64 data URIs;
- an `article` root and semantic sectioning, headings, paragraphs, figures, captions, quotations, navigation, lists, code, and footnotes;
- stable semantic `NodeId` values as HTML `id` attributes;
- table-of-contents and footnote links targeting those IDs;
- responsive image and small-viewport rules.

No CLR namespace, record name, or internal C# class name is emitted. Typed semantic role names such as `heading-2` may appear in `data-typography` attributes because they are renderer-facing semantics, not implementation type names.

This is not a WCAG conformance claim. The output has semantic and escaping tests, but Flow 0.1 has not completed a browser, screen-reader, right-to-left, vertical-writing, or accessibility audit.

## Safety rules

Text and attribute values are HTML-encoded. Font-family values are encoded as CSS code points before entering the style element. Only Flow anchors, local stable-ID fragments, and absolute `http`, `https`, or `mailto` links become anchors. An unsupported or unsafe target keeps its visible inline content but is emitted without a link.

The renderer validates that:

- the source `FlowDocument` is semantically valid;
- document identity and version match the layout;
- `ReadingMode.Flow` is used;
- every semantic node occurs exactly once in the layout tree;
- every layout node references the corresponding node instance from the source document.

## Determinism

The writer uses explicit ordering, invariant numeric formatting, `\n` line endings, fixed CSS declaration order, and UTF-8 without a byte-order mark. The same document, layout, preferences, and renderer version therefore produce the same bytes. Renderer output is runtime presentation and does not participate in `flow-c14n-0.1` or `DocumentHash`.
