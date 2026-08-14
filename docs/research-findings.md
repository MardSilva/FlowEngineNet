# Research findings

This log records where existing standards already satisfy Flow requirements and where an experiment identifies a genuine gap. Findings will be added alongside implementation evidence.

| Requirement | Existing standard evaluated | Finding or limitation | Possible workaround | Flow-specific behavior justified? |
| --- | --- | --- | --- | --- |
| Semantic, responsive rendering | HTML and CSS | Evaluation pending renderer milestone. | Use semantic HTML with generated CSS. | Not yet established. |
| Publication structure and reading order | EPUB | Evaluation pending importer milestone. | Map EPUB package and XHTML semantics into the Flow model. | Not yet established. |
| Deterministic JSON bytes for hashing | RFC 8785 JSON Canonicalization Scheme (JCS) | JCS defines deterministic JSON encoding but does not decide which domain fields belong to a Flow document hash. `flow-c14n-0.1` also uses fixed schema order rather than JCS lexicographic property sorting. | Keep a versioned semantic projection now; evaluate JCS as the final byte-normalization layer for a future profile. | A Flow-specific field projection is justified; a Flow-specific JSON normalization algorithm may not be. |
