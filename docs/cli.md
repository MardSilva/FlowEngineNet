# Flow CLI 0.1

The `flow` executable is a deliberately small composition layer over the document serializer, validator, canonicalizer, layout engine, and HTML renderer. Command parsing is implemented independently from command operations and uses no external CLI framework.

## Commands

```text
flow sample [output]
flow inspect <document>
flow validate <document>
flow hash <document>
flow render <document> --html <output> --width <n> --height <n>
```

`sample` writes “The Flow Experiment” to the optional output path or to `sample.flow.json` in the current directory. The command serializes the same typed sample factory used to generate the committed reference document.

`inspect` reports identity, metadata, node and chapter counts, assets, and presentation availability.

`validate` returns exit code `0` for a structurally valid document and `2` when validation diagnostics contain errors. Parsing, file, and command errors return `1`.

`hash` reports the algorithm, uppercase hexadecimal document hash, and canonicalization profile.

`render` resolves `ReadingMode.Flow` for the requested logical viewport, writes standalone HTML, and reports the source identity, canonical hash, anchor count, and selected viewport category. Width and height use invariant-culture positive numbers.

During development, invoke the executable through the project:

```powershell
dotnet run --project src/Flow.Cli -- sample sample.flow.json
dotnet run --project src/Flow.Cli -- validate sample.flow.json
dotnet run --project src/Flow.Cli -- render sample.flow.json --html sample.html --width 390 --height 844
```

## Separation and testing

`CliCommandParser` converts argument tokens into typed command records without performing I/O. `CliOperations` performs serialization, validation, hashing, layout, and rendering. `FlowCliApplication` owns exit-code and error handling. This separation allows integration tests to execute the real operations with isolated temporary files and captured text streams without starting a subprocess.
