# Fundação da distribuição para Windows

[English](../windows-distribution.md) | Português (Brasil)

A distribuição do Flow para Windows passa a ter uma identidade de produto estável antes da criação do primeiro instalador. A versão semântica continua definida em `Directory.Build.props`; `eng/Flow.WindowsProduct.props` acrescenta somente a identidade e a política de instalação específicas do Windows. Assim, não surge uma segunda fonte para a versão pública.

O alvo inicial é `win-x64`, com instalação por utilizador no diretório de programas dos dados locais da aplicação. O código permanente de upgrade é `{C412C622-FA2F-400C-88EE-BA5D4A573F7D}`. O Windows Installer usa a versão numérica separada `0.2.3` para ordenar o pacote `0.2.0-alpha.3`. As próximas versões do instalador devem aumentar esse número, mesmo quando o SemVer público mudar entre os canais alpha, beta, release candidate e estável.

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

## MSI por utilizador

O repositório também gera pacotes MSI em `en-US` e `pt-BR`. Eles são alternativas de idioma da mesma versão, não pacotes para instalar lado a lado. Os dois instalam o mesmo payload self-contained em `%LocalAppData%\Programs\FlowEngineNet`, acrescentam esse diretório ao `PATH` do utilizador atual e registram o Flow em Aplicativos instalados e Programas e Recursos. A instalação não deve pedir privilégios de administrador. Depois de instalar, abra um terminal novo antes de executar `flow` pelo nome.

Gere as duas variantes com:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/build-windows-installer.ps1
```

A saída fica em `artifacts/windows-installer/` e contém os dois arquivos MSI, checksums SHA-256, um manifesto do instalador e um documento CycloneDX. Uma saída existente só é substituída com `-Force`. O MSI é experimental e não está assinado; o Windows pode mostrar um aviso de publicador desconhecido ou do SmartScreen.

Para instalar sem interface:

```powershell
msiexec.exe /i ".\FlowEngineNet.Setup.0.2.0-alpha.3.pt-BR.win-x64.msi" /qn /norestart
```

O próprio Windows Installer cuida de reparo, upgrade e remoção. O reparo pode ser solicitado com `msiexec.exe /fa <código-do-produto> /qn /norestart`; a remoção normal deve ser feita em Aplicativos instalados ou Programas e Recursos. As duas variantes de idioma desta versão compartilham um `ProductCode` fixo. Cada versão pública futura do MSI deverá receber outro `ProductCode`, conservar o `UpgradeCode` permanente e aumentar a versão numérica do instalador. Dessa forma, o major upgrade substitui a versão anterior e impede a instalação de uma versão mais baixa sobre outra mais recente.

A desinstalação remove `flow.exe`, licença, documento de versão, registro do produto e somente o segmento de `PATH` criado pelo MSI. Livros, diretórios configurados, documentos `.flow.json`, relatórios, exportações e preferências ficam intactos. Esta versão não cria atalho no menu Iniciar nem associa arquivos `.epub` ou `.flow.json`.

O teste que altera temporariamente o Windows deve ser executado apenas nesse sistema:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/test-windows-installer.ps1
```

O teste gera identidades aleatórias para produto, upgrade, componente, Registro e diretório. Ele nunca usa o código do produto de produção nem uma instalação pessoal existente. Dentro dessa identidade isolada, verifica instalação limpa, execução sem .NET no `PATH`, reparo, major upgrade, recusa de downgrade e desinstalação. O diretório de teste contém espaços e Unicode, e o `PATH` original do utilizador precisa ser restaurado sem alterações.

O instalador usa WiX Toolset 4.0.6, fixado no projeto e licenciado sob MS-RL. WiX 6 e 7 não foram adotados porque a distribuição atual dessas versões acrescenta uma EULA de Open Source Maintenance Fee. O WiX participa apenas do build e não é instalado com o Flow. Versão, licença e papel de ferramenta excluída da distribuição aparecem no SBOM do instalador. O MSI usa os mecanismos padrão de registro e major upgrade documentados pela [Microsoft](https://learn.microsoft.com/pt-br/windows/win32/msi/configuring-add-remove-programs-with-windows-installer) e pelo [WiX](https://docs.firegiant.com/wix/schema/wxs/majorupgrade/).

O MSI deve ser gerado uma única vez para cada candidato a release e identificado pelo checksum. Compilar de novo a mesma revisão não produz necessariamente bytes idênticos, pois o Windows Installer usa metadados de identidade próprios para cada pacote gerado. O ZIP portátil continua sendo o artefato Windows reproduzível byte a byte. O manifesto do MSI registra a identidade fixa do produto, a revisão do código e a versão do payload usados no instalador.

## Limite atual

O ZIP portátil e o MSI ainda são artefatos locais de desenvolvimento, sem assinatura, e não foram anexados a uma GitHub Release. Ainda não existem MSIX, aplicação gráfica ou associação de arquivos. A integração automatizada com a release, a assinatura de código e a consulta de atualização continuam como incrementos separados.
