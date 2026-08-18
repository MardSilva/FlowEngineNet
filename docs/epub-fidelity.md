# EPUB fidelity report

`Flow.Epub` can produce an experimental, typed `EpubFidelityReport` after import. The report reconciles bounded source measurements, the destination `FlowDocument`, `EpubSourceMap`, package-processing decisions, and import diagnostics. It complements diagnostics; it does not replace them and is not an EPUB conformance, accessibility, rendering, or visual-equivalence claim.

The report is deliberately external to `FlowDocument`. It is absent from `.flow.json`, `flow-c14n-0.1`, document identity, integrity hashes, presentation, layout, and rendering. Calling `IEpubFidelityAnalyzer.Analyze` is pure with respect to the imported document.

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

“Significant characters” means Unicode scalar values for which `Rune.IsWhiteSpace` is false. It is not a byte count, UTF-16 code-unit count, word count, or typography measurement. Destination counts are semantic Flow occurrences; asset byte deduplication therefore permits several image occurrences to point to one `FlowAsset` without being reported as loss.

For each metric, status counts partition the source count:

```text
source = preserved + transformed + approximated + unsupported + lost
preservation percentage = (preserved + transformed) / source * 100
```

The percentage is truncated to six decimal places. When `source` is zero, it is JSON `null` (`N/A`), never an invented `100%`. `Approximated`, `Unsupported`, and `Lost` deliberately receive no preservation credit. The report-level percentage applies the same formula to all measured source units. Because heterogeneous unit types are added, this number is an informative regression signal—not a scientific quality score or a comparison ranking between books.

Manifest destination counts describe resources accepted by the bounded import pipeline, not a promise that every resource became a canonical asset. Spine counts preserve declared occurrences, including repeated and non-linear entries. EPUB 3 TOC entries take precedence over NCX when both exist, matching the importer.

## CLI

Use the report alongside, or independently from, the diagnostic JSON:

```powershell
dotnet run --project src/Flow.Cli -- import book.epub `
  --output book.flow.json `
  --diagnostics-json diagnostics.json `
  --fidelity-report fidelity.json
```

`fidelity.json` uses format identifier `flow-epub-fidelity-0.1`, deterministic property/array ordering, UTF-8 without BOM, LF line endings, invariant numbers, and atomic replacement. A failed import still writes a partial report when the requested report path is valid. A partial report keeps unknown denominators as `null` rather than claiming completeness. The source EPUB, Flow document, diagnostics report, and fidelity report must all use distinct paths.

## Current limits

Measurements are structural and textual. They do not compare pixels, fonts, line breaking, pagination, audio timing, reading-system behavior, accessibility quality, or authorial intent. Elements without stable source fragments can be localized only to their resource. One source construct can also generate both a measurement finding and a related diagnostic finding. These limits are explicit so the report can improve without silently changing the canonical Flow profile.
