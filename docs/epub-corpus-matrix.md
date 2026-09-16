# Public EPUB corpus coverage

This matrix lists the cases exercised by the small public corpus in the normal test suite. All three EPUB files are generated from project-owned source during the test run. The repository stores the manifest and reviewed baseline, not EPUB binaries.

| Publication ID | EPUB profile | Navigation | Reading structure | Semantic and safety coverage |
| --- | --- | --- | --- | --- |
| `flow-epub2-ncx` | EPUB 2 | NCX | two chapters in spine order | cross-document link, TOC anchors, Flow JSON round-trip, layout, and HTML book |
| `flow-minimal-epub3` | EPUB 3 | none by design | two linear chapters | lists, internal link, PNG figure, stable assets and IDs |
| `flow-epub3-varied` | EPUB 3 | Navigation Document | non-linear cover followed by two linear XHTML resources | Portuguese accents, percent-encoded path, XHTML/SVG raster cover, typed CSS, cross-XHTML note, table, passive SVG, restricted MathML, malicious SVG/MathML sanitization, bidi, mixed languages, ruby, and unsupported-resource diagnostics |

The matrix describes tested fixtures, not universal EPUB support. In particular, it does not represent the variety of publisher files, assistive technologies, fixed-layout books, media overlays, encrypted publications, or very large books.

## Stable baseline

`tests/Flow.Epub.Tests/Corpus/epub-corpus-baseline.json` records evidence intended to catch regressions in reading order, semantic content, anchors, assets, notes, tables, validation, and canonical determinism. It includes ordered semantic IDs, focused counts, the canonical hash, and diagnostic codes required by intentionally lossy or unsafe fixtures.

The baseline deliberately excludes elapsed time, memory samples, output byte counts, diagnostic wording, diagnostic order, EPUBCheck process output, and incidental HTML details. New informational diagnostics do not fail comparison; a required diagnostic that disappears must be reviewed because it may indicate either a fix or a lost safety check.

The CI workflow runs this public corpus as part of the standard test suite on Windows and Linux. External books and EPUBCheck remain local and optional.
