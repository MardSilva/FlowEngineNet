# Experimental EPUB corpus catalog

The `flow-epub-corpus-0.1` profile describes publications used for `Flow.Epub` interoperability tests. The catalog records evidence about an input; it is not part of the converted book. Catalog fields, diagnostics, and results stay outside `FlowDocument`, `.flow.json`, `flow-c14n-0.1` canonicalization, hashes, and signatures.

The catalog contract, local discovery API, end-to-end executor, reviewed baselines, repeated-run qualification, and optional EPUBCheck adapter are implemented.

## Private directory inventory

`IEpubPrivateInventoryService` builds a neutral catalog before a private directory enters corpus qualification. Discovery is recursive, bounded by candidate count and depth, skips symbolic links and reparse points, opens files read-only, and applies the existing EPUB archive and XML limits during structural inspection.

Readable files receive SHA-256 hashes before inspection. Identical bytes become one candidate with a copy count. `EpubPrivateInventoryReportJsonSerializer` writes deterministic UTF-8 without BOM and with LF line endings. The report excludes source paths, file names, titles, authors, publisher identifiers, timestamps and content.

Protection handling is conservative. IDPF and Adobe font-obfuscation algorithms cause review instead of a DRM claim. Unknown encryption is classified as protected and is not decrypted. A `rights.xml` file records review evidence but is not proof of DRM by itself. Invalid archives, unknown EPUB families and publications without a supported linear XHTML reading order receive separate statuses instead of being omitted.

The CLI requires the source and output to remain outside the repository and refuses to place its catalog in the source tree:

```powershell
flow --language pt-BR --banner epub-inventory C:\books `
  --output C:\flow-local\epub-inventory.json `
  --repository-root C:\src\FlowEngineNet
```

## Private directory qualification

`IEpubPrivateQualificationService` turns the neutral inventory into a repeated semantic qualification without persisting a manifest containing private paths. Ready and review-required candidates are rediscovered by their expected size and SHA-256. Protected, corrupt and unsuitable candidates stay in the report with an explicit skipped status.

Each eligible candidate goes through the existing `EpubCorpusQualificationService` twice. The phases cover structural inspection, import, document validation, fidelity analysis, Flow JSON round-trip, canonical hash comparison, mobile and desktop layouts and HTML-book verification. Required phase outcomes are expressed through an in-memory local corpus entry; no second importer or renderer pipeline exists for the private workflow.

`flow-epub-private-qualification-0.1` stores neutral IDs, source and canonical hashes, completed phases, semantic and output counts, fidelity loss and aggregated diagnostic codes. It excludes the physical path, file name, title, author, publisher identifier, publication text, ordered node IDs and asset bytes. Because hashes can identify exact bytes, this report is private and must remain outside Git.

```powershell
flow --language pt-BR --banner epub-inventory-qualify C:\books `
  --report C:\flow-local\epub-qualification.json `
  --repository-root C:\src\FlowEngineNet `
  --legal-use --drm-free
```

The two declarations record caller assertions; they do not establish legal rights or detect every DRM system. Exit code `0` means that every discovered candidate was eligible, completed the automatic pipeline without measured fidelity loss and produced stable evidence twice. Exit code `2` keeps the report but signals a failed, lossy, inconclusive, nondeterministic or skipped candidate. Human review remains a separate per-publication workflow.

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

The Flow JSON round-trip uses a private per-publication temporary file rather than a complete JSON `MemoryStream`. The file is deleted as soon as deserialization and hash comparison finish. Mobile and desktop layouts and packages are then produced and verified one at a time; only node counts, file counts, byte totals, and required-element evidence survive each package phase. The workspace is removed when processing succeeds, fails, or is cancelled.

Every publication ends as `Passed`, `Failed`, `Skipped`, or `Inconclusive`. A missing local book is skipped. A corrupt archive, discovery hash mismatch, invalid Flow document, unstable round-trip hash, broken generated package reference, or unmet declared expectation fails only that publication. An expectation token the current executor does not understand is reported as inconclusive instead of being treated as success. Processing then continues with the other catalog entries.

The executor currently recognizes these expectation tokens:

- resources: `xhtml`, `css`, `ncx`, `jpeg`/`jpg`, `png`, `gif`, `webp`, and `svg`;
- features: `spine`, `table-of-contents`/`toc`, `figure`/`image`, `internal-link`, `ordered-list`, `unordered-list`, `footnote`/`note`, `table`, and `cover`;
- results: `inspection-success`, `import-success`, `valid-flow-document`, `roundtrip-stable`, `canonical-hash-stable`, `mobile-layout`, `desktop-layout`, and `html-book-package`.

`flow-epub-corpus-execution-0.1` records completed phases, stable diagnostics, structural counts, document identity, canonical hash, output sizes, and a corpus summary. Its default JSON projection excludes elapsed time, sampled managed heap, and sampled process working set, uses UTF-8 without BOM and LF, contains no physical paths, and can be written atomically with `EpubCorpusExecutionReportJsonSerializer.WriteAtomicallyAsync`. Environment-dependent observations can be included under the explicitly non-deterministic `nonDeterministicEnvironment` section and must not be used for byte-for-byte baseline comparison. These samples are phase-boundary observations, not exact allocation measurements or portable pass limits; the executor never forces garbage collection to improve them.

## Qualification and reviewed baselines

`EpubCorpusQualificationService` executes the same verified inputs twice. It compares stable evidence from both runs and reports whether import, IDs, semantic order, validation, and the canonical hash are deterministic. It also places catalog expectation failures, fidelity loss, and optional EPUBCheck status side by side without treating them as interchangeable results.

`flow-epub-corpus-baseline-0.1` keeps only evidence suited to review: status, EPUB version, manifest/spine counts, imported nodes and assets, validation and fidelity-loss counts, document identity, canonical hash, ordered node IDs, semantic feature counts, source-map count, and required diagnostic codes. Runtime measurements, rendered byte sizes, diagnostic prose, and accidental diagnostic order are excluded. Extra diagnostics are allowed; removal of a required code needs review.

The checked-in public baseline is `tests/Flow.Epub.Tests/Corpus/epub-corpus-baseline.json`. CI regenerates all fixtures, runs the corpus twice, and compares the observed evidence with that file on Windows and Linux. A baseline change should therefore be reviewed as a semantic change, not refreshed automatically to make a test pass.

For local or non-redistributable entries, `EpubCorpusQualificationService.WriteLocalDetailedReportAsync` writes `flow-epub-corpus-qualification-0.1` atomically. It records repeated-run determinism, baseline differences, fidelity loss, expectation failures, optional EPUBCheck evidence, and a first/middle/last sample of the ordered chapter IDs. Environment observations stay in a clearly marked non-deterministic section. The report contains catalog IDs and sanitized EPUB-internal resources, never the book bytes or physical source path.

The normal test suite uses generated fixtures only. The optional xUnit test is marked with category `EpubCorpus`; it does nothing unless `FLOW_EPUB_CORPUS_PATH` is set and that directory contains `epub-corpus.json`. No execution path downloads publications or writes private book bytes into reports or build artifacts.

## Large-publication preflight

`EpubLargePublicationPreflightService` evaluates only candidates explicitly supplied by the host. Each candidate has a neutral corpus ID, a local path, and affirmative legal-use and DRM-free declarations. Inputs inside the repository, missing or non-EPUB files, symbolic links, and files above the existing archive limit are rejected before inspection. The service never searches a personal directory on its own.

Default suitability requires a successful EPUB 2 or EPUB 3 inspection, a linear reading order, XHTML content, a navigation document or NCX, and at least two resource classes. Length is structural evidence rather than a page claim: a candidate needs at least 20 linear spine items, 20 XHTML documents, or at least 512 KiB of XHTML together with five linear spine items. Hosts can supply stricter typed criteria without raising the importer's security limits. Selection is deterministic and prefers richer feature evidence, more resource classes, more XHTML bytes, a longer spine, more XHTML documents, and then larger total uncompressed content.

The path-free `flow-epub-large-preflight-0.1` report contains the candidate ID, SHA-256, EPUB family, manifest and spine counts, linear/non-linear counts, grouped resource counts, archive byte totals, feature evidence, and stable diagnostic codes. It deliberately omits physical paths, file names, title, author, publisher, language, and publication text. TOC and image presence can be inferred from OPF evidence; ordinary links, notes, and tables remain `unknown` until later gate phases inspect semantic content. `EpubLargePublicationPreflightReportJsonSerializer.WriteAtomicallyAsync` writes UTF-8 without BOM and LF while preserving an existing destination if cancellation happens before replacement.

```csharp
var candidates = new[]
{
    new EpubLargePublicationCandidate(
        new EpubCorpusPublicationId("candidate-001"),
        localEpubPath,
        legalUseDeclared: true,
        drmFreeDeclared: true),
};

var report = await new EpubLargePublicationPreflightService().EvaluateAsync(
    candidates,
    repositoryRoot,
    cancellationToken: cancellationToken);

await EpubLargePublicationPreflightReportJsonSerializer.WriteAtomicallyAsync(
    report,
    localReportPath,
    cancellationToken);
```

The optional external test accepts an explicit `|`-separated list of `neutral-id=absolute-epub-path` entries. Setting the variable is an affirmative declaration that every listed copy is legally available for local testing and DRM-free. The report path must remain outside the repository.

```powershell
$env:FLOW_EPUB_LARGE_CANDIDATES = 'candidate-001=C:\books\one.epub|candidate-002=C:\books\two.epub'
$env:FLOW_EPUB_LARGE_PREFLIGHT_REPORT = 'C:\flow-local-reports\large-preflight.json'
dotnet test tests\Flow.Epub.Tests\Flow.Epub.Tests.csproj --filter 'Category=EpubLargePreflightExternal'
```

## Large-publication gate contract

`IEpubLargePublicationGate` defines the host-independent boundary for the full large-book gate. `EpubLargePublicationGate` implements the automatic part of that contract. It verifies the explicitly supplied local file and its SHA-256, makes a bounded temporary copy, and executes inspection, import, fidelity analysis, validation, Flow JSON round-trip, canonical integrity, mobile layout and HTML, then desktop layout and HTML. The whole sequence runs at least twice. The second run is compared with the first using stable evidence, phase outcomes, diagnostic codes, semantic order and hashes.

Mobile and desktop packages are rendered and verified one at a time. The verifier checks local files and fragments and returns only counts and byte totals; the package itself is released before the next target is created. Round-trip JSON uses a private temporary file that is removed immediately after deserialization. The gate workspace is deleted after success, failure, or cancellation.

The structural-audit phase follows references across the complete pipeline. It compares spine entries with semantic chapters and chapter pages without treating titles or file names as identity, keeps spine order authoritative, resolves TOC entries and internal links, verifies that external links survive in both HTML packages without loading them, and checks figures, covers, passive SVG assets, asset deduplication, notes, table structure, IDs, source locations, layouts, and rendered HTML. When a generated semantic child has no direct source location, link classification inherits the resource path from its nearest mapped ancestor. This inheritance is used only by the audit and does not alter the `FlowDocument`, `EpubSourceMap`, canonical bytes, or hashes. A footnote backlink is resolved only when its target block contains a `FootnoteReference` back to that same note. Table captions, spans, and header references are reported separately; cross-resource footnotes are also a separate family.

Each reference family records `found`, `resolved`, `broken`, `ambiguous`, `approximated`, and `skipped` as mutually exclusive counts. `Absent` means that an optional semantic construct was not found, while `NotApplicable` means that the check had no relevant input. A broken or ambiguous essential reference fails the automatic gate. Repeated failures are aggregated into stable diagnostic codes, and the report contains counts only—never EPUB text, HTML, paths, titles, or authors.

`EpubLargePublicationGateResult` always expands its phase list to every planned phase. A phase blocked by an earlier failure therefore remains `NotStarted`, or is recorded explicitly as `Skipped` or `Inconclusive`, instead of disappearing or looking successful. A missing or failed mandatory automatic phase fails the gate. The default options still require human review, so a technically successful automatic run remains `Inconclusive` until that separate review is recorded. Setting `requireHumanReview: false` is intended for automatic regression fixtures, not for claiming that a real publication passed the complete release gate.

The contract keeps automatic checks separate from human review items. Both use stable IDs and typed outcomes: `NotStarted`, `Passed`, `PassedWithWarnings`, `Failed`, `Skipped`, or `Inconclusive`. Diagnostics carry only a stable code, severity, and phase. They do not carry book excerpts or machine paths.

`flow-epub-large-publication-gate-0.1` stores reproducible options, deterministic structural evidence, every phase, automatic checks, human-review items, and diagnostics. Stable evidence includes EPUB size and version, manifest and spine counts, nodes, characters, assets, chapters, Flow JSON bytes, semantic-order and anchor-set hashes, canonical hash, layout counts, separate mobile/desktop HTML totals, and the typed reference-audit counts. The default projection writes an explicit `nonDeterministicEnvironment` section with `included: false`. Approximate duration, managed heap, and working set are sampled by phase and can be included for local analysis, but remain in that separate section. They are not byte-for-byte baseline evidence or universal pass limits, and the executor does not force garbage collection.

`EpubLargePublicationGateReportJsonSerializer` produces deterministic UTF-8 without BOM, ends with LF, and replaces a destination atomically. The JSON contains neutral candidate identity and hashes, counts, statuses, and diagnostic codes. It does not contain the EPUB path, title, author, licensed text, or generated HTML.

EPUBCheck remains separate evidence. A Flow failure does not become an EPUBCheck failure, and EPUBCheck success cannot turn a failed Flow phase into success. The current automatic gate does not run EPUBCheck itself; hosts can retain the existing corpus evidence alongside the gate report.

## Assisted local review

`IEpubLargePublicationReviewPackageGenerator` prepares the material needed to review a large publication without adding licensed artifacts to the repository. The caller must provide the candidate, its expected SHA-256, the repository root to protect, and an absolute output directory outside that root. The generator verifies legal-use and DRM-free declarations, checks the source hash, imports and validates the document, then writes mobile and desktop HTML packages sequentially.

The destination contains:

- `mobile/` and `desktop/`, each with a complete HTML book package;
- `review.html`, a script-free page with localized `en-US` or `pt-BR` labels and direct local links to the beginning, middle and end, plus representative TOC, link, image, cover, note and table targets when present;
- `review-checklist.json`, a versioned human checklist whose items accept `approved`, `rejected`, `not-applicable` or `inconclusive`;
- `review-manifest.json`, which relates the local artifacts to the neutral candidate ID, source EPUB SHA-256, canonical FlowDocument SHA-256, canonicalization version and package hashes.

The checklist starts as `inconclusive` for every applicable decision. The generator never converts visual inspection into an automatic pass. A structurally absent optional feature starts as `not-applicable`. Reports contain only IDs, relative targets, statuses and hashes; they do not contain publication text, titles, authors, physical source paths or generated HTML.

Generation takes place in a sibling staging directory. The completed directory replaces the destination only after every file has been written. A failure or cancellation removes staging data and leaves a previous destination intact. To avoid replacing unrelated files, an existing directory is accepted only when it contains a recognized Flow review manifest. Existing reparse points, relative output paths, file-system roots, destinations that contain the source EPUB, and destinations inside the protected repository are rejected. The generator does not launch a browser or execute scripts.

Minimal host-side usage:

```csharp
var options = new EpubLargePublicationReviewOptions(
    candidate.Id,
    expectedSourceSha256,
    @"C:\flow-local-reviews\candidate-001",
    repositoryRoot,
    HtmlBookUiLanguage.PortugueseBrazil);

var generator = new EpubLargePublicationReviewPackageGenerator();
var result = await generator.GenerateAsync(candidate, options, cancellationToken);
```

The result returns the local destination and content-free hashes for correlation. It does not update the automatic gate or mark the human-review phase as passed; that decision remains separate and must be recorded by the reviewer.

### Optional real-publication gate

The external large gate remains inactive during normal builds. A maintainer can run one explicitly selected local publication by setting four process-local variables:

```powershell
$env:FLOW_EPUB_LARGE_GATE_CANDIDATE = 'candidate-001=C:\books\selected.epub'
$env:FLOW_EPUB_LARGE_GATE_SHA256 = '<64 hexadecimal characters>'
$env:FLOW_EPUB_LARGE_GATE_REPORT = 'C:\flow-local-reports\candidate-001-gate.json'
$env:FLOW_EPUB_LARGE_REVIEW_OUTPUT = 'C:\flow-local-reports\candidate-001-review'

dotnet test tests/Flow.Epub.Tests/Flow.Epub.Tests.csproj `
  --filter 'Category=EpubLargeGateExternal'
```

The test always writes the gate report before evaluating the automatic result. It also prepares the local review directory when import and rendering can complete, even if a fidelity assertion later fails. The test fails on any incomplete automatic phase. Until a reviewer records the separate decisions, the human-review phase remains `NotStarted` and the overall result remains `Inconclusive`. Clear the four variables after the run so they do not affect later test sessions.

The first private long-publication run exercised 39 linear chapters, 3,868 semantic nodes and more than 1.1 million imported characters. Reading order, validation, canonical round-trip, deterministic repetition, TOC destinations, notes, tables, assets, mobile/desktop layouts and HTML IDs passed, but 41 of 56 XHTML image occurrences lacked a destination representation. After the image and reference-audit corrections, two final automatic runs on the same SHA-256-verified input produced identical stable evidence, zero lost fidelity units, 56 reconciled image occurrences, 146 resolved internal links and 120 resolved footnote backlinks. The environment observations remained separate from the deterministic comparison.

The first human review did not approve the gate because the 390 x 844 package allowed wide tables, covers and long headings to expand the reading surface horizontally, and a note shortcut did not frame its destination usefully. A focused renderer correction now contains media and long text, isolates tables in keyboard-accessible horizontal scroll regions, and highlights note targets. The repeated review passed the beginning, middle, end, TOC, images, links, notes and tables at exact 390 x 844 and 1600 x 1000 CSS-pixel viewports.

The mobile review used browser device-metric emulation rather than only resizing a screenshot window. This distinction matters: a browser can retain a wider CSS viewport while producing a 390-pixel bitmap, which resembles clipping even when the document itself does not overflow. In the accepted run, `html`, `body` and the article had equal client and scroll widths. The evidence and edited checklist remain outside the repository with the licensed output.

The same run completed without memory or output-package failure. Its environment-specific samples did not justify implementing ZIP spooling or progressive HTML writing before correcting fidelity. Those measurements are local observations, not support limits or performance guarantees.

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

The CLI exposes corpus qualification, the automatic large-publication gate and assisted-review generation. It does not automatically accept a baseline, install EPUBCheck, certify accessibility or claim general EPUB conformance. The standard suite uses controlled project fixtures and a fake EPUBCheck process, so it does not require Java. Real publications are tested only when a developer supplies them explicitly. License compatibility remains a maintainer-reviewed catalog assertion.

## CLI workflow

Run a versioned corpus whose declared EPUB files have already been placed in the permitted repository or external directory. The checked-in test manifest depends on fixtures generated by the test assembly; the production CLI does not materialize those test-only bytes.

```powershell
$manifest = 'C:\flow-corpus\epub-corpus.json'
$corpusRepository = 'C:\flow-corpus\repository'

dotnet run --project .\src\Flow.Cli -- corpus $manifest `
  --repository-root $corpusRepository `
  --external-root 'C:\books\flow-corpus' `
  --baseline 'C:\flow-corpus\accepted-baseline.json' `
  --report 'C:\flow-local\corpus-qualification.json'
```

For one real, DRM-free publication, compute its hash first and keep all licensed output outside the repository:

```powershell
$epub = 'C:\books\book.epub'
$sha256 = (Get-FileHash $epub -Algorithm SHA256).Hash
$repository = (Get-Location).Path

dotnet run --project .\src\Flow.Cli -- epub-qualify $epub `
  --candidate-id candidate-001 --sha256 $sha256 `
  --report 'C:\flow-local\candidate-001-gate.json' `
  --repository-root $repository `
  --legal-use --drm-free

dotnet run --project .\src\Flow.Cli -- epub-review $epub `
  --candidate-id candidate-001 --sha256 $sha256 `
  --output 'C:\flow-local\candidate-001-review' `
  --repository-root $repository --legal-use --drm-free --ui-language pt-BR
```

`--legal-use` and `--drm-free` record assertions made by the caller. They do not inspect licensing terms, decrypt content or bypass DRM. A successful automatic gate remains `inconclusive` in its report until the separate human checklist is completed. The CLI returns `0` when all automatic checks pass; it never marks human items as approved.

The three operational commands refuse an existing final destination unless `--force` is explicit. A destination-specific sidecar remains exclusively open while the command runs, preventing another Flow process from writing the same output. It records a random execution ID, state and destination fingerprint, but no physical path or publication data; it stays outside deterministic evidence and canonical content. An interrupted run requires `--resume`, which takes over the recognized lock, assigns a new ID, cleans only matching temporary artifacts and restarts the full operation. Partial reports and review packages are not checkpoints. When replacement and recovery are both intended, pass `--force --resume`. Unknown or ambiguous artifacts are preserved for manual inspection.

`flow execution-status <destination>` provides read-only local diagnosis and an optional path-free JSON report. `flow execution-clean <destination> --execution-id <id>` performs maintenance only after matching the exact recorded UUID and acquiring the lock exclusively. It retains the completed sidecar and never treats an invalid or copied lock as cleanup authority.
