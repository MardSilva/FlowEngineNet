# The Flow Experiment

This directory is the first end-to-end Flow sample:

- `sample.flow.json` is the canonical semantic source;
- `mobile.html` is rendered at 390 × 844;
- `desktop.html` is rendered at 1600 × 1000.

The source contains five chapters, a table of contents, stable anchors, headings, paragraphs, ordered and unordered lists, a block quote, an embedded SVG figure and caption, a footnote, internal and external links, inline formatting, a code block, and role-specific typography.

Reference evidence for `0.1.0-beta.2`:

```text
Document ID: urn:flow:sample:the-flow-experiment
Hash: SHA-256:F607E8E1EADF7EA07B91E5E25B8B99C9D6205DDBED47CAA4D34A2B66014B27CD
Canonicalization: flow-c14n-0.1
Anchors: 44
```

Both HTML outputs preserve the same document ID, element IDs, internal link targets, canonical source hash, and anchor count. They differ only in renderer state derived from their viewport profiles; this state does not participate in the canonical hash.

Regenerate the files from the repository root:

```powershell
dotnet run --project src/Flow.Cli -- sample samples/SampleBook/sample.flow.json
dotnet run --project src/Flow.Cli -- render samples/SampleBook/sample.flow.json --html samples/SampleBook/mobile.html --width 390 --height 844
dotnet run --project src/Flow.Cli -- render samples/SampleBook/sample.flow.json --html samples/SampleBook/desktop.html --width 1600 --height 1000
```
