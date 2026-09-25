[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$plan = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'release-plan.json') -Raw -Encoding UTF8 |
    ConvertFrom-Json
$version = [string]$plan.packageVersion
$testRoot = Join-Path $repositoryRoot 'artifacts/release-introduction-test'
$firstPath = Join-Path $testRoot 'first.md'
$secondPath = Join-Path $testRoot 'second.md'
$scriptPath = Join-Path $PSScriptRoot 'new-release-introduction.ps1'

try {
    & $scriptPath -Version $version -OutputPath $firstPath | Out-Null
    & $scriptPath -Version $version -OutputPath $secondPath | Out-Null

    $firstBytes = [System.IO.File]::ReadAllBytes($firstPath)
    $secondBytes = [System.IO.File]::ReadAllBytes($secondPath)
    $firstHash = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($firstBytes))
    $secondHash = [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($secondBytes))
    if ($firstHash -ne $secondHash) {
        throw 'Release introduction generation is not deterministic.'
    }
    if ($firstBytes.Length -ge 3 -and
        $firstBytes[0] -eq 0xEF -and
        $firstBytes[1] -eq 0xBB -and
        $firstBytes[2] -eq 0xBF) {
        throw 'Release introduction must use UTF-8 without BOM.'
    }

    $text = [System.Text.Encoding]::UTF8.GetString($firstBytes)
    if ($text.Contains("`r", [System.StringComparison]::Ordinal)) {
        throw 'Release introduction must use LF line endings.'
    }

    $requiredFragments = @(
        "FlowEngineNet.Tool.$version.nupkg",
        "FlowEngineNet.Setup.$version.pt-BR.win-x64.msi",
        "FlowEngineNet.Portable.$version.win-x64.zip",
        "--version $version",
        '--add-source ./flow-package',
        'flow --language pt-BR --banner menu',
        "blob/v$version/README.md",
        'Experimental prerelease',
        'Pré-release experimental',
        'Installed apps > Flow Engine .NET > Uninstall',
        'Aplicativos instalados > Flow Engine .NET > Desinstalar',
        'not code-signed',
        'não tem assinatura de código',
        'SHA256SUMS',
        'not published to NuGet.org yet'
    )
    foreach ($fragment in $requiredFragments) {
        if (-not $text.Contains($fragment, [System.StringComparison]::Ordinal)) {
            throw "Release introduction is missing required text: $fragment"
        }
    }

    $mismatchVersion = if ($version -eq '0.2.0-alpha.999') {
        '0.2.0-alpha.998'
    }
    else {
        '0.2.0-alpha.999'
    }
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $scriptPath -Version $mismatchVersion 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        throw 'Release introduction accepted a version that differs from the release plan.'
    }

    Write-Output "Release introduction tests passed for $version."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
