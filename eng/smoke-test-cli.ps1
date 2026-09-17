[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$ArtifactsDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'src/Flow.Cli/Flow.Cli.csproj'
if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $repositoryRoot 'artifacts/cli-smoke'
}

$artifactsRoot = [System.IO.Path]::GetFullPath($ArtifactsDirectory)
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$runningOnWindows = $env:OS -eq 'Windows_NT'
$comparison = if ($runningOnWindows) { [System.StringComparison]::OrdinalIgnoreCase } else { [System.StringComparison]::Ordinal }
if (-not $artifactsRoot.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw "The smoke-test artifacts directory must be a child of '$allowedArtifactsRoot'."
}

$packagesDirectory = Join-Path $artifactsRoot 'packages'
$toolDirectory = Join-Path $artifactsRoot 'tool'
$workDirectory = Join-Path $artifactsRoot 'work'
$nugetConfig = Join-Path $artifactsRoot 'NuGet.Config'

if (Test-Path -LiteralPath $artifactsRoot) {
    Remove-Item -LiteralPath $artifactsRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $packagesDirectory, $toolDirectory, $workDirectory -Force | Out-Null

$packageId = (& dotnet msbuild $projectPath -nologo -getProperty:PackageId).Trim()
$packageVersion = (& dotnet msbuild $projectPath -nologo -getProperty:PackageVersion).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($packageId) -or [string]::IsNullOrWhiteSpace($packageVersion)) {
    throw 'Could not resolve the CLI package identity from MSBuild.'
}

& dotnet pack $projectPath --configuration $Configuration --no-restore --output $packagesDirectory
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet pack failed.'
}

$packagePath = Join-Path $packagesDirectory "$packageId.$packageVersion.nupkg"
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    throw "Expected package was not produced: $packagePath"
}

$escapedPackagesDirectory = [System.Security.SecurityElement]::Escape($packagesDirectory)
$nugetXml = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="flow-local-smoke" value="$escapedPackagesDirectory" />
  </packageSources>
</configuration>
"@
[System.IO.File]::WriteAllText($nugetConfig, $nugetXml, [System.Text.UTF8Encoding]::new($false))

$installed = $false
try {
    & dotnet tool install $packageId --tool-path $toolDirectory --version $packageVersion --configfile $nugetConfig
    if ($LASTEXITCODE -ne 0) {
        throw 'Local dotnet tool installation failed.'
    }

    $installed = $true
    $flowExecutable = if ($runningOnWindows) {
        Join-Path $toolDirectory 'flow.exe'
    } else {
        Join-Path $toolDirectory 'flow'
    }
    if (-not (Test-Path -LiteralPath $flowExecutable -PathType Leaf)) {
        throw "Installed flow launcher was not found: $flowExecutable"
    }

    function Invoke-InstalledFlow {
        param([Parameter(Mandatory)][string[]]$Arguments)

        $captured = & $flowExecutable @Arguments 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Installed Flow command failed ($LASTEXITCODE): flow $($Arguments -join ' ')`n$($captured -join [Environment]::NewLine)"
        }

        return $captured -join "`n"
    }

    $help = Invoke-InstalledFlow -Arguments @('help')
    if ($help.IndexOf('Flow Engine .NET 0.2.0-alpha.1', [System.StringComparison]::Ordinal) -lt 0) {
        throw 'Installed CLI help did not report the expected experimental version.'
    }

    $portugueseHelp = Invoke-InstalledFlow -Arguments @('--language', 'pt-BR', 'help')
    if ($portugueseHelp.IndexOf('Comandos:', [System.StringComparison]::Ordinal) -lt 0) {
        throw 'The installed tool did not load the pt-BR resource catalog.'
    }

    $documentPath = Join-Path $workDirectory 'sample.flow.json'
    $htmlPath = Join-Path $workDirectory 'sample.html'
    Invoke-InstalledFlow -Arguments @('sample', $documentPath) | Out-Null
    $inspection = Invoke-InstalledFlow -Arguments @('inspect', $documentPath)
    $validation = Invoke-InstalledFlow -Arguments @('validate', $documentPath)
    $hash = Invoke-InstalledFlow -Arguments @('hash', $documentPath)
    Invoke-InstalledFlow -Arguments @('render', $documentPath, '--html', $htmlPath, '--width', '390', '--height', '844') | Out-Null

    if (-not (Test-Path -LiteralPath $documentPath -PathType Leaf) -or
        -not (Test-Path -LiteralPath $htmlPath -PathType Leaf)) {
        throw 'The installed tool did not create the expected sample and HTML files.'
    }

    if ($inspection.IndexOf('The Flow Experiment', [System.StringComparison]::Ordinal) -lt 0 -or
        $validation.IndexOf('Valid:', [System.StringComparison]::Ordinal) -lt 0 -or
        $hash.IndexOf('flow-c14n-0.1', [System.StringComparison]::Ordinal) -lt 0) {
        throw 'One or more installed CLI smoke checks returned unexpected output.'
    }

    $html = [System.IO.File]::ReadAllText($htmlPath)
    if ($html.IndexOf('<!DOCTYPE html>', [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw 'The installed tool did not render standalone HTML5.'
    }

    $result = [ordered]@{
        format = 'flow-cli-distribution-smoke-0.1'
        packageId = $packageId
        packageVersion = $packageVersion
        configuration = $Configuration
        commands = @('help', 'help-pt-BR', 'sample', 'inspect', 'validate', 'hash', 'render-html')
        status = 'passed'
    }
    $resultJson = $result | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText(
        (Join-Path $artifactsRoot 'smoke-result.json'),
        ($resultJson.Replace("`r`n", "`n") + "`n"),
        [System.Text.UTF8Encoding]::new($false))

    Write-Host "Flow CLI distribution smoke test passed: $packageId $packageVersion"
}
finally {
    if ($installed) {
        & dotnet tool uninstall $packageId --tool-path $toolDirectory | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw 'Local dotnet tool uninstall failed.'
        }
    }
}
