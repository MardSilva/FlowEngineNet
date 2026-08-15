# The Flow Experiment

“The Flow Experiment” is the first complete demonstration of the current Flow pipeline. It starts with one semantic document, validates and hashes it, creates layouts for two viewports, and renders two standalone HTML files.

The interesting result is not simply that two HTML files exist. It is that both presentations preserve the same document identity, semantic nodes, anchors, links, and canonical hash while adapting to different reading surfaces.

```text
sample.flow.json
      |
      +--> validate --> structural diagnostics
      |
      +--> hash -----> canonical SHA-256
      |
      +--> layout 390 x 844 -----> mobile.html
      |
      +--> layout 1600 x 1000 ---> desktop.html
```

## What is in this directory?

| File | Purpose |
| --- | --- |
| `sample.flow.json` | Canonical semantic source for the book |
| `mobile.html` | Standalone rendering produced for a 390 × 844 viewport |
| `desktop.html` | Standalone rendering produced for a 1600 × 1000 viewport |

The source contains five chapters, a table of contents, stable anchors, headings, paragraphs, ordered and unordered lists, a block quote, an embedded SVG figure and caption, a footnote, internal and external links, inline formatting, a code block, and role-specific typography.

The HTML files require no web server and no asset directory. Their CSS and figure bytes are embedded directly in each document.

## Fastest way to see the result

If you only want to inspect the result, clone the repository and open the two committed HTML files in a browser:

```powershell
Start-Process samples/SampleBook/mobile.html
Start-Process samples/SampleBook/desktop.html
```

On macOS, use `open` instead of `Start-Process`. On Linux, use `xdg-open`.

Resize the browser window after opening either file. The generated document includes a mobile safety rule, responsive images, embedded typography rules, and a maximum readable content width.

## Reproduce the experiment from source

### 1. Prerequisites

Install the .NET SDK pinned by the repository's `global.json` file:

```text
.NET SDK 10.0.302
```

Confirm the selected SDK from the repository root:

```powershell
dotnet --version
```

Then restore and build the solution:

```powershell
dotnet restore Flow.sln
dotnet build Flow.sln
```

All commands below assume that the current directory is the repository root.

### 2. Inspect the semantic source

```powershell
dotnet run --project src/Flow.Cli -- inspect samples/SampleBook/sample.flow.json
```

Expected summary:

```text
ID: urn:flow:sample:the-flow-experiment
Version: 1.0
Title: The Flow Experiment
Language: en
Authors: Flow Contributors
Nodes: 44
Chapters: 5
Sections: 1
Paragraphs: 18
Figures: 1
Footnotes: 1
Assets: 1
Anchors: 44
Presentation: yes
```

`inspect` reads the `.flow.json` file through the real Flow serializer. It does not inspect a separate manifest or hard-coded summary.

### 3. Validate the document

```powershell
dotnet run --project src/Flow.Cli -- validate samples/SampleBook/sample.flow.json
```

Expected result:

```text
Valid: no semantic validation errors.
```

Validation checks stable IDs, hierarchy, heading levels, figure assets, footnote references, internal anchors, and table-of-contents destinations. A valid result therefore means more than “the JSON parsed.”

### 4. Compute its canonical hash

```powershell
dotnet run --project src/Flow.Cli -- hash samples/SampleBook/sample.flow.json
```

Expected evidence for `0.1.0-rc.1`:

```text
Hash: SHA-256:F607E8E1EADF7EA07B91E5E25B8B99C9D6205DDBED47CAA4D34A2B66014B27CD
Canonicalization: flow-c14n-0.1
```

The hash covers identity, canonical metadata, semantic content, structure, asset references, and asset bytes. It does not cover viewport, resolved layout, reader theme, or renderer output.

### 5. Render the mobile version

```powershell
dotnet run --project src/Flow.Cli -- render samples/SampleBook/sample.flow.json --html samples/SampleBook/mobile.html --width 390 --height 844
```

Relevant output:

```text
Document ID: urn:flow:sample:the-flow-experiment
Hash: SHA-256:F607E8E1EADF7EA07B91E5E25B8B99C9D6205DDBED47CAA4D34A2B66014B27CD
Anchors: 44
Viewport: 390x844 (Small)
```

The small profile uses one column, compact margins, a full available content width, and block figures.

### 6. Render the desktop version

```powershell
dotnet run --project src/Flow.Cli -- render samples/SampleBook/sample.flow.json --html samples/SampleBook/desktop.html --width 1600 --height 1000
```

Relevant output:

```text
Document ID: urn:flow:sample:the-flow-experiment
Hash: SHA-256:F607E8E1EADF7EA07B91E5E25B8B99C9D6205DDBED47CAA4D34A2B66014B27CD
Anchors: 44
Viewport: 1600x1000 (Large)
```

The large profile keeps a maximum readable content width and increases lateral space. Two columns are not enabled by this CLI command, because Flow requires an explicit host decision before using them.

The render commands replace the specified output files. Use different paths if you want to preserve the committed reference artifacts.

## What should you compare?

Open both files and check the following:

1. The table-of-contents links navigate to the same chapter IDs.
2. The footnote reference navigates to the same footnote ID.
3. The figure scales to the available width and retains its caption.
4. The code block preserves whitespace and remains horizontally usable.
5. Body, chapter, heading, caption, footnote, quotation, TOC, and code typography are resolved independently.
6. Mobile and desktop use different layout constraints without changing their semantic reading order.

The generated HTML uses semantic elements such as `article`, `section`, headings, paragraphs, lists, `blockquote`, `figure`, `figcaption`, `nav`, `pre`, and `code`. It does not expose internal C# class names.

## What is actually preserved?

The committed artifacts and integration tests confirm:

```text
Document ID: urn:flow:sample:the-flow-experiment
Canonical hash: F607E8E1EADF7EA07B91E5E25B8B99C9D6205DDBED47CAA4D34A2B66014B27CD
Canonicalization: flow-c14n-0.1
Semantic element IDs: 44 in each HTML file
Internal links: 7 in each HTML file
```

The test suite regenerates the JSON and both HTML files and compares them byte for byte with the committed artifacts. It also compares every HTML `id` and internal `href` between mobile and desktop.

## Try changing it

Work on a copy so the reference fixture remains unchanged:

```powershell
Copy-Item samples/SampleBook/sample.flow.json experiment.flow.json
```

Useful experiments:

- Change a font family or theme under `presentation`, run `flow hash` again, and observe that the canonical hash remains unchanged.
- Change a sentence under `content`, run `flow hash` again, and observe that the hash changes.
- Render the same source at widths such as 480, 900, and 1400 to observe the `Small`, `Medium`, and `Large` profiles.
- Break a TOC target or duplicate a node ID and run `flow validate` to see a semantic diagnostic.

These experiments demonstrate the boundary at the center of the project: presentation may adapt, but canonical content and identity do not silently change with it.

## Generate a fresh sample elsewhere

The `sample` command creates the book from the typed `SampleBookFactory` and serializes it through the production Flow JSON serializer:

```powershell
dotnet run --project src/Flow.Cli -- sample my-sample.flow.json
```

If no output is supplied, the command writes `sample.flow.json` in the current directory:

```powershell
dotnet run --project src/Flow.Cli -- sample
```

The command replaces the target file if it already exists.

## Current limits

This sample proves the current `.flow.json` → validation/hash → adaptive layout → standalone HTML pipeline. It does not yet prove:

- broad EPUB interoperability or semantic-loss measurements across real publisher publications;
- editing and saving from a reader application;
- paged or print layout;
- font embedding—the named fonts fall back to locally available fonts;
- signature trust/transport, provenance, DRM, or publishing services.

The library now has a limited, diagnostic-first EPUB importer tested with a project-generated EPUB. It is not connected to this CLI pipeline and has not been validated against a broad real-world corpus, so the complete “EPUB in → inspect/hash/render” user workflow is still absent.

## Related documentation

- [CLI commands](../../docs/cli.md)
- [Architecture](../../docs/architecture.md)
- [Semantic document model](../../docs/flow-document-model.md)
- [Adaptive layout](../../docs/adaptive-layout.md)
- [Standalone HTML renderer](../../docs/html-renderer.md)
- [Canonicalization and hashing](../../docs/canonicalization.md)
- [EPUB import prototype](../../docs/epub-import.md)
- [Known limitations](../../docs/known-limitations.md)
