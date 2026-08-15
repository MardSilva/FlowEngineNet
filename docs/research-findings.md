# Research findings

This log records where existing standards already satisfy Flow requirements and where an experiment identifies a genuine gap. Findings will be added alongside implementation evidence.

| Requirement | Existing standard evaluated | Finding or limitation | Possible workaround | Flow-specific behavior justified? |
| --- | --- | --- | --- | --- |
| Semantic, responsive rendering | HTML5 and CSS | Native elements, embedded assets, media queries, and typed styles are sufficient for the current Flow model. The standards do not define Flow-to-HTML mapping or deterministic bytes. | Keep an explicit, tested adapter profile with safe escaping and fixed output order. | No new markup language is justified; only a versioned Flow adapter profile is needed. |
| Publication structure and reading order | EPUB | Evaluation pending importer milestone. | Map EPUB package and XHTML semantics into the Flow model. | Not yet established. |
| Deterministic JSON bytes for hashing | RFC 8785 JSON Canonicalization Scheme (JCS) | JCS defines deterministic JSON encoding but does not decide which domain fields belong to a Flow document hash. `flow-c14n-0.1` also uses fixed schema order rather than JCS lexicographic property sorting. | Keep a versioned semantic projection now; evaluate JCS as the final byte-normalization layer for a future profile. | A Flow-specific field projection is justified; a Flow-specific JSON normalization algorithm may not be. |
