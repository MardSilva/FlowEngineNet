# HTML book package

`HtmlBookPackageRenderer` is an additional output of `Flow.Rendering.Html`; it does not replace `HtmlDocumentRenderer`. It converts one validated `FlowDocument` and matching `LayoutDocument` into a deterministic, script-free directory that can be opened through `file://` without a server.

```text
book/
  index.html
  toc.html
  chapters/
    chapter-001.html
    chapter-002.html
  backmatter/
    notes.html              # only when a note cannot belong to one chapter
  assets/
  styles/
    book.css
  manifest.json
```

`chapter-001.html` describes package reading order only. It is not a semantic identifier, page number, print position, or canonical field. Stable Flow `NodeId` values remain HTML `id` attributes and never depend on these filenames.

## Content placement policy

The package uses the semantic top-level order from `DocumentContent`:

- `TableOfContents` is removed from the content stream and rendered in `toc.html`;
- non-TOC content before the first `Chapter` is rendered in `index.html`;
- every `Chapter` starts its own `chapters/chapter-NNN.html` file;
- non-`Chapter` content between chapters or after the last chapter follows the preceding chapter in that chapter file;
- when there are no chapters, all non-TOC content remains in `index.html`;
- when no semantic TOC exists, `toc.html` contains a generated navigation list of chapters without changing the Flow document.

Footnote placement is derived from the immutable reference graph rather than the physical location of the source EPUB note resource:

- a note referenced from exactly one chapter is rendered once at the end of that chapter;
- repeated references inside that chapter share the same note definition and semantic ID;
- a note referenced by more than one chapter is rendered once in conditional `backmatter/notes.html`;
- references and backlinks are rewritten to same-file or cross-file anchors as appropriate;
- the original `Footnote` and every descendant `NodeId` remain unique across the package.

The current Flow model does not distinguish a source footnote from a source endnote canonically. Consequently, explicit endnote intent cannot yet be used as a placement signal without a separately versioned model/canonicalization decision. Shared notes use back matter conservatively; single-chapter notes favor immediate reading context.

`index.html` always contains a metadata-derived title page and a direct route to the contents. When `DocumentPresentation.Cover` points to a figure owned by another file, the index also contains a presentation-only cover preview linking to the original semantic figure. The preview has no invented `NodeId`; the real figure retains its original ID in its owning chapter. A credits section is emitted only when author metadata exists; the renderer does not infer publisher, rights, edition, or other unavailable credits.

## Links, assets, and CSS

The renderer builds a complete `NodeId`-to-file index before writing pages. Flow anchors, TOC destinations, footnote calls, and ordinary backlinks become same-file fragments or relative cross-file references. Semantic TOC levels become genuinely nested ordered lists rather than a visually indented flat list. Each page has previous, contents, and next navigation where applicable. Tests resolve every generated local link against the package.

Assets are grouped by SHA-256 bytes. Equal bytes produce one safe `assets/<lowercase-sha256>.<extension>` file even when several `AssetId` values reference them. EPUB filenames and paths never become package paths. Figures use relative asset references appropriate to their page.

`styles/book.css` is reconstructed from the already-resolved typed layout cascade. No EPUB CSS source text enters the package. Pages use a restrictive CSP, load only the local stylesheet and local images, and contain no JavaScript or network resource.

## Reading experience and accessibility structure

Every file is an independent logical reading position with a book masthead, previous/contents/next links, a content landmark, and a footer. Numbered chapter headings establish the editorial `Chapter N of M` count when they exist; cover, heading-free front matter, introduction, acknowledgements, preface, dedication, and notes use descriptive labels instead. For publications without numbered headings, headed chapter nodes remain the fallback count. These values never claim to be physical page numbers and do not enter the Flow document.

The package UI supports explicit `pt-PT`, `pt-BR`, and `en` selection plus `auto`. Automatic selection uses `pt-BR` for Brazilian Portuguese, `pt-PT` for other Portuguese tags, and English as the deterministic fallback. This localizes navigation, appearance controls, landmarks, credits, notes, and logical progress without translating document content. The HTML shell carries the resolved UI language while the semantic `article` retains the publication language. Authored TOC headings and labels are preserved.

One package has one `index.html` and one resolved UI language. Multiple localized editions are generated into separate directories; the renderer does not duplicate every page or asset behind language-specific index files. `manifest.json` records the resolved noncanonical `uiLanguage`.

The package includes:

- one skip link to the unique `main` landmark on every file;
- distinct labels for primary and secondary book navigation;
- native links, details, radio buttons, fieldsets, legends, and labels that remain keyboard-operable without script;
- visible `:focus-visible` treatment and a visible skip link when focused;
- a balanced chapter-opening treatment and a responsive reading measure capped at `46rem`;
- light, dark, and sepia choices plus a book-default theme that respects `prefers-color-scheme` when the resolved theme is `System`;
- publisher/resolved, safe system-serif, and safe system-sans font choices;
- bounded local text enlargement choices layered over the already-resolved typed preferences;
- `prefers-reduced-motion` handling and basic print CSS that hides reader chrome.

Theme and font controls are implemented as CSS progressive enhancement with no script. Browsers without `:has()` still display and navigate the complete book with its resolved default cascade; the optional appearance switches may not apply there. Choices are local to each HTML file and are not persisted between chapters. When renderer safety resolves `HighContrast`, alternative theme controls are withheld so the package cannot override that constraint.

These structural features and tested color pairs improve the baseline but are not a WCAG conformance claim, screen-reader certification, contrast audit of arbitrary document-authored colors, or production print/pagination system.

## Manifest and integrity

`manifest.json` uses format `flow-html-book-0.1` and contains:

- document ID, optional version, and title;
- canonical hash algorithm, value, and canonicalization profile supplied by the host;
- logical reading order with page kind, noncanonical `publicationRole`, and chapter `NodeId` where applicable;
- every payload file, media type, byte length, and SHA-256 hash.

The manifest does not hash itself because embedding its own digest would be recursively undefined. This is explicit through `manifestSelfHashExcluded: true`; every other package file is listed and verifiable. Package output and its manifest remain renderer artifacts and do not participate in `flow-c14n-0.1`.

## CLI and safe replacement

```powershell
dotnet run --project src/Flow.Cli -- render book.flow.json --html-book output-directory
Start-Process output-directory/index.html
```

The CLI uses a deterministic 1024×768 logical Flow layout for this command; responsive CSS still adapts the files to mobile and desktop windows. API hosts can supply another valid `LayoutContext` and `UserReadingPreferences`.

Files are first written to a new sibling temporary directory. For a new target, that complete directory is moved into place. An existing target is replaced only when it contains a valid Flow HTML-book manifest and no reparse points; it is renamed to a backup before the complete temporary directory is moved into place, and restored if that move fails. Filesystem roots, existing files, output directories containing the source `.flow.json`, unsafe package paths, and arbitrary existing directories are rejected.

## Current limits

This is a logical multi-file reading package, not EPUB export, MHTML, Web Publication packaging, pagination, production print layout, or a Reader application. Editorial roles are conservatively inferred from cover intent and headings because the canonical model does not yet carry a complete publication-matter vocabulary. UI localization is currently limited to English, European Portuguese, and Brazilian Portuguese; diagnostics and the CLI shell remain English. There is no runtime language switch, JavaScript, search, persistent cross-file progress/preferences, service worker, browser cache manifest, WCAG certification, screen-reader audit, or full browser/file-URL matrix. The next increment addresses performance, memory, progress reporting, cancellation, and evidence for reducing duplicated/in-memory publication bytes.
