# HTML book package

`HtmlBookPackageRenderer` is an additional output of `Flow.Rendering.Html`; it does not replace `HtmlDocumentRenderer`. It converts one validated `FlowDocument` and matching `LayoutDocument` into a deterministic, script-free directory that can be opened through `file://` without a server.

```text
book/
  index.html
  toc.html
  chapters/
    chapter-001.html
    chapter-002.html
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

`index.html` always contains a metadata-derived title page. When `DocumentPresentation.Cover` points to a figure owned by another file, the index also contains a presentation-only cover preview linking to the original semantic figure. The preview has no invented `NodeId`; the real figure retains its original ID in its owning chapter.

## Links, assets, and CSS

The renderer builds a complete `NodeId`-to-file index before writing pages. Flow anchors, TOC destinations, footnote calls, and ordinary backlinks become same-file fragments or relative cross-file references. Each page has previous, contents, and next navigation where applicable. Tests resolve every generated local link against the package.

Assets are grouped by SHA-256 bytes. Equal bytes produce one safe `assets/<lowercase-sha256>.<extension>` file even when several `AssetId` values reference them. EPUB filenames and paths never become package paths. Figures use relative asset references appropriate to their page.

`styles/book.css` is reconstructed from the already-resolved typed layout cascade. No EPUB CSS source text enters the package. Pages use a restrictive CSP, load only the local stylesheet and local images, and require no JavaScript or network resource.

## Manifest and integrity

`manifest.json` uses format `flow-html-book-0.1` and contains:

- document ID, optional version, and title;
- canonical hash algorithm, value, and canonicalization profile supplied by the host;
- logical reading order with page kind and chapter `NodeId` where applicable;
- every payload file, media type, byte length, and SHA-256 hash.

The manifest does not hash itself because embedding its own digest would be recursively undefined. This is explicit through `manifestSelfHashExcluded: true`; every other package file is listed and verifiable. Package output and its manifest remain renderer artifacts and do not participate in `flow-c14n-0.1`.

## CLI and safe replacement

```powershell
dotnet run --project src/Flow.Cli -- render book.flow.json --html-book output-directory
Start-Process output-directory/index.html
```

The CLI uses a deterministic 1024×768 logical Flow layout for this command; responsive CSS still adapts the files to mobile and desktop windows. API hosts can supply another valid `LayoutContext`.

Files are first written to a new sibling temporary directory. For a new target, that complete directory is moved into place. An existing target is replaced only when it contains a valid Flow HTML-book manifest and no reparse points; it is renamed to a backup before the complete temporary directory is moved into place, and restored if that move fails. Filesystem roots, existing files, output directories containing the source `.flow.json`, unsafe package paths, and arbitrary existing directories are rejected.

## Current limits

This is a logical multi-file reading package, not EPUB export, MHTML, Web Publication packaging, pagination, print layout, or a Reader application. Chapter page titles and navigation labels are currently minimal. There is no JavaScript, search, persistent progress, theme selector, service worker, browser cache manifest, WCAG certification, or full browser/file-URL audit. Those experience and accessibility refinements belong to the next increment.
