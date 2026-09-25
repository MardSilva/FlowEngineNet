[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$ArtifactsDirectory,

    [string]$PackagePath
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

$externalPackagePath = $null
if (-not [string]::IsNullOrWhiteSpace($PackagePath)) {
    $externalPackagePath = [System.IO.Path]::GetFullPath($PackagePath)
    if (-not $externalPackagePath.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, $comparison) -or
        -not (Test-Path -LiteralPath $externalPackagePath -PathType Leaf) -or
        [System.IO.Path]::GetExtension($externalPackagePath) -ne '.nupkg') {
        throw "The supplied package must be an existing .nupkg under '$allowedArtifactsRoot'."
    }
    if ($externalPackagePath.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw 'The supplied package must be outside the smoke-test output directory.'
    }
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

$packagePath = Join-Path $packagesDirectory "$packageId.$packageVersion.nupkg"
if ($null -eq $externalPackagePath) {
    & dotnet pack $projectPath --configuration $Configuration --no-restore --output $packagesDirectory
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet pack failed.'
    }
}
else {
    if ([System.IO.Path]::GetFileName($externalPackagePath) -ne [System.IO.Path]::GetFileName($packagePath)) {
        throw "The supplied package name does not match the project identity: $externalPackagePath"
    }
    Copy-Item -LiteralPath $externalPackagePath -Destination $packagePath
}

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
        param(
            [Parameter(Mandatory)][string[]]$Arguments,
            [int]$ExpectedExitCode = 0
        )

        $captured = & $flowExecutable @Arguments 2>&1
        if ($LASTEXITCODE -ne $ExpectedExitCode) {
            throw "Installed Flow command returned $LASTEXITCODE instead of $ExpectedExitCode`: flow $($Arguments -join ' ')`n$($captured -join [Environment]::NewLine)"
        }

        return $captured -join "`n"
    }

    function Assert-NoAnsi {
        param(
            [Parameter(Mandatory)][string]$Name,
            [Parameter(Mandatory)][string]$Text
        )

        if ($Text.Contains([char]27)) {
            throw "Installed CLI emitted an ANSI escape in redirected $Name output."
        }
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $packageArchive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $packageEntries = @($packageArchive.Entries.FullName)
        foreach ($dependencyFile in @('Spectre.Console.dll', 'Spectre.Console.Ansi.dll')) {
            if (-not ($packageEntries | Where-Object { $_.EndsWith("/$dependencyFile", [System.StringComparison]::Ordinal) })) {
                throw "Installed CLI package does not contain its presentation dependency: $dependencyFile"
            }
        }

        $depsEntry = $packageArchive.Entries |
            Where-Object { $_.FullName.EndsWith('/flow.deps.json', [System.StringComparison]::Ordinal) } |
            Select-Object -First 1
        if ($null -eq $depsEntry) {
            throw 'Installed CLI package does not contain flow.deps.json.'
        }
        $depsReader = [System.IO.StreamReader]::new($depsEntry.Open(), [System.Text.Encoding]::UTF8)
        try {
            $depsText = $depsReader.ReadToEnd()
        }
        finally {
            $depsReader.Dispose()
        }
        foreach ($dependencyIdentity in @('Spectre.Console/0.57.2', 'Spectre.Console.Ansi/0.57.2')) {
            if ($depsText.IndexOf('"' + $dependencyIdentity + '"', [System.StringComparison]::Ordinal) -lt 0) {
                throw "Installed CLI runtime graph does not resolve $dependencyIdentity."
            }
        }
    }
    finally {
        $packageArchive.Dispose()
    }

    # Include the .store directory, which is hidden on Unix.
    $installedSpectreAssembly = Get-ChildItem -LiteralPath $toolDirectory -Filter 'Spectre.Console.dll' -File -Recurse -Force |
        Select-Object -First 1
    if ($null -eq $installedSpectreAssembly) {
        throw 'Spectre.Console.dll was not restored into the isolated tool installation.'
    }
    $spectreIdentity = [System.Reflection.AssemblyName]::GetAssemblyName($installedSpectreAssembly.FullName)
    if ($spectreIdentity.Name -ne 'Spectre.Console') {
        throw "Unexpected Spectre.Console assembly identity: $($spectreIdentity.FullName)"
    }

    $plainHelp = Invoke-InstalledFlow -Arguments @('--plain', 'help')
    Assert-NoAnsi -Name 'plain help' -Text $plainHelp
    if ($plainHelp.IndexOf("Flow Engine .NET $packageVersion", [System.StringComparison]::Ordinal) -lt 0 -or
        $plainHelp.IndexOf('Commands:', [System.StringComparison]::Ordinal) -lt 0) {
        throw 'Installed CLI plain help did not report the expected identity and command catalog.'
    }

    $specificHelp = Invoke-InstalledFlow -Arguments @('--plain', 'help', 'import')
    Assert-NoAnsi -Name 'command help' -Text $specificHelp
    if ($specificHelp.IndexOf('flow import <book.epub>', [System.StringComparison]::Ordinal) -lt 0 -or
        $specificHelp.IndexOf('--fidelity-report', [System.StringComparison]::Ordinal) -lt 0) {
        throw 'Installed CLI command-specific help is incomplete.'
    }

    $updateHelp = Invoke-InstalledFlow -Arguments @('--plain', 'help', 'update')
    Assert-NoAnsi -Name 'update help' -Text $updateHelp
    if ($updateHelp.IndexOf('flow update check', [System.StringComparison]::Ordinal) -lt 0 -or
        $updateHelp.IndexOf('--channel <stable|prerelease>', [System.StringComparison]::Ordinal) -lt 0 -or
        $updateHelp.IndexOf('installs nothing', [System.StringComparison]::Ordinal) -lt 0) {
        throw 'Installed CLI update help is incomplete or does not state its non-installing behavior.'
    }

    $portugueseHelp = Invoke-InstalledFlow -Arguments @('--language', 'pt-BR', '--plain', 'help')
    Assert-NoAnsi -Name 'pt-BR help' -Text $portugueseHelp
    if ($portugueseHelp.IndexOf('Comandos:', [System.StringComparison]::Ordinal) -lt 0 -or
        $portugueseHelp.IndexOf('Livros EPUB', [System.StringComparison]::Ordinal) -lt 0) {
        throw 'The installed tool did not load the pt-BR resource catalog.'
    }

    $menuRefusal = Invoke-InstalledFlow -Arguments @('menu') -ExpectedExitCode 1
    Assert-NoAnsi -Name 'redirected menu refusal' -Text $menuRefusal
    if ($menuRefusal.IndexOf('FLOWCLI_MENU_REQUIRES_INTERACTIVE', [System.StringComparison]::Ordinal) -lt 0 -or
        $menuRefusal.IndexOf('flow help', [System.StringComparison]::Ordinal) -lt 0) {
        throw 'Installed CLI did not refuse the menu safely without an interactive terminal.'
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
        $hash.IndexOf('flow-c14n-0.2', [System.StringComparison]::Ordinal) -lt 0) {
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
        packageSha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        packageOrigin = if ($null -eq $externalPackagePath) { 'built' } else { 'supplied' }
        presentationDependencies = @(
            [ordered]@{ name = 'Spectre.Console'; version = '0.57.2' }
            [ordered]@{ name = 'Spectre.Console.Ansi'; version = '0.57.2' }
        )
        commands = @(
            'help-plain',
            'help-import-plain',
            'help-update-plain',
            'help-pt-BR-plain',
            'menu-redirected-refusal',
            'sample',
            'inspect',
            'validate',
            'hash',
            'render-html'
        )
        status = 'passed'
    }
    $resultJson = $result | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText(
        (Join-Path $artifactsRoot 'smoke-result.json'),
        ($resultJson.Replace("`r`n", "`n") + "`n"),
        [System.Text.UTF8Encoding]::new($false))

    Write-Output "Flow CLI distribution smoke test passed: $packageId $packageVersion"
}
finally {
    if ($installed) {
        & dotnet tool uninstall $packageId --tool-path $toolDirectory | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw 'Local dotnet tool uninstall failed.'
        }
    }
}
