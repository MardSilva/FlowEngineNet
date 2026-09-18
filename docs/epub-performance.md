# EPUB performance, progress, and cancellation

English | [Português (Brasil)](pt-BR/epub-performance.md)

The large-publication pipeline exposes observation and cancellation contracts. These measurements are runtime evidence only: they are excluded from `FlowDocument`, `.flow.json`, canonical bytes, hashes, signatures, and deterministic HTML payloads.

## Typed observations

`EpubImportPhase` identifies archive copy/indexing, container/package/manifest work, spine processing, semantic conversion, finalization, serialization, fidelity analysis, HTML package rendering/writing, and completion. `EpubImportProgress` reports the current phase, completed units, an optional known total, and an optional current resource path.

Known totals are monotonic within each phase. Observer exceptions are isolated from the importer so a progress callback cannot change document bytes, IDs, diagnostics, or ordering.

```csharp
var observations = new Progress<EpubImportProgress>(item =>
{
    if (item.TotalUnits is { } total)
    {
        Console.WriteLine($"{item.Phase}: {item.CompletedUnits}/{total} {item.CurrentResource}");
    }
});

await using var source = File.OpenRead(epubPath);
var result = await new EpubImporter().ImportAsync(source, observations, cancellationToken);
```

`EpubImportMetrics` records total and per-phase duration, archive entries, compressed/uncompressed bytes, imported asset bytes, processed spine documents, semantic nodes, semantic text characters, `.flow.json` size when supplied by the host, HTML file/byte totals when supplied by the host, and a sampled approximate managed-heap peak. The importer value uses `GC.GetTotalMemory(false)` at phase boundaries.

The corpus gate separately samples managed heap and process working set across its complete pipeline. Both values are process-wide, environment-dependent observations rather than precise allocation measurements. They are excluded from deterministic reports and baselines. The gate does not force garbage collection and does not treat measurements from one machine as a universal limit.

The CLI prints an invariant-culture noncanonical summary after `flow import`. `flow render --html-book` reports file count, HTML payload bytes, render duration, and atomic-write duration.

## Cancellation guarantees

Cancellation is checked during archive copy, XML/resource processing, every spine item and converted XHTML document, JSON node/inline/asset writing, fidelity traversal, HTML page/asset construction, and package file writes. `Ctrl+C` cancels the CLI and returns exit code `130` with `FLOWCLI_CANCELLED`, distinct from malformed input.

CLI final outputs use temporary files/directories. Cancellation or failure removes the temporary target and preserves an existing valid final output. Library callers still own their destination streams and should use a transactional stream when partial bytes are unacceptable.

## Memory behavior

- `.flow.json` is written through an LF-normalizing stream rather than first retaining a complete JSON `MemoryStream` and then copying it;
- generated HTML/CSS/manifest byte arrays enter immutable package files without a second full copy;
- package writes use immutable memory directly instead of allocating `ToArray()` for every file.
- corpus JSON round-trips use a private temporary file that is removed after the restored hash is checked;
- corpus mobile and desktop packages are laid out, rendered, verified, summarized, and released sequentially;
- HTML package verification retains path and ID indexes instead of every parsed XML tree at once.

The EPUB ZIP is still buffered in memory to provide bounded, seekable `ZipArchive` processing. During one package phase, XML trees, the semantic document, standalone HTML used by package splitting, and that package's files can coexist temporarily. These are known remaining memory costs, not evidence of a universal supported book size.

## Recommended host limits

The default `EpubImportLimits` remain security limits, not performance promises:

- 2,048 archive entries;
- 128 MiB compressed archive input;
- 16 MiB per ordinary entry;
- 256 MiB total uncompressed data;
- compression ratio 200;
- 16 MiB per image;
- 20,000 x 20,000 maximum image dimensions and 100,000,000 pixels;
- 2 MiB per stylesheet.

Applications with tighter memory budgets should lower these values, apply a cancellation deadline, observe progress, and isolate untrusted imports. Raising a limit requires independent memory/security testing and does not expand Flow's declared interoperability profile.

## Functional tests, optional load tests, and benchmarks

Normal tests remain small and deterministic. Progress/cancellation tests use `Category=Functional`, `Security`, or `Regression`. The optional external load gate is separate:

```powershell
$env:FLOW_EPUB_LOAD_PATH = 'C:\livros\publicacao.epub'
dotnet test tests\Flow.Epub.Tests\Flow.Epub.Tests.csproj --filter 'Category=Load'
```

The path must refer to a legal local copy and is never added to the repository. With no variable, the optional gate performs no external load.

Benchmarks run separately in `Release`, after a warm-up, with the same file copied to a temporary workspace. Record at minimum:

```powershell
dotnet --info
Get-CimInstance Win32_Processor | Select-Object Name,NumberOfCores,NumberOfLogicalProcessors
Get-CimInstance Win32_ComputerSystem | Select-Object TotalPhysicalMemory
Measure-Command { dotnet run -c Release --project .\src\Flow.Cli -- import $epub --output $flow }
```

Record the EPUB SHA-256, Flow hash, CLI metrics, output sizes, runtime, operating system, CPU, memory, configuration, and whether antivirus/background load was present. Do not turn one machine's timing or sampled managed memory into a universal pass threshold.
