# Experimental EPUB corpus catalog

The `flow-epub-corpus-0.1` profile describes publications used for `Flow.Epub` interoperability tests. The catalog records evidence about an input; it is not part of the converted book. Catalog fields, diagnostics, and results stay outside `FlowDocument`, `.flow.json`, `flow-c14n-0.1` canonicalization, hashes, and signatures.

This first increment defines and validates the manifest contract. Local-file discovery, corpus execution, and optional EPUBCheck evidence belong to later increments.

## JSON contract

The root object contains `format` and `publications`. The current format is `flow-epub-corpus-0.1`. Each publication records:

- `id`: a file-name-independent identifier of 1 to 64 characters. It accepts lowercase ASCII letters, digits, dots, hyphens, and underscores, and must begin with a letter or digit;
- `title` and `origin`: the catalog title and documented source;
- `license`: the applicable license or condition and the evidence used to record it;
- `kind`: `projectFixture`, `redistributablePublication`, or `localNonRedistributablePublication`;
- `redistribution`: `allowed` or `prohibited`, consistently with `kind`;
- `relativePath`: a logical relative path using `/`, without an absolute path, URI scheme, backslash, or literal/percent-encoded `.` and `..` segments;
- `expectedEpubVersion`: `epub2` or `epub3`;
- `expectedSizeBytes` and `sha256`: the positive expected byte length and SHA-256 of the input bytes;
- `languages`: language tags in declared order because the first value can have editorial meaning;
- `expectedResources`, `expectedFeatures`, `expectedResults`, and `knownLimitations`: expected resource classes, publication characteristics, outcomes, and known restrictions, all written in ordinal order.

The reader rejects unknown or duplicate properties, missing required fields, repeated IDs, invalid hashes, unknown versions, unsafe paths, and inconsistent license classifications. Stable diagnostics use codes `EPC001` through `EPC012` and are ordered by location, code, and message.

The serializer writes properties in a fixed order, publications by `id`, and set-like values in ordinal order. SHA-256 values are normalized to uppercase. Titles, origins, license names, evidence, and limitation text are not rewritten. JSON uses UTF-8 without a BOM, ends with LF, and is limited to 1 MiB.

## Licensing and files

A catalog record does not grant rights over a publication. Origin and license evidence document the maintainer's decision; third-party files can be versioned only when redistribution is authorized. Legally obtained books without redistribution permission must use `localNonRedistributablePublication` and stay outside the repository.

The minimal manifest at `tests/Flow.Epub.Tests/Corpus/epub-corpus.json` describes only the EPUB produced by `MinimalEpubFactory`. No binary is stored. The test generates the same bytes with fixed ZIP timestamps and checks their length and SHA-256. This keeps the fixture small, reviewable, and reproducible on Windows and Linux under the SDK pinned by the project.

## Current boundary

The catalog does not yet discover files, read `FLOW_EPUB_CORPUS_PATH`, run batch imports, produce corpus reports, or invoke EPUBCheck. Those responsibilities begin with Prompt 18.2.
