# Flow 0.1 conformance profile

The Flow 0.1 conformance suite is a small executable research profile. It does not claim W3C, EPUB, HTML, accessibility, cryptographic, archival, or legal conformance. Each numbered case protects one invariant from the original 0.1 definition of done.

| ID | Invariant | Executable evidence |
| --- | --- | --- |
| 001 | Document identity survives different layouts | Mobile and desktop `LayoutDocument` values retain the source `DocumentId` and version. |
| 002 | Duplicate node IDs are invalid | `DocumentValidator` emits `FLOW_DUPLICATE_NODE_ID`. |
| 003 | Stable anchors resolve | A hierarchical `DocumentAnchor` resolves to the intended node instance. |
| 004 | Font changes do not change the hash | Documents differing only in presentation produce the same `DocumentHash`. |
| 005 | Content changes change the hash | A semantic text change produces a different SHA-256 hash. |
| 006 | Heading typography is independent | Body and heading roles retain distinct typed styles. |
| 007 | TOC entries generate navigable anchors | HTML `nav` links target the stable heading ID. |
| 008 | Small screens use one column | A 390 × 844 context selects the small, one-column profile. |
| 009 | User font preference overrides author font | Resolution applies the reader font without mutating author presentation. |
| 010 | Figure and caption form a valid relationship | Caption ownership is valid only through a figure. |
| 011 | Footnote references resolve | Valid targets pass and missing footnotes produce `FLOW_UNRESOLVED_FOOTNOTE_REFERENCE`. |
| 012 | HTML output is semantic and escaped | The renderer emits semantic elements, stable IDs, and encoded untrusted text without CLR names. |

The numbered cases live in `tests/Flow.Conformance.Tests/SemanticConformanceTests.cs`. Additional conformance tests enforce the approved direct project dependency graph. Feature-specific suites provide deeper coverage and are not replacements for these twelve stable research vectors.

Run the profile with:

```powershell
dotnet test tests/Flow.Conformance.Tests/Flow.Conformance.Tests.csproj
```
