# CLI do Flow 0.2

[English](../cli.md) | Português (Brasil)

`Flow.Cli` compõe os serviços do motor sem framework pesado. Parsing de comandos e operações são separados para permitir testes. Durante o desenvolvimento, use `dotnet run --project src/Flow.Cli --`; o pacote local instala o comando `flow`.

## Instalação local no perfil do usuário

Já é possível gerar no Windows um MSI local por utilizador, ainda sem assinatura e fora da release pública. Consulte [distribuição para Windows](windows-distribution.md). Para desenvolvimento multiplataforma, a versão atual também pode ser instalada como ferramenta .NET no seu perfil:

```powershell
$packageDirectory = Join-Path $PWD "artifacts/local-tool"
dotnet pack src/Flow.Cli/Flow.Cli.csproj --configuration Release --output $packageDirectory
dotnet tool install --global FlowEngineNet.Tool --add-source $packageDirectory --ignore-failed-sources
flow --language pt-BR --banner menu
```

Se a mesma versão de desenvolvimento já estiver instalada, remova-a antes de instalar o pacote recompilado. Isso é necessário porque a versão do pacote pode continuar igual entre builds locais:

```powershell
dotnet tool uninstall --global FlowEngineNet.Tool
dotnet tool install --global FlowEngineNet.Tool --add-source $packageDirectory --ignore-failed-sources
```

O pacote instalado é o build local; esses comandos não o publicam no NuGet. Para remover o comando depois, execute `dotnet tool uninstall --global FlowEngineNet.Tool`. Se `flow` não for reconhecido logo após a instalação, abra outro terminal e confirme se o diretório padrão das ferramentas .NET está no `PATH`.

## Opções globais

```text
flow [--language <en-US|pt-BR>] [--banner] [--no-color] [--plain] <comando>
```

`--language <en-US|pt-BR>` escolhe os textos para pessoas. Sem a opção, a CLI usa `en-US`. Comandos, opções, caminhos, campos serializados e códigos diagnósticos não mudam. Em `pt-BR`, a CLI mostra um resumo traduzido e conserva o detalhe técnico original.

`--banner` exibe um cabeçalho ASCII no estilo FIGlet. Em um terminal interativo compatível, a CLI pode usar uma apresentação Rich que consulta novamente a largura disponível entre as telas. Saídas redirecionadas ou capturadas, terminais sem ANSI ou controle de cursor e hosts sem suporte confiável a Unicode recebem texto simples e determinístico. `--no-color` e `NO_COLOR` retiram as cores, mas mantêm o espaçamento e a hierarquia do modo Rich.

`--plain` seleciona expressamente a apresentação simples para leitores de tela, terminais limitados e captura previsível. Esse modo desativa cores, animações, controle de cursor e caracteres de desenho. A precedência é: `--plain`; fallback automático de segurança; modo Rich sem cores por `--no-color` ou `NO_COLOR`; modo Rich com cores. A apresentação escolhida não altera arquivos, bytes canônicos, relatórios nem hashes.

## Menu interativo e assistentes guiados

`flow menu` começa com uma apresentação curta do projeto, do fluxo entre EPUB e Flow e do estado experimental do formato atual. O painel azul aparece uma vez por sessão, antes do catálogo navegável pelo teclado. Use as setas e Enter para escolher uma categoria ou um comando. Cada prévia informa finalidade, sintaxe direta, entradas obrigatórias, efeitos nos arquivos e observações de segurança. `Voltar` retorna ao nível anterior, `Sair` fecha o menu e Esc cancela a navegação. Ctrl+C conserva o comportamento de cancelamento e o código de saída `130`.

As prévias de `import` e `epub-inspect` também oferecem um assistente. Os dois confirmam se a origem é um arquivo `.epub` local, comum e existente, mostram um resumo completo e pedem confirmação antes de executar. Na importação, você pode manter o destino `.flow.json` derivado do título ou informar outro caminho. Também pode escolher relatórios de diagnóstico, fidelidade, metadados de origem, processamento e mapa de origem. A inspeção pode gravar seu relatório JSON determinístico. Os relatórios opcionais recebem nomes previsíveis ao lado do EPUB.

As prévias de `inspect`, `validate` e `hash` aceitam um arquivo `.flow.json` local e comum. Esses fluxos apenas leem o documento. A saída normal continua visível e pode ser copiada, inclusive o hash e o perfil canônico. Quando a validação encontra um erro semântico, o painel de resultado preserva o código de saída `2`.

O assistente de `render` gera HTML standalone com largura e altura lógicas informadas pelo usuário ou um pacote de livro HTML com interface em `auto`, `en`, `pt-PT` ou `pt-BR`. Antes da execução, o resumo mostra os caminhos normalizados, informa se o destino existe e registra o tipo de saída, o viewport ou o idioma escolhido. A confirmação não ignora as regras de substituição do comando direto.

Todos os assistentes chamam os mesmos comandos tipados e as mesmas operações da CLI direta. Relatórios de importação e pacotes HTML book mantêm a escrita atômica já existente; o HTML standalone conserva o comportamento atual do renderer direto. No fim, o menu mostra o código de saída e as saídas realmente gravadas. Uma entrada vazia ou `:cancel` volta sem criar arquivos, e Esc cancela as listas. Ctrl+C retorna `130`. Se stdin terminar inesperadamente, a CLI retorna `FLOWCLI_INTERACTIVE_INPUT_UNAVAILABLE` sem presumir nenhuma escolha. O estado do terminal é restaurado depois de sucesso, falha ou cancelamento.

O menu é opcional e exige um terminal Rich interativo. Streams redirecionados, `--plain`, controle de cursor indisponível ou um perfil de terminal inseguro são recusados com `FLOWCLI_MENU_REQUIRES_INTERACTIVE`. `flow help` apresenta o mesmo catálogo, e cada fluxo do menu informa o comando direto antes da execução. Para leitores de tela, automação e terminais limitados, use esse comando direto.

Os assistentes de corpus, gate e manutenção de execuções ficam reunidos em **Avançado**. Antes de pedir qualquer valor, cada fluxo explica sua finalidade e o custo esperado do processamento. O resumo final mostra os caminhos normalizados de origem, evidências e destinos. A execução usa o mesmo comando tipado do modo direto, portanto IDs de candidatos, valores SHA-256 esperados, `--force`, `--resume`, locks e IDs de execução conservam o significado atual.

As operações que processam publicações privadas ou informadas individualmente pedem as declarações de `--legal-use` e `--drm-free` em duas etapas separadas. Cancelar é a primeira opção nas duas telas, e a confirmação final da execução não vale como declaração. O gate continua sendo uma evidência técnica, não uma certificação; candidatos ignorados ou com falha e revisões humanas pendentes permanecem visíveis nos relatórios normais. No caso de `execution-clean`, o ID hexadecimal de 32 caracteres precisa ser digitado uma segunda vez antes da confirmação final. A limpeza continua sujeita à política existente e só atua sobre resíduos reconhecidos daquele destino.

Durante uma operação interativa, o menu mostra a fase tipada atual numa área de progresso localizada. O contrato informa uma unidade real, correspondente ao comando, desde o início até a conclusão. Ele não estima páginas, bytes, tempo restante nem percentuais internos que a operação não consiga medir. A saída normal do comando fica em memória enquanto a área de progresso está ativa, evitando escritas concorrentes no mesmo terminal. Os prompts só voltam depois que essa área é encerrada.

O painel final mostra estado em texto e símbolo, código de saída, duração medida e as quantidades de arquivos, linhas de resultado e linhas de diagnóstico. Caminhos gravados, saída normal e diagnósticos ficam em seções separadas. A cor apenas reforça o estado. Códigos e mensagens de diagnóstico são preservados sem alteração. O contrato também possui uma apresentação Plain, com linhas comuns de início e conclusão, sem ANSI nem controle de cursor; os comandos diretos e a automação redirecionada mantêm a saída estável já existente.

Os testes automatizados cobrem largura estreita e variável, ausência de ANSI, cursor ou Unicode, writers redirecionados, `NO_COLOR`, `--no-color`, `--plain`, cancelamento, fim da entrada, acentos, texto bidirecional e caracteres parecidos com markup. O workflow do repositório valida o pacote no Windows e no Linux. Windows PowerShell e Prompt são suportados quando o SDK/runtime selecionado e o launcher de ferramenta global funcionam nesses hosts. Isso descreve o que foi testado; o projeto ainda não passou por auditoria externa de acessibilidade nem por uma avaliação formal com leitores de tela.

## Ajuda específica por comando

`flow help` organiza o catálogo em cinco grupos fixos: Início, Livros EPUB, Documentos Flow, Corpus e qualidade e Manutenção. A visão geral mostra apenas o nome e um resumo de cada comando, portanto as assinaturas longas não prejudicam a leitura nem em terminais com 80 colunas. Em um terminal interativo estreito, o resumo fica abaixo do comando; quando há espaço, ambos aparecem lado a lado. O modo simples preserva a mesma informação sem sequências ANSI, bordas ou controle do cursor.

Para consultar todos os detalhes tipados de um comando, use uma destas formas:

```text
flow help <comando>
flow <comando> --help
```

A ajuda detalhada informa finalidade, uso, argumentos posicionais, opções obrigatórias e opcionais, exemplos seguros, efeitos nos arquivos, observações de segurança e códigos de saída relevantes. Todos os comandos públicos têm textos em en-US e pt-BR. A ajuda é resolvida antes dos argumentos obrigatórios, por isso a operação descrita não é iniciada e nenhuma saída é criada. Um comando de ajuda desconhecido retorna `FLOWCLI_UNKNOWN_HELP_COMMAND` e código `1`.

## Comandos

```text
flow menu
flow sample [output]
flow import <book.epub> [--output <book.flow.json>] [--diagnostics-json <report.json>] [--fidelity-report <fidelity.json>]
flow epub-inspect <book.epub> [--json <report.json>]
flow epub-inventory <directory> --output <catalog.json> --repository-root <absolute-directory> [--force]
flow epub-inventory-qualify <directory> --report <report.json> --repository-root <absolute-directory> --legal-use --drm-free [--force] [--resume]
flow epub-inventory-matrix <qualification.json> --qualification-sha256 <hash> --output <matrix.json> --repository-root <absolute-directory> [--force] [--resume]
flow epub-inventory-review <directory> --qualification <report.json> --qualification-sha256 <hash> --output <absolute-directory> --repository-root <absolute-directory> --legal-use --drm-free [--ui-language <auto|en|pt-PT|pt-BR>] [--force] [--resume]
flow corpus <manifest.json> --repository-root <directory> --report <report.json> [--external-root <directory>] [--baseline <baseline.json>] [--force] [--resume]
flow epub-qualify <book.epub> --candidate-id <id> --sha256 <hash> --report <report.json> --repository-root <absolute-directory> --legal-use --drm-free [--repetitions <n>] [--include-environment] [--force] [--resume]
flow epub-review <book.epub> --candidate-id <id> --sha256 <hash> --output <absolute-directory> --repository-root <absolute-directory> --legal-use --drm-free [--ui-language <auto|en|pt-PT|pt-BR>] [--force] [--resume]
flow execution-status <destination> [--json <report.json>] [--force]
flow execution-clean <destination> --execution-id <32-hex-id>
flow inspect <document>
flow validate <document>
flow hash <document>
flow render <document> --html <output> --width <n> --height <n>
flow render <document> --html-book <output-directory> [--ui-language <auto|en|pt-PT|pt-BR>]
```

`sample` grava "The Flow Experiment". `inspect`, `validate` e `hash` leem `.flow.json`. `validate` retorna `0` para documento válido e `2` para erros de validação; falhas de comando ou leitura retornam `1`.

`import` processa um EPUB dentro dos limites de segurança e grava `.flow.json` por escrita atômica. Sem `--output`, cria ao lado do EPUB um nome `snake_case` derivado do título, limitado a 96 caracteres. O título original permanece nos metadados. `Ctrl+C` retorna `130`, conserva a saída final anterior e remove o temporário.

`--diagnostics-json`, `--fidelity-report`, `--metadata-json`, `--processing-json` e `--source-map-json` gravam evidências separadas e não canônicas. JSON usa UTF-8 sem BOM, LF e ordem determinística. `EPUB077` registra diferença única de maiúsculas no caminho; `EPUB078`, imagem local usada só por CSS; `EPUB079`, alternativa textual recuperada; `EPUB080`, metadados válidos de coluna não representados. `EPUB041` permanece para imagem sem alternativa explícita, e `EPUB058` para recuperação malformada de tabela.

`epub-inspect` lê container e OPF sem produzir `FlowDocument`. `epub-inventory` descobre um diretório privado, calcula hashes, deduplica payloads e exclui identidade editorial e caminhos do catálogo. `epub-inventory-qualify` executa duas vezes inspeção, importação, validação, fidelidade, round-trip, integridade, layouts e verificação HTML para cada candidato elegível. Entradas protegidas, corrompidas ou inadequadas ficam marcadas como ignoradas.

`epub-inventory-matrix` classifica um relatório já produzido após verificar seu SHA-256. `epub-inventory-review` gera pacotes de revisão para todos os candidatos qualificados sem persistir nomes ou caminhos das publicações.

`corpus` executa um manifesto versionado e pode comparar um baseline revisado; nunca aceita automaticamente um baseline novo. `epub-qualify` executa o gate de uma publicação ao menos duas vezes. `epub-review` cria os pacotes mobile/desktop, `review.html`, checklist e manifesto. `--legal-use` e `--drm-free` são declarações do chamador, não verificação jurídica do Flow.

`render --html` usa o viewport solicitado. `render --html-book` cria o pacote `flow-html-book-0.1`, com sumário, capítulos, assets e manifesto. `--ui-language` muda somente a interface gerada; texto do livro e títulos do TOC nunca são traduzidos.

### Substituição e retomada

Operações de corpus e revisão recusam substituir uma saída final sem `--force`. A nova saída só é promovida depois de completa e validada. Um lock oculto por destino impede dois escritores, registra UUID aleatório e estado, mas não guarda caminho nem conteúdo do livro.

Após interrupção, `--resume` reconhece o lock, remove apenas artefatos transacionais conhecidos e reinicia pela fonte verificada. Saída parcial não é reaproveitada. `execution-status` consulta o estado sem escrever. `execution-clean` exige o UUID exato, recusa um escritor ativo e nunca remove arquivos alheios.

## Empacotamento local

```powershell
pwsh -NoProfile -File ./eng/smoke-test-cli.ps1 -Configuration Release
```

O smoke test empacota `FlowEngineNet.Tool`, instala em diretório isolado e valida a ajuda Plain geral e específica, o catálogo `pt-BR`, a ausência de ANSI em saída redirecionada, as assemblies do Spectre.Console, a recusa segura do menu sem terminal interativo e o fluxo `sample`/`inspect`/`validate`/`hash`/`render`. No fim, desinstala a ferramenta sem tocar na instalação global. Ainda não há publicação, assinatura ou instalador.

## Separação e testes

O parser só cria comandos tipados; `CliOperations` coordena serviços. Testes cobrem parsing, códigos de saída, localização, escrita atômica, locks, retomada, importação, renderização e empacotamento. A CLI ainda não expõe PDF, paginação, assinatura/verificação ou aplicação Reader.
