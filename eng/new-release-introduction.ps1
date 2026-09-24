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

if ($Version -notmatch '^0\.2\.0-(alpha|beta|rc)\.\d+$') {
    throw "Release notes require a supported 0.2.0 prerelease version: $Version"
}

$notesTemplate = @'
> Experimental prerelease. The command line, `.flow.json` format and all 0.x APIs may still change.
>
> Pré-release experimental. A linha de comando, o formato `.flow.json` e todas as APIs 0.x ainda podem mudar.

[English documentation](https://github.com/MardSilva/FlowEngineNet/blob/v{VERSION}/README.md) | [Documentação em português](https://github.com/MardSilva/FlowEngineNet/blob/v{VERSION}/README.pt-BR.md)

## Install and try / Instalar e testar

This build requires the .NET 10 SDK. Download `FlowEngineNet.Tool.{VERSION}.nupkg` from the assets below, place it in an otherwise empty directory named `flow-package`, and run from its parent directory:

Esta versão exige o SDK do .NET 10. Baixe `FlowEngineNet.Tool.{VERSION}.nupkg` nos arquivos abaixo, coloque-o em um diretório vazio chamado `flow-package` e execute a partir do diretório pai:

```text
dotnet tool install --global FlowEngineNet.Tool --version {VERSION} --add-source ./flow-package
flow --language pt-BR --banner menu
```

The package is not published to NuGet.org yet. The local directory passed to `--add-source` is required.

O pacote ainda não está publicado no NuGet.org. O diretório local informado em `--add-source` é obrigatório.

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
$notes = $notesTemplate.Replace('{VERSION}', $Version, [System.StringComparison]::Ordinal)

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
