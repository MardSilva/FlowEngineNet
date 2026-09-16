# Experimental EPUB corpus catalog

The `flow-epub-corpus-0.1` profile describes publications used for `Flow.Epub` interoperability tests. The catalog records evidence about an input; it is not part of the converted book. Catalog fields, diagnostics, and results stay outside `FlowDocument`, `.flow.json`, `flow-c14n-0.1` canonicalization, hashes, and signatures.

The catalog contract, local discovery API, end-to-end executor, reviewed baselines, repeated-run qualification, and optional EPUBCheck adapter are implemented.

## What "corpus" means here

The corpus is a list of EPUB files used to test the importer. It is not a new book format and it is not part of the Reader. Each catalog entry says which local EPUB to use, how to verify its bytes, what license rules apply, and which results Flow should produce.

The executor opens each available book and repeats the same checks. That gives the project a reproducible answer to a practical question: "Does this version of Flow still import these books without breaking content, IDs, links, hashes, or HTML output?" Private books stay on the developer's machine. The report stores the catalog ID and test evidence, not the private file.

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

The public manifest at `tests/Flow.Epub.Tests/Corpus/epub-corpus.json` describes three EPUBs produced by project fixture factories. No EPUB binary is stored. Tests generate the same bytes with fixed ZIP timestamps and verify their length and SHA-256. The set covers EPUB 2/NCX, EPUB 3 navigation, and a varied EPUB 3 fixture without bringing third-party book rights into the repository. The exact scope appears in the [public coverage matrix](epub-corpus-matrix.md).

## Offline discovery

`IEpubCorpusDiscoveryService` resolves a validated manifest without importing EPUB content. `EpubCorpusDiscoveryOptions.FromEnvironment(repositoryRoot)` reads the optional `FLOW_EPUB_CORPUS_PATH` setting. No URL in `origin` or license evidence is opened, and the implementation contains no download path.

Project fixtures resolve only through their safe repository-relative path. Redistributable publications may use that path or the external corpus. A local non-redistributable publication is considered only in the external corpus and is rejected if that directory is inside the repository.

External matching first checks `<publication-id>.epub`, then scans safe `.epub` files for the cataloged size and SHA-256. The scan is ordinal, bounded to 4,096 entries and 16 directory levels by default, and never follows symbolic links or reparse points. A correct hash can therefore identify a renamed file without relying on its publisher-supplied name.

Each result has one typed status:

- `Available`;
- `Missing`;
- `HashMismatch`;
- `LicenseRejected`;
- `UnsafePath`;
- `SearchLimitExceeded`.

Missing private inputs are warnings rather than test failures. License rejection, unsafe paths, and mismatched bytes remain explicit errors. License compatibility is a reviewed catalog assertion; Flow verifies that `kind` and `redistribution` agree but does not infer legal permission from a license name.

An available publication is copied to an isolated system-temporary directory and verified again after copying. `EpubCorpusDiscoverySession.OpenRead(id)` exposes only the verified copy. Disposing the session removes the workspace. Cancellation or an exception before the session is returned also removes it.

`EpubCorpusDiscoveryReportJsonSerializer` writes `flow-epub-corpus-discovery-0.1` evidence with fixed property and publication order, UTF-8 without BOM, and LF. Reports contain stable catalog IDs, typed status/source/match values, and sanitized diagnostics. Physical source paths, temporary paths, user names, timestamps, and machine data are absent.

## Optional corpus execution

`Flow.Epub.Corpus` is a separate orchestration project. This keeps `Flow.Epub` independent of layout, integrity, and rendering while allowing `EpubCorpusExecutor` to run one verified temporary copy through structural inspection, import, document validation, fidelity analysis, Flow JSON round-trip, canonical hash comparison, mobile and desktop layout, and two deterministic HTML book packages.

Every publication ends as `Passed`, `Failed`, `Skipped`, or `Inconclusive`. A missing local book is skipped. A corrupt archive, discovery hash mismatch, invalid Flow document, unstable round-trip hash, broken generated package reference, or unmet declared expectation fails only that publication. An expectation token the current executor does not understand is reported as inconclusive instead of being treated as success. Processing then continues with the other catalog entries.

The executor currently recognizes these expectation tokens:

- resources: `xhtml`, `css`, `ncx`, `jpeg`/`jpg`, `png`, `gif`, `webp`, and `svg`;
- features: `spine`, `table-of-contents`/`toc`, `figure`/`image`, `internal-link`, `ordered-list`, `unordered-list`, `footnote`/`note`, `table`, and `cover`;
- results: `inspection-success`, `import-success`, `valid-flow-document`, `roundtrip-stable`, `canonical-hash-stable`, `mobile-layout`, `desktop-layout`, and `html-book-package`.

`flow-epub-corpus-execution-0.1` records completed phases, stable diagnostics, structural counts, document identity, canonical hash, output sizes, and a corpus summary. Its default JSON projection excludes elapsed time and managed-memory samples, uses UTF-8 without BOM and LF, contains no physical paths, and can be written atomically with `EpubCorpusExecutionReportJsonSerializer.WriteAtomicallyAsync`. Environment-dependent observations can be included under the explicitly non-deterministic `nonDeterministicEnvironment` section and must not be used for byte-for-byte baseline comparison.

## Qualification and reviewed baselines

`EpubCorpusQualificationService` executes the same verified inputs twice. It compares stable evidence from both runs and reports whether import, IDs, semantic order, validation, and the canonical hash are deterministic. It also places catalog expectation failures, fidelity loss, and optional EPUBCheck status side by side without treating them as interchangeable results.

`flow-epub-corpus-baseline-0.1` keeps only evidence suited to review: status, EPUB version, manifest/spine counts, imported nodes and assets, validation and fidelity-loss counts, document identity, canonical hash, ordered node IDs, semantic feature counts, source-map count, and required diagnostic codes. Runtime measurements, rendered byte sizes, diagnostic prose, and accidental diagnostic order are excluded. Extra diagnostics are allowed; removal of a required code needs review.

The checked-in public baseline is `tests/Flow.Epub.Tests/Corpus/epub-corpus-baseline.json`. CI regenerates all fixtures, runs the corpus twice, and compares the observed evidence with that file on Windows and Linux. A baseline change should therefore be reviewed as a semantic change, not refreshed automatically to make a test pass.

For local or non-redistributable entries, `EpubCorpusQualificationService.WriteLocalDetailedReportAsync` writes `flow-epub-corpus-qualification-0.1` atomically. It records repeated-run determinism, baseline differences, fidelity loss, expectation failures, optional EPUBCheck evidence, and a first/middle/last sample of the ordered chapter IDs. Environment observations stay in a clearly marked non-deterministic section. The report contains catalog IDs and sanitized EPUB-internal resources, never the book bytes or physical source path.

The normal test suite uses generated fixtures only. The optional xUnit test is marked with category `EpubCorpus`; it does nothing unless `FLOW_EPUB_CORPUS_PATH` is set and that directory contains `epub-corpus.json`. No execution path downloads publications or writes private book bytes into reports or build artifacts.

For a local run in PowerShell:

```powershell
$env:FLOW_EPUB_CORPUS_PATH = 'C:\path\to\private-corpus'
dotnet test tests/Flow.Epub.Tests/Flow.Epub.Tests.csproj --filter 'Category=EpubCorpus'
```

The external directory and its catalog remain local. Clear the variable after the run with `Remove-Item Env:FLOW_EPUB_CORPUS_PATH` when it should not affect later test sessions.

### Qualify one private publication directly

The `EpubCorpusExternal` test is intended for a book that a developer may inspect locally but cannot add to the repository. It creates an in-memory catalog entry, hashes the source as a stream, copies the verified input to the normal isolated workspace, and runs the complete qualification twice. The detailed report must also stay outside the repository.

```powershell
$env:FLOW_EPUB_REAL_BOOK_PATH = 'C:\path\to\book.epub'
$env:FLOW_EPUB_REAL_REPORT_PATH = Join-Path $env:TEMP 'flow-epub-qualification.json'
dotnet test tests/Flow.Epub.Tests/Flow.Epub.Tests.csproj --filter 'Category=EpubCorpusExternal'
Remove-Item Env:FLOW_EPUB_REAL_BOOK_PATH
Remove-Item Env:FLOW_EPUB_REAL_REPORT_PATH
```

If `FLOW_EPUB_REAL_BOOK_PATH` is absent, the test returns without doing external work. This keeps CI offline and independent of private files.

### Add a publication safely

1. Decide whether redistribution is allowed and keep evidence for that decision. If it is not, use `localNonRedistributablePublication`; do not copy the EPUB into the repository.
2. Give the entry a stable catalog ID that does not reveal a private file name.
3. Record the exact byte length and SHA-256. In PowerShell, use `Get-Item book.epub` for the length and `Get-FileHash book.epub -Algorithm SHA256` for the hash.
4. Declare only results and features that can be checked consistently. Do not baseline timing, memory, diagnostic wording, or cosmetic HTML.
5. Place a private book under `FLOW_EPUB_CORPUS_PATH`, run the optional `EpubCorpus` test, and inspect the detailed local report. The discovery service works on a verified temporary copy.
6. Commit only a redistributable fixture or public-domain publication whose license evidence has been reviewed. Private manifests and reports should remain local if their metadata is sensitive.

## Optional EPUBCheck evidence

EPUBCheck answers a different question from Flow. EPUBCheck checks whether the source publication conforms to EPUB rules. Flow checks whether its own pipeline can inspect, import, validate, preserve, lay out, and render the publication. Either side can pass while the other fails, so `EpubCorpusEvidenceRelationship` records agreement or divergence without changing the Flow status.

`EpubCheckProcessAdapter` accepts either an explicit EPUBCheck executable or an EPUBCheck JAR plus an explicit Java executable. With no configuration it returns `Disabled`. It never searches for, downloads, or installs either tool. Process startup uses `ProcessStartInfo.ArgumentList` with `UseShellExecute = false`; configured arguments and EPUB paths are never assembled into a shell command.

```csharp
var epubCheck = new EpubCheckProcessAdapter(
    new EpubCheckProcessOptions(
        EpubCheckToolKind.Jar,
        toolPath: @"C:\tools\epubcheck\epubcheck.jar",
        javaExecutablePath: @"C:\Program Files\Java\bin\java.exe"));
```

The adapter is passed to `EpubCorpusExecutor` through its composition constructor. Paths are local configuration and must not be added to the catalog or committed.

The adapter first runs `--version`, then requests the official JSON report with `--json - <publication.epub>`. The default compatibility range accepts EPUBCheck major version 5. The range is configurable because report behavior belongs to a particular tool version. The current upstream production release is documented on the [EPUBCheck project page](https://github.com/w3c/epubcheck), while command and JSON-report behavior come from the official [CLI reference](https://w3c.github.io/epubcheck/docs/cli/) and [reporting guide](https://w3c.github.io/epubcheck/docs/report/).

Execution has a configurable timeout and bounded stdout/stderr capture. Missing tools, unsupported versions, timeout, oversized output, unknown JSON, and abnormal exit codes become typed `EPC034`-`EPC039` diagnostics. Cancellation stops the process tree and removes the temporary EPUB copy. Persisted deterministic evidence contains normalized status, counts, messages, and EPUB-internal resource paths. Sanitized raw process output appears only in the explicitly non-deterministic report section when requested.

## Current boundary

There is no CLI corpus command, automatic baseline-acceptance command, bundled EPUBCheck installation, accessibility certification, or general EPUB conformance claim. The standard suite uses controlled project fixtures and a fake EPUBCheck process, so it does not require Java. A real small or medium book is tested only when a developer provides it through the external corpus. License compatibility remains a maintainer-reviewed catalog assertion.
