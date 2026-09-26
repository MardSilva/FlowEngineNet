# Aplicação do Flow para Windows

[English](../windows-app.md) | Português (Brasil)

`Flow.Windows` é o primeiro host nativo do Flow Engine .NET para Windows. Trata-se de uma aplicação experimental em WinUI 3, não de um Reader. No ciclo `0.2.0-alpha.4`, a interface explicativa passou a usar a mesma fronteira tipada da CLI, sem abrir terminal, iniciar a CLI ou interpretar sua saída.

A página inicial abre um fluxo visual para três operações comuns:

- **Inspecionar EPUB** lê o pacote local e mostra título, autores, idioma, versão EPUB declarada e contagens do manifest e do spine. O arquivo de origem não é convertido nem modificado.
- **Importar EPUB** cria um documento `.flow.json` e, opcionalmente, um relatório de diagnósticos e um livro HTML localizado e sem scripts. O destino inicial fica ao lado do EPUB; depois da inspeção, o nome portátil é derivado do título da publicação.
- **Validar EPUB** importa um EPUB local em memória, executa as verificações estruturais e semânticas e não grava saída. A validação direta de `.flow.json` continua disponível na CLI; o seletor gráfico aceita apenas EPUB enquanto esta interface estiver voltada para esse fluxo.

O idioma dos controles do HTML gerado pode ser escolhido sem alterar o conteúdo do livro. As opções são seleção automática, inglês, português do Brasil e português de Portugal. Texto autoral, títulos e rótulos de navegação nunca são traduzidos silenciosamente.

As fases reais da operação alimentam o indicador de progresso. O cancelamento é cooperativo e remove a saída temporária em caso de interrupção ou falha. Uma saída final existente só é substituída após confirmação. Se houver resíduo reconhecido de uma execução interrompida, a interface também pede uma confirmação separada antes de descartá-lo e refazer o trabalho. O resultado começa com uma mensagem curta; códigos estáveis, locais, contagens e detalhes técnicos ficam disponíveis em uma seção expansível.

Caminhos, metadados e bytes da capa permanecem no processo e no dispositivo local. O fluxo não acessa a rede, não executa scripts da publicação, não carrega recursos externos e não contorna DRM. Fechar a aplicação não deixa uma operação em segundo plano.

As configurações continuam pequenas, locais e reconstruíveis. A gravação é atômica em `%LocalAppData%\FlowEngineNet\settings.json`; se o arquivo estiver ausente, malformado ou acima do limite, a aplicação volta aos padrões seguros. O arquivo pode guardar o caminho da pasta pessoal escolhida, mas não registra o caminho de cada livro, conteúdo da publicação, identidade canônica nem preferências do documento.

## Segurança das saídas

O host gráfico chama diretamente os casos tipados de `Flow.Application`. `Flow.Windows.Shell` cuida apenas das decisões locais próprias de uma aplicação desktop: caminhos absolutos, leitura da origem, sugestão de nome baseada no título, confirmações e gravação atômica. Ele não analisa texto do console nem referencia `Flow.Cli`.

O relatório opcional de diagnósticos usa, por enquanto, o envelope específico da interface `flow-windows-diagnostics-0.1`, com os mesmos diagnósticos da camada de aplicação. O `.flow.json`, a validação semântica, o hash canônico e o pacote HTML vêm do motor compartilhado. Os relatórios completos de metadados, fidelidade, processamento e source map continuam disponíveis na CLI até receberem controles visuais próprios.

## Pasta pessoal e prévia

A página **Pasta pessoal** guarda nas configurações locais um único diretório absoluto escolhido pelo utilizador. Ela procura recursivamente até 500 arquivos EPUB, ignora reparse points, ordena os caminhos de forma determinística e processa um livro por vez. O índice em memória reúne caminhos de origem, metadados básicos, capas seguras e diagnósticos apenas enquanto a aplicação está aberta. Nada disso entra no `FlowDocument`, no hash canônico ou nos relatórios de evidência. Atualizar a página reconstrói o índice a partir dos arquivos de origem.

Cada publicação encontrada pode seguir para inspeção, importação, validação ou prévia. A prévia gera um livro HTML temporário pelo mesmo renderer e mantém o hash canônico nos perfis celular, tablet e desktop. Esses perfis mudam apenas o viewport entregue ao renderer. Eles não representam páginas, emulação completa do dispositivo nem um Reader.

O WebView2 recebe o pacote pelo host virtual fixo `flow-preview.local`. A navegação fica restrita a esse host. Novas janelas, downloads, pedidos de permissão e recursos externos são bloqueados. O diretório temporário é removido quando a página fecha ou o perfil muda. Se o processo for encerrado à força, pode restar um diretório reconhecível em dados locais; ele contém apenas a saída local gerada e está registrado nas limitações conhecidas.

## Modo avançado

O modo avançado continua desativado por padrão. Quando é habilitado nas configurações, a página de operações mostra as fases reais, os códigos diagnósticos e uma representação para PowerShell ou Prompt de Comando criada por `FlowCommandDisplayFormatter`. Esse texto nunca é usado para executar a operação. **Copiar comando** envia o texto à área de transferência, **Salvar log** só grava depois da escolha do destino e **Copiar e abrir PowerShell** abre uma janela limpa do PowerShell após a cópia. O comando não é colado nem executado.

## Arquitetura e acessibilidade

A aplicação está dividida em dois projetos:

- `Flow.Windows.Shell` contém configurações neutras, catálogos de texto, estado de navegação e a orquestração visual sobre `Flow.Application`;
- `Flow.Windows` contém a composição WinUI 3 e as views XAML.

O host usa `WinExe`, portanto não abre um terminal junto com a janela. Ele não referencia nem executa `Flow.Cli`. Aplicações externas só abrem por ação explícita, como abrir o PowerShell no modo avançado ou um link oficial no navegador. A prévia local restrita usa WebView2, como explicado acima. O `NavigationView` se adapta à largura disponível, os destinos principais podem ser alcançados pelo teclado, os títulos expõem níveis de acessibilidade e os controles têm nomes ou descrições úteis para tecnologias assistivas. Os recursos de tema incluem alto contraste, e o logo muda conforme o tema claro ou escuro.

A janela preserva um mínimo de 1000 × 660 unidades lógicas. O host converte esse valor conforme o DPI do monitor, por isso a área visual mínima é a mesma em escalas de 100%, 150% e 200%. Em telas menores, o limite é reduzido à área útil do próprio monitor para manter a barra de título e os controles acessíveis.

A página **Como funciona** é um guia dentro da aplicação, não uma cópia do site público. Em larguras maiores, um índice permanece ao lado da explicação escolhida. Quando falta espaço, ele vira um seletor acima do conteúdo. O guia apresenta a separação entre documento, layout e renderização, a identidade canônica, o pipeline do EPUB, o papel de cada formato, a fidelidade e o escopo experimental atual. As perguntas comuns usam seções expansíveis, sem esconder a explicação principal. A escolha das seções funciona pelo teclado, e as duas formas de navegação usam o mesmo conteúdo localizado em inglês e português do Brasil.

O projeto usa o componente WinUI do Microsoft Windows App SDK sob a [licença do Microsoft Windows App SDK](https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE). A dependência fica fixada de forma central e restrita ao host Windows; ela não entra no modelo de documentos nem na CLI multiplataforma.

## Sobre e identidade da distribuição

A página **Sobre**, no rodapé da navegação, mostra a versão pública, a arquitetura do processo e o caráter experimental da aplicação. A versão vem dos metadados do assembly, gerados pela mesma configuração central da CLI. O contrato neutro `FlowWindowsProductIdentity` recebe os metadados da distribuição sem depender da CLI nem do instalador.

O host lê apenas o `VERSION.json` da distribuição adjacente. Quando o manifesto corresponde ao build, a página mostra a revisão de origem e informa se havia alterações ainda não commitadas. Sem metadados, indica um build de desenvolvimento ou avulso; se estiverem malformados, acima do limite ou incompatíveis, a revisão fica indisponível. A página não expõe caminhos de instalação, nomes de máquinas nem metadados de livros. O manifesto também não serve como prova de instalação por MSI ou de confiança no publicador.

A seção expansível de licenças funciona offline. O build monta `Assets/LICENSES.txt` com a licença MIT do Flow e os avisos fornecidos pelos pacotes resolvidos de WinUI, WebView2 e runtime usados pela aplicação gráfica. Quando um pacote fornece apenas uma referência à licença, essa referência é preservada; os textos das licenças não são traduzidos. A publicação self-contained também inclui os avisos fornecidos pelos runtimes .NET resolvidos. Ferramentas usadas apenas no build não aparecem como dependências da aplicação. Os links do projeto, de relatos de problemas e de releases só abrem quando selecionados. Exibir a página não acessa a rede nem consulta atualizações.

## Consulta explícita de atualizações

Em **Sobre**, o botão **Verificar atualizações** consulta as releases oficiais no GitHub. A opção de incluir pré-releases vem marcada nesta aplicação experimental; desmarque-a para consultar apenas versões estáveis. O resultado mostra a versão em execução, a última versão no canal escolhido e um link validado para a release oficial. A aplicação nunca baixa nem instala a nova versão automaticamente.

A aplicação e a CLI compartilham `Flow.Updates`, responsável pela leitura das releases, comparação semântica de versões e transporte HTTP com limites. A CLI mantém a sintaxe dos comandos, o JSON e os códigos de saída. A consulta tem limite de dez segundos, recusa redirecionamentos e aceita até 2 MiB de resposta. Abrir a aplicação ou a página Sobre não inicia a consulta. Cancelamento, falha de rede, timeout, resposta inválida e canal sem releases geram mensagens localizadas, sem afetar as operações com documentos. Sair da página cancela uma consulta em andamento.

A orientação para MSI só aparece quando o diretório da aplicação corresponde ao registro do instalador por utilizador, incluindo o subdiretório `app`. Essa evidência local não comprova assinatura nem integridade. Sem um registro correspondente, a forma de instalação fica sem confirmação. Para atualizar uma instalação MSI, o utilizador precisa abrir a release, escolher o novo MSI e executá-lo. Os testes automatizados usam transportes falsos e não acessam o GitHub.

## Build e execução local

O projeto WinUI é compilado de forma explícita no Windows, enquanto o build comum da solução continua multiplataforma:

```powershell
dotnet restore Flow.sln
dotnet restore .\src\Flow.Windows\Flow.Windows.csproj -p:Platform=x64
dotnet build .\src\Flow.Windows\Flow.Windows.csproj --no-restore -p:Platform=x64
dotnet run --project .\src\Flow.Windows\Flow.Windows.csproj -p:Platform=x64
```

A aplicação pode rodar a partir da saída do build ou do payload combinado self-contained gerado por `eng/build-windows-combined-payload.ps1`. Esse artefato também inclui a CLI portátil sem alterações e um manifesto auditável. O MSI da alpha.4 consome o artefato, instala os dois pontos de entrada por utilizador e cria um único atalho no menu Iniciar para a aplicação gráfica. A CLI não recebe atalho e continua disponível pelo comando `flow` num terminal novo.

Os testes automatizados cobrem recuperação e persistência atômica das configurações, descoberta da pasta pessoal, natureza não canônica do índice, navegação, os dois catálogos de idioma, escolha de tema, contratos das operações, preservação da origem, políticas de saída, limpeza após cancelamento, descarte e hash da prévia, escaping dos comandos, validação de EPUB e Flow, estrutura de segurança do XAML, acessibilidade, alto contraste e limites entre projetos. Testes locais de abertura e UI Automation confirmam que o processo cria uma janela nativa responsiva e expõe a página de processamento. A revisão visual nas escalas de 100%, 150% e 200% continua manual, pois testes unitários não comprovam ausência de cortes nem legibilidade física.

A aplicação ainda não tem banco persistente de biblioteca, progresso de leitura, anotações, pesquisa nem paginação de produção. A pasta pessoal e a prévia são conveniências operacionais, não o Flow Reader.
