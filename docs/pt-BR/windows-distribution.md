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

## Limite atual

Esta fundação ainda não publica executável self-contained, MSI, MSIX, aplicação gráfica ou associação de arquivos. Ela também não instala nem remove nada da máquina do desenvolvedor. A criação do instalador, os testes de upgrade do pacote instalado e a integração com a GitHub Release continuam como incrementos separados.
