# Fundação da distribuição para Windows

[English](../windows-distribution.md) | Português (Brasil)

A distribuição do Flow para Windows passa a ter uma identidade de produto estável antes da criação do primeiro instalador. A versão semântica continua definida em `Directory.Build.props`; `eng/Flow.WindowsProduct.props` acrescenta somente a identidade e a política de instalação específicas do Windows. Assim, não surge uma segunda fonte para a versão pública.

O alvo inicial é `win-x64`, com instalação por utilizador no diretório de programas dos dados locais da aplicação. O código permanente de upgrade é `{C412C622-FA2F-400C-88EE-BA5D4A573F7D}`. O Windows Installer usa a versão numérica separada `0.2.4` para ordenar o pacote `0.2.0-alpha.4`. As próximas versões do instalador devem aumentar esse número, mesmo quando o SemVer público mudar entre os canais alpha, beta, release candidate e estável.

O instalador poderá remover apenas o payload da aplicação, o alias de comando, o registro do instalador e os atalhos do produto. Livros, diretórios configurados, documentos `.flow.json`, preferências, relatórios e exportações pertencem ao utilizador. Upgrades e desinstalações normais devem preservá-los.

## Assets da marca

As três fontes PNG de 512×512 em `assets/branding/source/` foram fornecidas pelo responsável pelo repositório para uso no Flow Engine .NET. Os hashes revisados e as finalidades estão registrados em `assets/branding/brand-assets.json`. Ainda não há uma licença de marca separada. Os arquivos-fonte permanecem inalterados e nunca devem ser substituídos pelos derivados.

O símbolo escuro com destaque azul é usado em superfícies claras. O símbolo claro com destaque azul-claro é usado em superfícies escuras. A variante monocromática fica reservada aos contextos sem cor. Os ícones do produto no Windows usam o símbolo claro sobre uma base azul-marinho opaca para continuar visíveis nos temas claro e escuro do sistema.

Execute o gerador exclusivo para Windows a partir da raiz do repositório:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/New-FlowWindowsBrandAssets.ps1
```

O gerador confere os hashes das fontes, recorta somente o espaço transparente, mantém a proporção e grava assets de 16, 32, 48, 256 e 512 pixels, além de um `.ico` com várias imagens. Ele valida a política de transparência e um contraste mínimo de 4,5:1 para a paleta. O manifesto versionado registra dimensões e SHA-256 de cada derivado. Nenhuma imagem de banner é gerada porque o escopo atual ainda não a utiliza.

O GDI+ é o renderer fixado para essa etapa de engenharia. Por isso, a regeneração é suportada apenas no Windows. Os derivados versionados podem ser consumidos e verificados nos outros sistemas. No Windows, os testes repetem a geração num diretório temporário e exigem igualdade byte a byte; nenhuma biblioteca gráfica de runtime foi adicionada ao motor do Flow.

## Distribuição portátil para win-x64

O primeiro artefato executável para Windows é um ZIP self-contained para `win-x64`. Ele não exige instalação separada do SDK nem do runtime do .NET e não altera o Registro, o `PATH`, o menu Iniciar ou a lista de programas instalados. Basta extrair o arquivo para um diretório local e executar `flow.exe`.

Gere o pacote na raiz do repositório com o PowerShell 7:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/build-windows-portable.ps1
```

A saída é gravada de forma atômica em `artifacts/windows-portable/`. Se o destino já existir, o script recusa a sobrescrita, a menos que seja usado `-Force`. O diretório contém o ZIP versionado, `portable-manifest.json`, um SBOM CycloneDX 1.5 e `SHA256SUMS`. Dentro do ZIP há somente `flow.exe`, `LICENSE.txt` e `VERSION.json`.

O builder publica a aplicação duas vezes e compara todos os arquivos do payload e os dois pacotes normalizados. As entradas do ZIP seguem ordem ordinal, timestamp fixo e atributos externos neutros. O manifesto e o documento de versão registram versão pública, revisão exata do Git, RID, arquitetura, catálogos de idioma e modo de distribuição, sem timestamps nem caminhos locais.

Execute o smoke test do artefato com:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/test-windows-portable.ps1
```

O teste confere checksums e SBOM, extrai o pacote num diretório temporário isolado, retira os caminhos do .NET do ambiente do processo filho e exercita a ajuda em inglês e português do Brasil, a recusa segura do menu com entrada redirecionada, a criação do sample, a inspeção, a validação e a renderização HTML. A saída redirecionada não pode conter escapes ANSI.

### Decisão sobre o executável único

O `PublishSingleFile` ficou habilitado porque a CLI atual, o Spectre.Console e os dois catálogos de idioma funcionam sem assemblies auxiliares. `IncludeNativeLibrariesForSelfExtract` e `IncludeAllContentForSelfExtract` permanecem desabilitados: o pacote não solicita a extração completa do seu conteúdo num diretório temporário. Se uma dependência futura não respeitar essas condições, será preferível adotar um payload com vários arquivos, revisado e explícito, em vez de uma extração oculta.

O documento CycloneDX é criado a partir do grafo de dependências específico do RID produzido pelo publish. Ele cobre os assemblies do Flow, o Spectre.Console e o runtime do .NET incluído no executável. Todos os componentes distribuídos atualmente usam a licença MIT. O SBOM não descreve bibliotecas do sistema Windows, o host de build, dependências exclusivas de teste nem o GitHub Actions.

## Payload combinado da aplicação e da CLI

A integração da alpha.4 começa por um payload local combinado, antes de qualquer alteração no MSI. Ele reúne a aplicação WinUI 3 unpackaged e a CLI single-file existente sob o mesmo contrato de versão. Os dois pontos de entrada são self-contained para `win-x64`. A aplicação gráfica continua com vários arquivos porque o Windows App SDK precisa de bibliotecas nativas, XAML compilado e índices de recursos ao lado do executável.

Gere o payload na raiz do repositório:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/build-windows-combined-payload.ps1
```

A saída é gravada de forma atômica em `artifacts/windows-combined-payload/`. O diretório `payload/app/` contém `Flow.Windows.exe` e seus arquivos de runtime. `payload/cli/flow.exe` vem do build portátil reproduzível da CLI. `LICENSE.txt` e `VERSION.json` valem para os dois pontos de entrada. O arquivo `combined-payload-manifest.json` registra versão pública, revisão do Git, estado da árvore de trabalho, RID, arquitetura, método de empacotamento, idiomas, finalidade, tamanho e SHA-256 de cada arquivo instalável. `SHA256SUMS` também cobre o manifesto separado.

O builder publica a aplicação gráfica duas vezes e exige o mesmo conjunto de arquivos e hashes. Ele mantém somente os diretórios de recursos `en-US` e `pt-BR`, rejeita arquivos de depuração e cache e não copia diretórios de build, testes nem EPUBs privados. A CLI continua vindo de `build-windows-portable.ps1`, portanto este incremento não cria uma segunda política de publicação para ela.

Execute o smoke test combinado com:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/test-windows-combined-payload.ps1
```

O teste confere a cobertura completa do manifesto, os hashes, as versões dos pontos de entrada e a ausência de caminhos privados. Ele executa a CLI sem as indicações do runtime .NET no ambiente do processo filho, abre a aplicação WinUI, espera pela janela nativa principal e fecha somente esse processo. O script não grava no Registro, não altera o `PATH`, não cria atalhos e não muda o estado de instalação. Esse payload combinado é a entrada revisada que o builder do MSI consome.

## MSI por utilizador

O repositório também gera pacotes MSI em `en-US` e `pt-BR`. Eles são alternativas de idioma da mesma versão, não pacotes para instalar lado a lado. Os dois consomem o payload combinado revisado e instalam a aplicação gráfica em `%LocalAppData%\Programs\FlowEngineNet\app`. A CLI continua disponível em `%LocalAppData%\Programs\FlowEngineNet\flow.exe`, preservando o caminho usado na alpha.3 e a entrada no `PATH` do utilizador atual. O Flow aparece em Aplicativos instalados e Programas e Recursos sem exigir privilégios de administrador. Depois de instalar, abra um terminal novo antes de executar `flow` pelo nome.

Gere as duas variantes com:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/build-windows-installer.ps1
```

A saída fica em `artifacts/windows-installer/` e contém os dois arquivos MSI, checksums SHA-256, um manifesto do instalador e um documento CycloneDX. Uma saída existente só é substituída com `-Force`. O MSI é experimental e não está assinado; o Windows pode mostrar um aviso de publicador desconhecido ou do SmartScreen.

Para instalar sem interface:

```powershell
msiexec.exe /i ".\FlowEngineNet.Setup.0.2.0-alpha.4.pt-BR.win-x64.msi" /qn /norestart
```

O próprio Windows Installer cuida de reparo, upgrade e remoção. O reparo completo pode ser solicitado com `msiexec.exe /famus <código-do-produto> /qn /norestart`; a remoção normal deve ser feita em Aplicativos instalados ou Programas e Recursos. As duas variantes de idioma desta versão compartilham um `ProductCode` fixo. Cada versão pública futura do MSI deverá receber outro `ProductCode`, conservar o `UpgradeCode` permanente e aumentar a versão numérica do instalador. Dessa forma, o major upgrade substitui a versão anterior e impede a instalação de uma versão mais baixa sobre outra mais recente.

O instalador cria um único atalho no menu Iniciar, que abre `Flow.Windows.exe` diretamente sem mostrar um terminal. Ele não cria atalho para a CLI, associação de arquivos, serviço, tarefa agendada nem inicialização automática. A desinstalação remove os payloads da aplicação e da CLI, licença, documento de versão, registro do produto, atalho do produto e somente o segmento de `PATH` criado pelo MSI. Livros, diretórios configurados, documentos `.flow.json`, relatórios, exportações HTML e preferências ficam intactos.

O teste que altera temporariamente o Windows deve ser executado apenas nesse sistema:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/test-windows-installer.ps1
```

O teste gera identidades aleatórias para produto, upgrade, componente, Registro e diretório. Ele nunca usa o código do produto de produção nem uma instalação pessoal existente. Dentro dessa identidade isolada, verifica instalação limpa, execução sem .NET no `PATH`, reparo, major upgrade, recusa de downgrade e desinstalação. O diretório de teste contém espaços e Unicode. A conferência final compara as entradas normalizadas e ordenadas do `PATH`: diferenças inofensivas na formatação de separadores ou barras finais são aceitas, mas uma entrada alheia alterada, removida ou reordenada continua reprovando o gate.

O instalador usa WiX Toolset 4.0.6, fixado no projeto e licenciado sob MS-RL. WiX 6 e 7 não foram adotados porque a distribuição atual dessas versões acrescenta uma EULA de Open Source Maintenance Fee. O WiX participa apenas do build e não é instalado com o Flow. Versão, licença e papel de ferramenta excluída da distribuição aparecem no SBOM do instalador. O MSI usa os mecanismos padrão de registro e major upgrade documentados pela [Microsoft](https://learn.microsoft.com/pt-br/windows/win32/msi/configuring-add-remove-programs-with-windows-installer) e pelo [WiX](https://docs.firegiant.com/wix/schema/wxs/majorupgrade/).

O MSI deve ser gerado uma única vez para cada candidato a release e identificado pelo checksum. Compilar de novo a mesma revisão não produz necessariamente bytes idênticos, pois o Windows Installer usa metadados de identidade próprios para cada pacote gerado. O ZIP portátil continua sendo o artefato Windows reproduzível byte a byte. O manifesto do MSI registra a identidade fixa do produto, a revisão do código e a versão do payload usados no instalador.

## Consulta explícita de atualização

Execute `flow update check` para consultar releases estáveis ou `flow update check --channel prerelease` para incluir pré-releases. A rede só é acessada por essa ação explícita e somente para leitura. O comando valida a resposta oficial do GitHub e informa a página da release, o pacote esperado e o SHA-256 publicado, quando disponível. Nenhum instalador é baixado ou executado.

Quando o executável atual corresponde ao diretório registrado pelo MSI por utilizador, o comando identifica esse método e orienta o download e a execução do MSI mais recente. O major upgrade usa o `UpgradeCode` permanente descrito acima. Instalações como ferramenta .NET ou pacote portátil também são reconhecidas e recebem o comando ou procedimento manual adequado. Sem evidência suficiente, o método fica como desconhecido, sem tentativa de adivinhação.

## Gate de release

O workflow manual **Draft release** gera o ZIP portátil no Windows, testa sua execução sem um runtime do .NET no `PATH` e entrega exatamente esse payload ao builder do MSI. Outro runner Windows instala silenciosamente um pacote de teste anterior com identidade isolada, exercita a CLI instalada, faz o reparo e o upgrade, recusa o downgrade, desinstala o produto e confirma a remoção dos arquivos, registros e segmento de `PATH` pertencentes ao instalador. Identidades aleatórias impedem que o teste alcance o código de produto da release ou uma instalação pessoal do Flow.

O gate final exige que `.nupkg`, ZIP e MSI indiquem a mesma versão pública e a mesma revisão do Git. Também compara os três arquivos extraídos do ZIP promovido com os hashes do payload registrados no manifesto do MSI. Os formatos não precisam ter bytes iguais. Evidências do instalador, manifestos de distribuição, SBOMs separados e um arquivo consolidado de checksums do Windows só seguem para o draft depois dessas verificações.

O workflow cria apenas uma GitHub Release em rascunho, depois da confirmação explícita e do environment protegido `draft-release`. Ele não publica no NuGet, não cria uma release pública e não marca uma pré-release como `Latest`.

## Limite atual

`Flow.Application` oferece a fronteira em processo para inspecionar e importar EPUB, validar e calcular o hash de documentos Flow e renderizar HTML. A camada relata progresso, cancelamento, diagnósticos e resultados tipados sem depender da CLI, de um terminal ou da execução de subprocessos. A CLI continua responsável pelos caminhos, pela persistência, pelas confirmações e pelos códigos de saída.

A aplicação WinUI 3 roda como um `WinExe` gráfico separado, explica o Flow offline e executa inspeção, importação e validação local de EPUB pela fronteira tipada compartilhada. Ela também oferece índice reconstruível da pasta pessoal, prévia local restrita e detalhes avançados de comando e log por adesão. A CLI nunca é iniciada; abrir o PowerShell exige uma ação separada e não executa o comando exibido. O MSI da alpha.4 instala esse host e a CLI a partir do mesmo payload combinado e expõe somente a aplicação gráfica no menu Iniciar. Consulte [windows-app.md](windows-app.md).

O ZIP portátil e o MSI continuam sendo artefatos experimentais sem assinatura, embora o workflow confirmado manualmente já possa anexá-los a uma release em rascunho. Por isso, o Windows pode mostrar um aviso de publicador desconhecido ou do SmartScreen. Ainda não existem MSIX nem associação de arquivos. O comando de atualização informa o pacote disponível e seu checksum, mas nunca baixa ou executa o arquivo. Assinatura de código e instalação automatizada continuam como incrementos separados.
