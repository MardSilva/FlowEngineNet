# EPUB fidelity report

English | [Português (Brasil)](pt-BR/epub-fidelity.md)

`Flow.Epub` can produce an experimental, typed `EpubFidelityReport` after import. The report reconciles bounded source measurements, the destination `FlowDocument`, `EpubSourceMap`, package-processing decisions, and import diagnostics. It complements diagnostics; it does not replace them and is not an EPUB conformance, accessibility, rendering, or visual-equivalence claim.

The report is deliberately external to `FlowDocument`. It is absent from `.flow.json`, every Flow canonical profile, document identity, integrity hashes, presentation, layout, and rendering. Calling `IEpubFidelityAnalyzer.Analyze` is pure with respect to the imported document.

## Status and impact

Every measured source unit receives exactly one status:

- `Preserved`: the corresponding semantic unit remains directly representable;
- `Transformed`: meaning remains represented through a deliberate format change, such as XHTML links becoming Flow anchors;
- `Approximated`: visible or useful information remains, but the source construct has no direct Flow equivalent;
- `Unsupported`: the current profile recognizes the source representation but cannot import it;
- `Lost`: no destination representation was produced.

Findings independently record impact as `Informational`, `Minor`, `Moderate`, `Major`, or `Critical`. A finding contains a stable code, status, impact, explanation, aggregated count, related diagnostic code, and—when evidence exists—resource path, decoded fragment, `NodeId`, and `AssetId`. Diagnostic-derived findings may overlap a measurement finding: their purpose is traceability, so finding counts must not be summed as if they were mutually exclusive source units.

## Measurements and formula

The analyzer measures linear and non-linear spine positions, significant characters, headings, paragraphs, internal/external links, images/covers, notes/references, tables/rows/cells, TOC entries, manifest resources, and unknown or currently unrepresentable XHTML elements. TOC links and note calls belong only to their dedicated metrics rather than being double-counted as ordinary internal links. Source resource paths are retained in noncanonical import evidence only long enough to create localized findings.

Neutral XHTML `div` and `span` containers belong to the last metric because Flow does not retain their source wrapper. They are classified as `Transformed`, not `Approximated`: their child content, typed language/direction where applicable, and reading order survive while the neutral wrapper is flattened. Aggregated `EPUB075` findings make that transformation visible. Semantic wrappers such as `aside` and `details`, specialized inline elements, and unknown elements remain `Approximated` through `EPUB010` when Flow cannot preserve their distinct meaning.

“Significant characters” means Unicode scalar values for which `Rune.IsWhiteSpace` is false. It is not a byte count, UTF-16 code-unit count, word count, or typography measurement. Destination counts are semantic Flow occurrences; asset byte deduplication therefore permits several image occurrences to point to one `FlowAsset` without being reported as loss.

For each metric, status counts partition the source count:

```text
source = preserved + transformed + approximated + unsupported + lost
preservation percentage = (preserved + transformed) / source * 100
```

The percentage is truncated to six decimal places. When `source` is zero, it is JSON `null` (`N/A`), never an invented `100%`. `Approximated`, `Unsupported`, and `Lost` deliberately receive no preservation credit. The report-level percentage applies the same formula to all measured source units. Because heterogeneous unit types are added, this number is an informative regression signal—not a scientific quality score or a comparison ranking between books.

Manifest destination counts describe resources accepted by the bounded import pipeline, not a promise that every resource became a canonical asset. Spine counts preserve declared occurrences, including repeated and non-linear entries. EPUB 3 TOC entries take precedence over NCX when both exist, matching the importer.

Each safe image occurrence that becomes a `Figure` counts as a transformed image, even when its bytes are shared with another occurrence through asset deduplication. An image originally placed inside a paragraph is segmented into a figure between ordered text blocks. The image is therefore represented, while the exact inline-versus-block distinction remains an explicit `EPUB010` approximation.

A paragraph whose semantic output is one or more figures is counted as transformed rather than lost. Its image and any visible text remain measured by their own destination units. This avoids treating an image-only XHTML wrapper as missing paragraph content while keeping empty paragraphs and failed image imports visible through the ordinary source/destination reconciliation.

Image bytes and accessible text are separate evidence. `EPUB079` is a minor approximation when an explicit XHTML source other than `alt` supplies the Flow alternative text. `EPUB041` is moderate: the image remains present, but the publication supplied no explicit textual alternative that Flow can preserve. The report does not run OCR or infer authorial intent.

Valid XHTML `colgroup`/`col` metadata is reported as the minor table approximation `EPUB080`, not as malformed table recovery. It does not add a source content unit or a destination cell: the current model preserves the table's semantic rows and cells but has no column-definition node. Malformed column groups with visible recoverable content continue to use `EPUB058`.

## CLI

Use the report alongside, or independently from, the diagnostic JSON:

```powershell
dotnet run --project src/Flow.Cli -- import book.epub `
  --output book.flow.json `
  --diagnostics-json diagnostics.json `
  --fidelity-report fidelity.json
```

`fidelity.json` uses format identifier `flow-epub-fidelity-0.1`, deterministic property/array ordering, UTF-8 without BOM, LF line endings, invariant numbers, and atomic replacement. A failed import still writes a partial report when the requested report path is valid. A partial report keeps unknown denominators as `null` rather than claiming completeness. The source EPUB, Flow document, diagnostics report, and fidelity report must all use distinct paths.

The container-classification correction does not change this JSON schema. It changes only the status assigned to a measured source unit that was already present in the report. Fidelity evidence is noncanonical and does not affect `.flow.json`, canonical bytes, document hashes, layout, or rendering.

## Current limits

Measurements are structural and textual. They do not compare pixels, fonts, line breaking, pagination, audio timing, reading-system behavior, accessibility quality, or authorial intent. Elements without stable source fragments can be localized only to their resource. One source construct can also generate both a measurement finding and a related diagnostic finding. These limits are explicit so the report can improve without silently changing the canonical Flow profile.
