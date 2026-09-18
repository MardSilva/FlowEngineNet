# Resolved and reduced limitations

This file records boundaries that left `known-limitations.md` after a tested implementation changed the project. It is an engineering history, not a claim that Flow supports every EPUB or is production-ready. A limitation appears here only with the narrower boundary that still remains documented.

## Flow 0.2 alpha

### Private EPUB inventory

Before 21.1, Flow had no single operation for discovering and classifying a private EPUB directory without exposing editorial identity. The new `epub-inventory` command now:

- discovers `.epub` files recursively under explicit file-count and depth limits;
- opens source files read-only and skips symbolic links and reparse points;
- calculates SHA-256 and collapses identical payloads with a retained copy count;
- records EPUB 2/3 family, declared languages, byte counts, spine evidence and resource counts;
- separates corrupt, structurally unsuitable, protected and review-required candidates;
- recognizes supported font-obfuscation algorithms only when they target a manifest-declared font;
- writes deterministic JSON without physical paths, file names, titles, authors, publisher identifiers or content.

The first private run inventoried six distinct EPUB 3 payloads twice with identical catalog bytes and no source-file metadata changes. All six were structurally readable and unprotected by unsupported encryption. The later 21.2 run qualified the same neutral candidates without adding their bytes or identities to Git.

### Private batch qualification

Before 21.2, inventory could classify a directory, but semantic qualification still required a hand-written corpus manifest or one command per publication. `epub-inventory-qualify` now rediscovers eligible candidates by SHA-256 and runs the existing corpus pipeline twice for every candidate. The path-free report records deterministic evidence, completed phases, semantic counts, fidelity loss and diagnostic-code counts. Protected, corrupt and unsuitable entries remain visible as skipped outcomes.

This resolves the missing automatic batch qualification. It does not persist converted books, generate review packages for every candidate or replace the assisted human review used by the large-publication gate. Those narrower limits remain in `known-limitations.md`.

The first private batch was repeated with byte-identical report hashes and unchanged source snapshots. All six candidates were eligible and deterministic. Four passed without measured fidelity loss; two failed deterministically because loss was measured. Those losses remain current importer limitations rather than being counted as successful coverage.

### Legacy XHTML doctypes

Some older reflowable EPUBs declare the HTML doctype, a public W3C XHTML 1.0/1.1 doctype, or the public NISO NCX 2005-1 doctype even though the importer does not need the external DTD. The shared XML reader previously rejected those spine or navigation documents before semantic conversion.

XHTML and NCX loading now use separate, small allowlists and ignore accepted declarations without resolving or parsing DTD content. Container, OPF, SVG and every other XML input keep the stricter DTD prohibition. Unknown declarations, mismatched public/system identifiers, internal subsets and entity declarations remain errors. This resolves the three declaration patterns found during the expanded private-corpus run without weakening the existing XXE boundary.

### Images in headings and legacy anchor markers

Images nested in headings were previously reduced to their alternative text. The importer now retains their validated asset as a `Figure`, keeps heading text and level, and records the unavoidable block-level approximation through `EPUB010`. For an image-only heading, the alternative text supplies a semantic `Heading` label so TOC targets remain valid. Repeated import, source mapping, JSON round trip, canonical hash, layout and HTML rendering are covered by regression tests.

Two new private candidates also produced 151 `EPUB011` warnings for `<a>` elements that had no `href` because they were destination markers, not links. Markers with `id` or nonempty `name` are now transparent and preserve their children and source locations. Genuinely malformed anchors still receive `EPUB011`. The two candidates now retain the same content without those false positives, and the candidate with 53 heading images imports successfully with 104 figures and no semantic validation error.

### Neutral XHTML container fidelity

The fidelity analyzer previously placed every `div` and `span` in the same approximation bucket as semantic wrappers and unknown elements. That overstated loss: these neutral containers are intentionally flattened while their children and reading order remain available.

The source snapshot now marks neutral `div`/`span` units as transformations, while `aside`, `details`, specialized inline semantics, and unknown elements remain approximations. Aggregated informational `EPUB075` evidence records each neutral transformation by resource and container kind. The expanded private run recorded 22,598 such transformations across 14 eligible publications. Two consecutive reports were byte-identical with SHA-256 `6347C7BD1A239E33EBC5036674E2D684049A14E0D1B99B3D503567F708C0BCA9`; all candidates were stable, and no Flow error was reported. The correction changes neither `FlowDocument` nor canonical bytes, hashes, layout, or HTML output. Four publications still have one measured lost unit each from separate causes, which remain open instead of being reclassified by this change.

### EPUB heading-level repair

One private candidate originally stopped before layout because its XHTML repeatedly jumped from `h1` to `h3`. The importer now applies the smallest deterministic repair within each XHTML resource, keeps the first heading of every new chapter independent and emits `EPUB071` for every aggregated source pattern it changes. Manually created Flow documents remain subject to strict heading validation.

After the change, the candidate completed all 12 qualification phases, including mobile and desktop layout and both HTML packages, with no validation diagnostics. Two complete private-batch executions produced the same report SHA-256, and the six source files remained unchanged. The repair does not reconstruct an editorial outline; that narrower boundary remains in `known-limitations.md`.

### Private-batch fidelity reconciliation

The first private batch left 24 source units classified as lost. Nineteen were note references that targeted a resource rather than a fragment; the destination resource contained exactly one semantic note without a source ID. The importer now resolves only that unambiguous structure, assigns a deterministic Flow ID and records `EPUB072` as an approximation.

The remaining five units came from one safely representable percent-encoded `mailto` link and four image-only external links. Safe encoded mail is retained and checked consistently by import, HTML rendering and package verification. The first correction made the four image associations explicit through `EPUB073`; the later typed `FigureLink` model now preserves their destinations without loading them. Two complete runs qualified all six candidates through all 12 phases with zero lost units and identical report SHA-256.

### Private difference classification

Before 21.3, private qualification could report pass, loss and diagnostic counts, but it did not distinguish unsupported source features, source defects, Flow errors, approximations and pending human decisions in one typed result. `epub-inventory-matrix` now verifies the exact qualification-report hash and classifies its existing evidence without reopening the publications. The deterministic matrix excludes editorial identity and book content, rejects malformed or inconsistent input and keeps human review pending.

Diagnostic occurrence counts now remain typed through import, corpus execution, qualification, fidelity evidence and diagnostic JSON. This preserves aggregated findings such as repeated unsupported associations instead of reducing each diagnostic row to one occurrence. The first private matrix retained all four `EPUB073` occurrences and classified unsupported evidence separately from loss and approval.

This closes the missing automatic-classification step. Neutral resource locations are still unavailable when the aggregate qualification report does not contain them, and the matrix is not a substitute for editorial, visual or accessibility review. Those boundaries remain in `known-limitations.md`.

### Corpus-wide assisted visual review

Before 21.5, review packages could be generated only by invoking `epub-review` for one known file at a time. `epub-inventory-review` now verifies a qualification report hash, rediscovers qualified private candidates by source SHA-256 and generates their review packages in one bounded run. One candidate failure no longer prevents the remaining candidates from receiving mobile and desktop packages.

The path-free corpus report and neutral index expose beginning, middle and end samples plus shortcuts to relevant structures such as images, notes, tables, internationalized content, SVG, MathML and diagnostic concentration. Book content remains only in the private HTML packages outside Git. Human checklist decisions are never inferred or marked complete automatically.

The first private run generated all six candidate packages without a missing source, skipped item or package failure. Repeating the run produced 718 files with no path-or-byte difference between the two output trees. The source EPUBs and both review directories remained outside the repository.

### Embedded-font resource triage

The first private difference matrix classified five candidates as containing unsupported content because `EPUB009` covered every manifest resource not imported as reading content. Direct inspection showed that all 28 occurrences behind that result were OTF or TTF files referenced by publication CSS, not missing text or unknown reading-order content.

The inspector now recognizes EPUB font media types structurally. Import keeps the typed `font-family` intent, excludes the font bytes and emits `EPUB074`; fidelity and the private matrix classify that evidence as a typography approximation with cause `EmbeddedFontSubstitution`. A host may use an installed matching family, but import does not depend on host font discovery. Unknown binary resources and unsupported spine content continue to use `EPUB009`.

Two private qualifications and two matrix generations produced identical bytes within each pair. After figure-link support, the current matrix contains six candidates with approximations and none with unsupported content, measured loss, broken source references or Flow errors. This reduces the generic-resource false positive without claiming exact visual fidelity or permission to redistribute embedded fonts.

### Figure links

`Figure` now has an optional immutable `FigureLink`. Internal targets use `DocumentAnchor`; safe HTTP, HTTPS and `mailto` targets retain their original absolute URI. EPUB image-only anchors therefore survive import, JSON round trips, layout and standalone or multipage HTML without network access during conversion. Unsafe, empty, unresolved or ambiguous targets still leave the figure intact and produce a diagnostic.

Because the destination is semantic content, the current writers use `flow-json-0.2` and `flow-c14n-0.2`. The reader still accepts `flow-json-0.1`, and `FlowDocumentCanonicalizerV01` preserves the previous projection for compatibility checks. Two private qualifications and two matrices were byte-identical within each pair: all six candidates now have approximations, with no `EPUB073`, unsupported-content classification, measured loss or Flow error.

### Real large-publication gate

The earlier absence of a complete large-publication run was resolved for one verified, legally obtained and DRM-free private input. Two final automatic executions produced identical stable evidence. Import, validation, JSON round-trip, canonical integrity, layout, HTML-package verification and structural/reference auditing completed, followed by assisted review at exact mobile and desktop viewports.

This closed the missing-execution gap for that publication. It did not establish a universal size limit, EPUB conformance or publisher-wide compatibility, so those narrower restrictions remain current.

### Inline image preservation

Safe images appearing inside mixed paragraph content were previously measured as lost. The importer now promotes them to ordered `Figure` nodes, preserves their assets and source order, and deduplicates repeated bytes. The verified large publication went from 41 measured lost image units to none.

The representation is still approximate because the current semantic model has no inline-image node. That distinction remains in `known-limitations.md`.

### Internal links and footnote backlinks

The large-publication audit previously relied on approximate ownership for some generated nodes. Source location now falls back to the nearest mapped semantic ancestor, and backlink auditing checks structural destinations. In the verified run, all 146 classified internal links and all 120 footnote backlinks resolved without approximation.

Repeated spine occurrences and the lack of a dedicated backlink node remain separate model restrictions.

### Responsive HTML review package

The first assisted review found global mobile overflow, wide-table overflow and weak note-target framing. The HTML package now contains media and long headings within the reading surface, gives wide tables their own scroll region and visibly frames note targets. The repeated review passed at 390 x 844 and 1600 x 1000 CSS pixels.

This resolves the observed regressions, not a full browser, writing-mode, screen-reader or accessibility audit.

### Operational corpus and gate CLI

Corpus qualification, large-publication qualification and assisted review were previously available only through APIs. The CLI now exposes `corpus`, `epub-qualify` and `epub-review`, with localized human-readable output, explicit legal/DRM-free declarations and deterministic evidence.

Transactional output added recognized `--force` replacement, safe restart through `--resume`, destination-specific cross-process locks, read-only execution inspection and UUID-bound cleanup. Partial semantic or HTML output is never treated as a checkpoint or completed result.

Distributed scheduling, authentication and unusual filesystems that ignore exclusive file sharing remain outside the contract.

### Local CLI distribution evidence

The CLI can be packed as the framework-dependent .NET tool `FlowEngineNet.Tool`. Windows and Linux smoke tests install it from an isolated local source and exercise the launcher without changing global tools. The release pipeline produces one normalized candidate on Ubuntu after two matching canonical builds, verifies its checksums, SBOM and manifest, then installs and exercises that exact package on Ubuntu and Windows.

This replaced the failed assumption that independent Windows and Linux builds must be byte-identical. Public publication, signing, macOS validation, self-contained deployment and independent reproducible builders remain unresolved.

### Generated interface localization

Human-readable CLI framing and review material now use resource catalogs for `en-US` and `pt-BR`. The HTML-book package also resolves generated interface text to English, `pt-PT` or `pt-BR` without translating authored content. Commands, JSON fields and diagnostic codes remain invariant.

Original technical diagnostic details, runtime language switching and a complete localization audit remain current limitations.

## Flow 0.1 foundation

The 0.1 cycle closed the original absence of a semantic document model, stable typed IDs and anchors, structural validation, optional typed presentation, user preference cascade, deterministic `.flow.json`, versioned canonicalization, SHA-256 integrity, a renderer-independent Flow layout, standalone semantic HTML, a sample book, a framework-free CLI, local RSA signature proof of concept and the first bounded EPUB importer.

Those capabilities are implemented and covered by the numbered conformance suite. Their narrower interoperability, trust, rendering and publishing boundaries remain listed in `known-limitations.md`.
