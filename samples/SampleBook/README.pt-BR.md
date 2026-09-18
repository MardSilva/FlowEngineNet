# The Flow Experiment

[English](README.md) | Português (Brasil)

`sample.flow.json` contém o livro de referência "The Flow Experiment": cinco capítulos, sumário, headings, parágrafos, listas, citação, figura, legenda, nota, links, formatação inline, código e tipografia por papel.

Execute na raiz do repositório:

```powershell
dotnet run --project src/Flow.Cli -- sample
dotnet run --project src/Flow.Cli -- inspect samples/SampleBook/sample.flow.json
dotnet run --project src/Flow.Cli -- validate samples/SampleBook/sample.flow.json
dotnet run --project src/Flow.Cli -- hash samples/SampleBook/sample.flow.json
dotnet run --project src/Flow.Cli -- render samples/SampleBook/sample.flow.json --html samples/SampleBook/mobile.html --width 390 --height 844
dotnet run --project src/Flow.Cli -- render samples/SampleBook/sample.flow.json --html samples/SampleBook/desktop.html --width 1600 --height 1000
```

As duas saídas mantêm identidade, hash e âncoras, embora o layout se adapte ao viewport. Os HTMLs versionados servem como demonstração; não são uma aplicação Reader.
