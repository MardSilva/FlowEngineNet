# Flow CLI 0.2

The `flow` executable is a deliberately small composition layer over the document serializer, validator, canonicalizer, layout engine, and HTML renderer. Command parsing is implemented independently from command operations and uses no external CLI framework.

The help output identifies the CLI and `.flow.json` representation as experimental. Stable `FLOWCLI_*` prefixes distinguish command, option, value, and operation failures. Exit code `0` means success, `1` means command/input/I/O failure, and `2` means the document parsed but failed semantic validation.

## Commands

```text
flow sample [output]
flow import <book.epub> [--output <book.flow.json>]
flow epub-inspect <book.epub> [--json <report.json>]
flow inspect <document>
flow validate <document>
flow hash <document>
flow render <document> --html <output> --width <n> --height <n>
```

`sample` writes “The Flow Experiment” to the optional output path or to `sample.flow.json` in the current directory. The command serializes the same typed sample factory used to generate the committed reference document.

`import` accepts a security-bounded EPUB and writes a deterministic `.flow.json`. Without `--output`, the destination is the source path with `.epub` replaced by `.flow.json`. Warnings and errors use stable `EPUB*`/`FLOWCLI_*` diagnostics. The command validates before writing, uses an atomic replacement, reports the imported identity and hash, and never leaves a partial output after a failed import.

`epub-inspect` reads only the EPUB container and OPF structure. It reports the EPUB 2/3 family, principal metadata, manifest, linear/non-linear spine, navigation documents, archive sizes, resource types, missing resources, unsupported resources, and diagnostics without producing a `FlowDocument`. `--json` writes the deterministic `flow-epub-inspection-0.1` report even when the publication is invalid enough to return exit code `1`.

`inspect` reports identity, metadata, total nodes, chapters, sections, paragraphs, figures, footnotes, assets, addressable anchors, and presentation availability.

`validate` returns exit code `0` for a structurally valid document and `2` when validation diagnostics contain errors. Parsing, file, and command errors return `1`.

`hash` reports the algorithm, uppercase hexadecimal document hash, and canonicalization profile.

`render` resolves `ReadingMode.Flow` for the requested logical viewport, writes standalone HTML, and reports the source identity, canonical hash, anchor count, and selected viewport category. Width and height use invariant-culture positive numbers.

The CLI imports the currently supported EPUB subset and reads `.flow.json`; it does not yet expose signing, verification, reader-preference switches, PDF, or paged output. Successful `sample`, `import`, and `render` operations replace their target files.

During development, invoke the executable through the project:

```powershell
dotnet run --project src/Flow.Cli -- sample sample.flow.json
dotnet run --project src/Flow.Cli -- epub-inspect book.epub --json inspection.json
dotnet run --project src/Flow.Cli -- import book.epub --output book.flow.json
dotnet run --project src/Flow.Cli -- validate sample.flow.json
dotnet run --project src/Flow.Cli -- render sample.flow.json --html sample.html --width 390 --height 844
```

## Separation and testing

`CliCommandParser` converts argument tokens into typed command records without performing I/O. `CliOperations` performs EPUB inspection/import, serialization, validation, hashing, layout, and rendering. `FlowCliApplication` owns exit-code and error handling. This separation allows integration tests to execute the real operations with isolated temporary files and captured text streams without starting a subprocess.
