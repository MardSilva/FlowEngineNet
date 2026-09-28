[CmdletBinding()]
param(
    [string]$Version,

    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$planPath = Join-Path $PSScriptRoot 'release-plan.json'
$plan = Get-Content -LiteralPath $planPath -Raw -Encoding UTF8 | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = [string]$plan.packageVersion
}

if ($Version -ne [string]$plan.packageVersion) {
    throw "Release notes version '$Version' does not match the planned version '$($plan.packageVersion)'."
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-(alpha|beta|rc)\.\d+)?$') {
    throw "Release notes require a supported semantic version: $Version"
}
$isPrerelease = $Version.Contains('-')
if (($isPrerelease -and $plan.channel -ne 'prerelease') -or
    (-not $isPrerelease -and $plan.channel -ne 'stable')) {
    throw 'Release version and channel must agree.'
}
$releaseNotice = if ($isPrerelease) {
    '> Experimental prerelease. The command line, `.flow.json` format and 0.x APIs may still change.' + "`n> `n" +
    '> Pré-release experimental. A linha de comando, o formato `.flow.json` e as APIs 0.x ainda podem mudar.'
} else {
    '> Stable release for the documented local EPUB workflow. The Flow format and cryptographic signatures remain experimental; PDF import and a complete reader are not included.' + "`n> `n" +
    '> Versão estável para o fluxo local de EPUB documentado. O formato Flow e as assinaturas criptográficas continuam experimentais; importação de PDF e um leitor completo não estão incluídos.'
}

$notesTemplate = @'
{RELEASE_NOTICE}

[English documentation](https://github.com/MardSilva/FlowEngineNet/blob/v{VERSION}/README.md) | [Documentação em português](https://github.com/MardSilva/FlowEngineNet/blob/v{VERSION}/README.pt-BR.md)

## Install and try / Instalar e testar

### Windows installer / Instalador para Windows

Download `FlowEngineNet.Setup.{VERSION}.en-US.win-x64.msi` or `FlowEngineNet.Setup.{VERSION}.pt-BR.win-x64.msi` and open it. The per-user installer does not require the .NET SDK or administrator privileges. This MSI is not code-signed, so Windows may show an unknown-publisher or SmartScreen warning. Verify the SHA-256 before continuing.

Baixe `FlowEngineNet.Setup.{VERSION}.pt-BR.win-x64.msi` ou `FlowEngineNet.Setup.{VERSION}.en-US.win-x64.msi` e abra o arquivo. O instalador por utilizador não exige o SDK do .NET nem privilégios de administrador. Este MSI não tem assinatura de código; por isso, o Windows pode mostrar um aviso de publicador desconhecido ou do SmartScreen. Confira o SHA-256 antes de continuar.

To remove Flow, use **Settings > Apps > Installed apps > Flow Engine .NET > Uninstall**. Removal deletes only installer-owned application files and registration. Books, Flow documents, preferences, reports and exports are preserved.

Para remover o Flow, use **Configurações > Aplicativos > Aplicativos instalados > Flow Engine .NET > Desinstalar**. A remoção apaga somente os arquivos e registros pertencentes ao instalador. Livros, documentos Flow, preferências, relatórios e exportações são preservados.

### Portable Windows ZIP / ZIP portátil para Windows

`FlowEngineNet.Portable.{VERSION}.win-x64.zip` is self-contained. Extract it and run `flow.exe`; it changes neither Installed Apps nor the user `PATH`.

`FlowEngineNet.Portable.{VERSION}.win-x64.zip` é self-contained. Extraia o arquivo e execute `flow.exe`; ele não altera Aplicativos instalados nem o `PATH` do utilizador.

### Local .NET tool / Ferramenta .NET local

This option requires the .NET 10 SDK. Download `FlowEngineNet.Tool.{VERSION}.nupkg` from the assets below, place it in an otherwise empty directory named `flow-package`, and run from its parent directory:

Esta opção exige o SDK do .NET 10. Baixe `FlowEngineNet.Tool.{VERSION}.nupkg` nos arquivos abaixo, coloque-o em um diretório vazio chamado `flow-package` e execute a partir do diretório pai:

```text
dotnet tool install --global FlowEngineNet.Tool --version {VERSION} --add-source ./flow-package
flow --language pt-BR --banner menu
```

The .NET tool package is not published to NuGet.org yet. The local directory passed to `--add-source` is required.

O pacote da ferramenta .NET ainda não está publicado no NuGet.org. O diretório local informado em `--add-source` é obrigatório.

## What is included / O que está incluído

- Semantic Flow documents with stable IDs, deterministic JSON, canonical hashes and experimental signatures.
- EPUB 2/3 inspection and import, validation, fidelity evidence, responsive HTML books and an interactive CLI.
- Documentação bilíngue em inglês e português brasileiro.

PDF import, a production reader, DRM handling and production pagination are not part of this build.

Importação de PDF, Reader de produção, tratamento de DRM e paginação de produção não fazem parte desta versão.

## Verify downloads / Verificar downloads

Use `SHA256SUMS` to verify the attached files. The CycloneDX SBOM, in-toto statement, release manifest and validation evidence describe how the package was produced and checked. These records are not a code-signing certificate or a supported-release guarantee.

Use `SHA256SUMS` para verificar os arquivos anexados. O SBOM CycloneDX, a declaração in-toto, o manifesto e as evidências registram como o pacote foi produzido e validado. Esses registros não são um certificado de assinatura de código nem uma garantia de suporte.
'@
$notes = $notesTemplate.Replace('{VERSION}', $Version, [System.StringComparison]::Ordinal).Replace('{RELEASE_NOTICE}', $releaseNotice, [System.StringComparison]::Ordinal)

$normalized = $notes.Replace("`r`n", "`n").Replace("`r", "`n").TrimEnd() + "`n"
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Write-Output $normalized.TrimEnd("`n")
    exit 0
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
$repositoryPrefix = $repositoryRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
$comparison = if ($env:OS -eq 'Windows_NT') {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
if (-not $resolvedOutput.StartsWith($repositoryPrefix, $comparison)) {
    throw "Release notes output must remain inside the repository: $resolvedOutput"
}

$parent = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$temporaryPath = $resolvedOutput + '.tmp-' + [Guid]::NewGuid().ToString('N')
try {
    [System.IO.File]::WriteAllText(
        $temporaryPath,
        $normalized,
        [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryPath -Destination $resolvedOutput -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}

Write-Output "Release introduction written to $resolvedOutput"
