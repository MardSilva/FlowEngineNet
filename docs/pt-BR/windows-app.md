# Aplicação do Flow para Windows

[English](../windows-app.md) | Português (Brasil)

`Flow.Windows` é o primeiro host nativo do Flow Engine .NET para Windows. Trata-se de uma aplicação experimental em WinUI 3, não de um Reader. No ciclo `0.2.0-alpha.4`, a interface explicativa passou a usar a mesma fronteira tipada da CLI, sem abrir terminal, iniciar a CLI ou interpretar sua saída.

A página inicial abre um fluxo visual para três operações comuns:

- **Inspecionar EPUB** lê o pacote local e mostra título, autores, idioma, versão EPUB declarada e contagens do manifest e do spine. O arquivo de origem não é convertido nem modificado.
- **Importar EPUB** cria um documento `.flow.json` e, opcionalmente, um relatório de diagnósticos e um livro HTML localizado e sem scripts. O destino inicial fica ao lado do EPUB; depois da inspeção, o nome portátil é derivado do título da publicação.
- **Validar** aceita um EPUB local ou `.flow.json`, executa em memória as verificações aplicáveis e não grava saída.

O idioma dos controles do HTML gerado pode ser escolhido sem alterar o conteúdo do livro. As opções são seleção automática, inglês, português do Brasil e português de Portugal. Texto autoral, títulos e rótulos de navegação nunca são traduzidos silenciosamente.

As fases reais da operação alimentam o indicador de progresso. O cancelamento é cooperativo e remove a saída temporária em caso de interrupção ou falha. Uma saída final existente só é substituída após confirmação. Se houver resíduo reconhecido de uma execução interrompida, a interface também pede uma confirmação separada antes de descartá-lo e refazer o trabalho. O resultado começa com uma mensagem curta; códigos estáveis, locais, contagens e detalhes técnicos ficam disponíveis em uma seção expansível.

Caminhos, metadados e bytes da capa permanecem no processo e no dispositivo local. O fluxo não acessa a rede, não executa scripts da publicação, não carrega recursos externos e não contorna DRM. Fechar a aplicação não deixa uma operação em segundo plano.

As configurações continuam pequenas, locais e reconstruíveis. A gravação é atômica em `%LocalAppData%\FlowEngineNet\settings.json`; se o arquivo estiver ausente, malformado ou acima do limite, a aplicação volta aos padrões seguros. Esse arquivo não guarda caminhos de livros, conteúdo, identidade canônica nem preferências do documento.

## Segurança das saídas

O host gráfico chama diretamente os casos tipados de `Flow.Application`. `Flow.Windows.Shell` cuida apenas das decisões locais próprias de uma aplicação desktop: caminhos absolutos, leitura da origem, sugestão de nome baseada no título, confirmações e gravação atômica. Ele não analisa texto do console nem referencia `Flow.Cli`.

O relatório opcional de diagnósticos usa, por enquanto, o envelope específico da interface `flow-windows-diagnostics-0.1`, com os mesmos diagnósticos da camada de aplicação. O `.flow.json`, a validação semântica, o hash canônico e o pacote HTML vêm do motor compartilhado. Os relatórios completos de metadados, fidelidade, processamento e source map continuam disponíveis na CLI até receberem controles visuais próprios.

## Arquitetura e acessibilidade

A aplicação está dividida em dois projetos:

- `Flow.Windows.Shell` contém configurações neutras, catálogos de texto, estado de navegação e a orquestração visual sobre `Flow.Application`;
- `Flow.Windows` contém a composição WinUI 3 e as views XAML.

O host usa `WinExe`, portanto não abre um terminal junto com a janela. Ele não referencia `Flow.Cli`, não inicia subprocessos, não usa WebView2 e não acessa a rede. O `NavigationView` se adapta à largura disponível, os destinos principais podem ser alcançados pelo teclado, os títulos expõem níveis de acessibilidade e os controles têm nomes ou descrições úteis para tecnologias assistivas. Os recursos de tema incluem alto contraste, e o logo muda conforme o tema claro ou escuro.

O projeto usa o componente WinUI do Microsoft Windows App SDK sob a [licença do Microsoft Windows App SDK](https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE). A dependência fica fixada de forma central e restrita ao host Windows; ela não entra no modelo de documentos nem na CLI multiplataforma.

## Build e execução local

O projeto WinUI é compilado de forma explícita no Windows, enquanto o build comum da solução continua multiplataforma:

```powershell
dotnet restore Flow.sln
dotnet build .\src\Flow.Windows\Flow.Windows.csproj --no-restore -p:Platform=x64
dotnet run --project .\src\Flow.Windows\Flow.Windows.csproj -p:Platform=x64
```

Por enquanto, a aplicação roda a partir da saída do build. O MSI da alpha.3 ainda instala apenas a CLI. A inclusão da interface gráfica no instalador, o atalho do menu Iniciar, o upgrade da alpha.3 e a remoção dos dois executáveis pertencem à etapa final de integração do instalador Windows.

Os testes automatizados cobrem recuperação e persistência atômica das configurações, navegação, os dois catálogos de idioma, escolha de tema, contratos das operações, preservação da origem, políticas de saída, limpeza após cancelamento, validação de EPUB e Flow, acessibilidade do XAML, alto contraste e limites entre projetos. Testes locais de abertura e UI Automation confirmam que o processo cria uma janela nativa responsiva e expõe a página de processamento. A revisão visual nas escalas de 100%, 150% e 200% continua manual, pois testes unitários não comprovam ausência de cortes nem legibilidade física.

A aplicação ainda não tem pasta pessoal indexada, prévia incorporada, exibição do comando equivalente no modo avançado nem integração ao instalador. Esses pontos pertencem a incrementos separados; esta página operacional não é um Reader.
