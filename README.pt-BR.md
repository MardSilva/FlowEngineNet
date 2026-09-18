# Flow Engine .NET

[English](README.md) | [Português (Brasil)](README.pt-BR.md)

> **Experimental:** o Flow 0.x é um projeto de pesquisa, não um formato padronizado. Ainda não deve ser usado para arquivos permanentes, documentos jurídicos ou cargas de produção.

O Flow Engine .NET pesquisa um modelo de documento no qual identidade canônica e conteúdo semântico não dependem de viewport, tipografia, layout, paginação ou tecnologia de renderização.

```text
Documento != Layout != Renderização
```

O projeto não substitui PDF nem EPUB. A versão atual testa a base do motor e um fluxo EPUB limitado; ainda não oferece Reader, editor ou cadeia editorial pronta para produção.

## Marco atual

O marco em desenvolvimento é o **`0.2.0-alpha.1`**, dedicado a fluxos EPUB reais.

Já estão implementados:

- modelo semântico imutável com nós de bloco e inline;
- identificadores tipados e estáveis, âncoras, índice de nós e validação estrutural;
- apresentação e tipografia opcionais, com preferências de leitura e limites do renderer;
- serialização experimental `.flow.json` determinística;
- canonicalização versionada (`flow-c14n-0.2`, com escritor legado 0.1) e hash SHA-256;
- prova de conceito local de assinatura RSA-PSS-SHA256 sobre os bytes canônicos;
- importação EPUB limitada por regras de segurança, com inspeção EPUB 2/3, navegação, metadados, spine, imagens, notas, tabelas, SVG passivo, MathML restrito, internacionalização e um subconjunto CSS tipado;
- rastreabilidade determinística entre recursos e fragmentos EPUB e os IDs do Flow;
- layout adaptativo independente de renderer para `ReadingMode.Flow`;
- HTML5 standalone e pacote HTML sem scripts, com sumário, capítulos, notas locais, temas e interface gerada em `en`, `pt-PT` ou `pt-BR`;
- CLI leve para importar, inspecionar, validar, calcular hashes, renderizar e executar os fluxos privados de corpus e revisão;
- relatórios tipados de fidelidade, metadados de origem, processamento e source map;
- testes unitários, de integração, segurança, regressão e conformidade.

Ainda não estão implementados renderers além de HTML, exportação EPUB, paginação física, modos `Paged` e `Print`, Reader, editor, PKI, DRM ou serviços de publicação. `ReadingMode.Paged` e `ReadingMode.Print` são contratos reservados e lançam `NotSupportedException`.

## Pipeline

```text
Importador / .flow.json
          |
          v
    FlowDocument        identidade canônica e conteúdo semântico
          |
          v
   LayoutDocument       estilos resolvidos e intenções adaptativas
          |
          v
      Renderer          adaptador: HTML, UI nativa, impressão etc.
```

Apresentação, preferências do leitor, viewport, decisões de layout e saída renderizada não participam do hash canônico.

## Flow ao lado de EPUB e PDF

O EPUB continua sendo um formato de distribuição válido. Um futuro Reader poderá abri-lo diretamente e usar o Flow internamente para normalizar semântica, validar referências, manter IDs estáveis e renderizar sem prender o conteúdo ao layout original. A pasta HTML gerada hoje é uma saída de demonstração e publicação, não o armazenamento planejado para o Reader.

O PDF exige uma linha de pesquisa separada. A primeira etapa deverá tratar PDFs digitais, com evidência tipada da extração e diagnósticos de perda. OCR, documentos digitalizados, paginação física e renderização PDF de produção não fazem parte do ciclo EPUB.

## Requisitos e validação local

- .NET SDK selecionado por `global.json`;
- Windows, Linux ou macOS compatível com esse SDK;
- PowerShell 7 para os scripts multiplataforma de distribuição.

```powershell
dotnet restore Flow.sln
dotnet format Flow.sln --verify-no-changes
dotnet build Flow.sln --no-restore
dotnet test Flow.sln --no-build
pwsh -NoProfile -File ./eng/test-documentation.ps1
```

O CI executa restore, formatação, documentação, build, testes e smoke test da CLI em Windows e Ubuntu. Os artefatos continuam locais; nada é publicado no NuGet ou em GitHub Releases.

## Início rápido da CLI

Comandos, opções, campos JSON e códigos diagnósticos permanecem em inglês. A opção global `--language` muda apenas os textos para pessoas. Os catálogos atuais são `en-US` e `pt-BR`; sem a opção, a CLI usa `en-US`.

```powershell
dotnet run --project src/Flow.Cli -- --language pt-BR --banner help
dotnet run --project src/Flow.Cli -- --language en-US --no-color help
```

As opções globais aparecem antes do comando. `--banner` exibe o cabeçalho ASCII opcional e não altera arquivos gerados, bytes canônicos ou hashes.

### Importar um EPUB

```powershell
Set-Location 'C:\caminho\para\FlowEngineNet'
$epub = 'C:\livros\Meu livro.epub'

dotnet run --project .\src\Flow.Cli -- --language pt-BR import $epub
```

Sem `--output`, o `.flow.json` é criado ao lado do EPUB. O nome portátil vem do título do livro; o título original, com acentos, permanece nos metadados. Para guardar as evidências não canônicas:

```powershell
dotnet run --project .\src\Flow.Cli -- import $epub `
  --diagnostics-json '.\diagnostics.json' `
  --fidelity-report '.\fidelity.json' `
  --metadata-json '.\metadata.json' `
  --processing-json '.\processing.json' `
  --source-map-json '.\source-map.json'
```

Depois, gere um livro HTML:

```powershell
$flow = 'C:\livros\meu_livro.flow.json'
dotnet run --project .\src\Flow.Cli -- render $flow --html-book 'C:\livros\meu_livro_book' --width 390 --height 844 --ui-language pt-BR
```

O pacote contém `index.html`, sumário, capítulos, notas locais, assets deduplicados, CSS compartilhado e manifesto de integridade. É uma saída descompactada e pode ocupar bastante espaço.

### Inspecionar, validar e renderizar

```powershell
dotnet run --project src/Flow.Cli -- epub-inspect caminho/para/livro.epub --json epub-report.json
dotnet run --project src/Flow.Cli -- inspect samples/SampleBook/sample.flow.json
dotnet run --project src/Flow.Cli -- validate samples/SampleBook/sample.flow.json
dotnet run --project src/Flow.Cli -- hash samples/SampleBook/sample.flow.json
dotnet run --project src/Flow.Cli -- render samples/SampleBook/sample.flow.json --html sample.html --width 390 --height 844
```

### Inventário privado de EPUBs

`epub-inventory` encontra arquivos `.epub` recursivamente sem convertê-los, copiá-los ou renomeá-los. O catálogo guarda IDs neutros, SHA-256, tamanhos, idiomas, contagens estruturais e diagnósticos. Não guarda caminhos físicos, nomes de arquivo, títulos, autores nem conteúdo do livro.

Mantenha o catálogo fora do repositório e do diretório dos EPUBs:

```powershell
dotnet run --project src/Flow.Cli -- `
  --language pt-BR --banner `
  epub-inventory C:\livros `
  --output C:\flow-local\epub-inventory.json `
  --repository-root C:\src\FlowEngineNet
```

Para qualificar duas vezes cada candidato elegível:

```powershell
dotnet run --project src/Flow.Cli -- `
  --language pt-BR `
  epub-inventory-qualify C:\livros `
  --report C:\flow-local\epub-qualification.json `
  --repository-root C:\src\FlowEngineNet `
  --legal-use --drm-free
```

O relatório registra fases, hashes, contagens semânticas, fidelidade, diagnósticos e determinismo. Publicações protegidas, corrompidas ou inadequadas ficam marcadas como ignoradas. As declarações `--legal-use` e `--drm-free` são responsabilidade do utilizador; o Flow não comprova direitos nem remove DRM.

Classifique o relatório sem reabrir os livros:

```powershell
$qualification = 'C:\flow-local\epub-qualification.json'
$qualificationHash = (Get-FileHash $qualification -Algorithm SHA256).Hash

dotnet run --project src/Flow.Cli -- `
  epub-inventory-matrix $qualification `
  --qualification-sha256 $qualificationHash `
  --output C:\flow-local\epub-difference-matrix.json `
  --repository-root C:\src\FlowEngineNet
```

A matriz distingue aprovação, aproximação, conteúdo não suportado, perda, defeito da fonte, erro do Flow e revisão humana pendente. A classificação automática nunca conclui a revisão humana.

### Gate e revisão assistida de uma publicação

```powershell
$epub = 'C:\livros\livro.epub'
$sha256 = (Get-FileHash $epub -Algorithm SHA256).Hash
$repository = (Get-Location).Path

dotnet run --project .\src\Flow.Cli -- epub-qualify $epub `
  --candidate-id candidate-001 --sha256 $sha256 `
  --report 'C:\flow-local\candidate-001-gate.json' `
  --repository-root $repository --legal-use --drm-free

dotnet run --project .\src\Flow.Cli -- epub-review $epub `
  --candidate-id candidate-001 --sha256 $sha256 `
  --output 'C:\flow-local\candidate-001-review' `
  --repository-root $repository --legal-use --drm-free `
  --ui-language pt-BR
```

O gate repete a cadeia automática. A revisão gera HTML mobile e desktop, amostras do começo, meio e fim, atalhos por recurso e um checklist separado. O Flow não aprova decisões humanas automaticamente.

Operações longas usam locks por destino e IDs de execução. `--force` autoriza a substituição segura de uma saída reconhecida; `--resume` reinicia uma transação interrompida a partir da fonte verificada. `execution-status` consulta o estado, e `execution-clean` exige o UUID exato para remover somente artefatos reconhecidos.

## Evidência atual do ciclo EPUB

O corpus privado mais recente encontrou 15 entradas. As 14 publicações elegíveis passaram duas execuções determinísticas sem unidade de fidelidade perdida; uma entrada corrompida foi ignorada. A matriz tem 13 candidatos com aproximações, um caso de acessibilidade que exige revisão humana e uma origem corrompida. Não há conteúdo classificado como não suportado nem erro do Flow nessa execução.

As 24 estruturas `colgroup` válidas encontradas em quatro candidatos agora usam `EPUB080`, em vez do diagnóstico de tabela malformada `EPUB058`. Linhas e células permanecem intactas e os hashes canônicos não mudaram. Esses resultados são evidência de regressão para um conjunto privado, não certificação EPUB ou garantia de abrir qualquer livro.

## Pacote local da CLI

O projeto pode empacotar `Flow.Cli` como a ferramenta .NET local `FlowEngineNet.Tool`, cujo comando é `flow`. Ainda não há publicação no NuGet.

```powershell
pwsh -NoProfile -File .\eng\smoke-test-cli.ps1 -Configuration Release
pwsh -NoProfile -File .\eng\build-release-artifacts.ps1 -Configuration Release
pwsh -NoProfile -File .\eng\invoke-release-dry-run.ps1
```

O fluxo cria um candidato normalizado, checksums SHA-256, SBOM CycloneDX, manifesto e proveniência local não assinada. O CI instala o mesmo `.nupkg` em Ubuntu e Windows. Isso não constitui release pública, assinatura ou instalador do sistema operacional.

## Documentação

- [Arquitetura](docs/pt-BR/architecture.md)
- [Modelo de documento](docs/pt-BR/flow-document-model.md)
- [Layout adaptativo](docs/pt-BR/adaptive-layout.md)
- [Canonicalização e hash](docs/pt-BR/canonicalization.md)
- [Assinaturas experimentais](docs/pt-BR/signatures.md)
- [Renderer HTML standalone](docs/pt-BR/html-renderer.md)
- [Pacote de livro HTML](docs/pt-BR/html-book-package.md)
- [CLI](docs/pt-BR/cli.md)
- [Importação EPUB](docs/pt-BR/epub-import.md)
- [Relatório de fidelidade](docs/pt-BR/epub-fidelity.md)
- [Desempenho, progresso e cancelamento](docs/pt-BR/epub-performance.md)
- [Corpus EPUB](docs/pt-BR/epub-corpus.md)
- [Matriz pública do corpus](docs/pt-BR/epub-corpus-matrix.md)
- [Perfil de conformidade](docs/pt-BR/conformance.md)
- [Limitações atuais](docs/pt-BR/known-limitations.md)
- [Limitações resolvidas ou reduzidas](docs/pt-BR/resolved-limitations.md)
- [Revisão da versão 0.1](docs/pt-BR/0.1-release-review.md)
- [Revisão do ciclo EPUB 0.2](docs/pt-BR/0.2-epub-cycle-review.md)
- [Artefatos locais de release](docs/pt-BR/release-artifacts.md)
- [Resultados da pesquisa](docs/pt-BR/research-findings.md)
- [Política de tradução](docs/pt-BR/translation-policy.md)
- [Roteiro](docs/pt-BR/roadmap.md)

O inglês é a fonte canônica. O manifesto de tradução registra o hash da fonte revisada e faz o CI falhar quando uma tradução fica desatualizada.

## Versionamento e compatibilidade

O projeto está em `0.x`. APIs, JSON, canonicalização, diagnósticos e renderização ainda podem mudar. Perfis versionados preservam as fronteiras já publicadas, mas não existe garantia geral de compatibilidade, migração ou interoperabilidade independente.

## Licença

Consulte [LICENSE](LICENSE). EPUBs privados ou licenciados usados em testes locais não fazem parte do repositório.
