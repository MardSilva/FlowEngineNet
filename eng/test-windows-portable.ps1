[CmdletBinding()]
param(
    [string]$ArtifactsDirectory
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') {
    throw 'The win-x64 portable smoke test must run on Windows.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $allowedArtifactsRoot 'windows-portable'
}
$artifactsRoot = [System.IO.Path]::GetFullPath($ArtifactsDirectory)
if (-not $artifactsRoot.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The portable artifacts directory must be a child of '$allowedArtifactsRoot'."
}

function Get-Sha256Lower {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Invoke-PortableFlow {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [int]$ExpectedExitCode = 0
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Environment['PATH'] = [Environment]::GetFolderPath([Environment+SpecialFolder]::System)
    $startInfo.Environment.Remove('DOTNET_ROOT') | Out-Null
    $startInfo.Environment.Remove('DOTNET_ROOT_X64') | Out-Null
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $output = $process.StandardOutput.ReadToEnd()
    $errorOutput = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne $ExpectedExitCode) {
        throw "Portable Flow returned $($process.ExitCode) instead of $ExpectedExitCode`: flow $($Arguments -join ' ')`n$output`n$errorOutput"
    }
    $combined = $output + $errorOutput
    if ($combined.Contains([char]27)) {
        throw "Portable Flow emitted ANSI in redirected output: flow $($Arguments -join ' ')"
    }
    return $combined
}

$manifestPath = Join-Path $artifactsRoot 'portable-manifest.json'
$checksumsPath = Join-Path $artifactsRoot 'SHA256SUMS'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or
    -not (Test-Path -LiteralPath $checksumsPath -PathType Leaf)) {
    throw 'Portable manifest or SHA256SUMS is missing.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.format -ne 'flow-windows-portable-0.1' -or
    $manifest.runtimeIdentifier -ne 'win-x64' -or
    -not $manifest.selfContained -or
    -not $manifest.singleFile) {
    throw 'Portable manifest identity is invalid.'
}

$checksumEntries = @{}
foreach ($line in Get-Content -LiteralPath $checksumsPath) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') {
        throw "Invalid SHA256SUMS line: $line"
    }
    $checksumEntries[$Matches[2]] = $Matches[1]
}
foreach ($entry in $checksumEntries.GetEnumerator()) {
    $path = Join-Path $artifactsRoot $entry.Key
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Sha256Lower -Path $path) -ne $entry.Value) {
        throw "Portable checksum validation failed: $($entry.Key)"
    }
}

$zipFiles = @(Get-ChildItem -LiteralPath $artifactsRoot -Filter '*.zip' -File)
$sbomFiles = @(Get-ChildItem -LiteralPath $artifactsRoot -Filter '*.cdx.json' -File)
if ($zipFiles.Count -ne 1 -or $sbomFiles.Count -ne 1) {
    throw 'Portable output must contain exactly one ZIP and one CycloneDX document.'
}
$zipPath = $zipFiles[0].FullName
$sbomPath = $sbomFiles[0].FullName
$sbom = Get-Content -LiteralPath $sbomPath -Raw | ConvertFrom-Json
if ($sbom.bomFormat -ne 'CycloneDX' -or $sbom.specVersion -ne '1.5' -or
    @($sbom.components).Count -lt 11 -or
    @($sbom.components | Where-Object name -eq 'Spectre.Console').Count -ne 1 -or
    @($sbom.components | Where-Object name -eq 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64').Count -ne 1) {
    throw 'The portable CycloneDX SBOM is incomplete.'
}

$workRoot = Join-Path $allowedArtifactsRoot ('.flow-windows-portable-smoke-' + [Guid]::NewGuid().ToString('N'))
try {
    [System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $workRoot)
    $executable = Join-Path $workRoot 'flow.exe'
    foreach ($requiredFile in @('flow.exe', 'LICENSE.txt', 'VERSION.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $workRoot $requiredFile) -PathType Leaf)) {
            throw "Portable payload is missing: $requiredFile"
        }
    }
    if (@(Get-ChildItem -LiteralPath $workRoot -File).Count -ne 3) {
        throw 'Portable payload contains unexpected sidecar files.'
    }
    $versionDocument = Get-Content -LiteralPath (Join-Path $workRoot 'VERSION.json') -Raw | ConvertFrom-Json
    if ($versionDocument.version -ne $manifest.version -or
        $versionDocument.sourceRevision -ne $manifest.sourceRevision -or
        $versionDocument.runtimeIdentifier -ne $manifest.runtimeIdentifier) {
        throw 'Portable payload and external manifest do not identify the same build.'
    }
    $executableBytes = [System.IO.File]::ReadAllBytes($executable)
    $asciiExecutable = [System.Text.Encoding]::ASCII.GetString($executableBytes)
    $unicodeExecutable = [System.Text.Encoding]::Unicode.GetString($executableBytes)
    foreach ($privatePathPrefix in @('C:\Users\', '/home/runner/', '/Users/')) {
        if ($asciiExecutable.IndexOf($privatePathPrefix, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $unicodeExecutable.IndexOf($privatePathPrefix, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Portable executable contains a private build path prefix: $privatePathPrefix"
        }
    }

    $englishHelp = Invoke-PortableFlow -Executable $executable -Arguments @('--plain', 'help') -WorkingDirectory $workRoot
    $portugueseHelp = Invoke-PortableFlow -Executable $executable -Arguments @('--language', 'pt-BR', '--plain', 'help') -WorkingDirectory $workRoot
    $defaultHelp = Invoke-PortableFlow -Executable $executable -Arguments @('help') -WorkingDirectory $workRoot
    if ($englishHelp.IndexOf("Flow Engine .NET $($manifest.version)", [System.StringComparison]::Ordinal) -lt 0 -or
        $englishHelp.IndexOf('Commands:', [System.StringComparison]::Ordinal) -lt 0 -or
        $portugueseHelp.IndexOf('Comandos:', [System.StringComparison]::Ordinal) -lt 0 -or
        $portugueseHelp.IndexOf('Livros EPUB', [System.StringComparison]::Ordinal) -lt 0 -or
        $defaultHelp.IndexOf('Flow Engine .NET', [System.StringComparison]::Ordinal) -lt 0) {
        throw 'Portable help, version identity or localized Unicode output is incomplete.'
    }

    $menuRefusal = Invoke-PortableFlow -Executable $executable -Arguments @('menu') -WorkingDirectory $workRoot -ExpectedExitCode 1
    if ($menuRefusal.IndexOf('FLOWCLI_MENU_REQUIRES_INTERACTIVE', [System.StringComparison]::Ordinal) -lt 0) {
        throw 'Portable menu did not refuse redirected input safely.'
    }

    $documentPath = Join-Path $workRoot 'portable-sample.flow.json'
    $htmlPath = Join-Path $workRoot 'portable-sample.html'
    Invoke-PortableFlow -Executable $executable -Arguments @('sample', $documentPath) -WorkingDirectory $workRoot | Out-Null
    $inspection = Invoke-PortableFlow -Executable $executable -Arguments @('inspect', $documentPath) -WorkingDirectory $workRoot
    $validation = Invoke-PortableFlow -Executable $executable -Arguments @('validate', $documentPath) -WorkingDirectory $workRoot
    Invoke-PortableFlow -Executable $executable -Arguments @('render', $documentPath, '--html', $htmlPath, '--width', '390', '--height', '844') -WorkingDirectory $workRoot | Out-Null
    if (-not (Test-Path -LiteralPath $htmlPath -PathType Leaf) -or
        $inspection.IndexOf('The Flow Experiment', [System.StringComparison]::Ordinal) -lt 0 -or
        $validation.IndexOf('Valid:', [System.StringComparison]::Ordinal) -lt 0) {
        throw 'Portable sample, inspect, validate or render smoke check failed.'
    }

    $result = [ordered]@{
        format = 'flow-windows-portable-smoke-0.1'
        version = $manifest.version
        sourceRevision = $manifest.sourceRevision
        runtimeIdentifier = $manifest.runtimeIdentifier
        dotnetRemovedFromPath = $true
        commands = @('help-plain', 'help-default-safe', 'help-pt-BR', 'menu-redirected-refusal', 'sample', 'inspect', 'validate', 'render-html')
        status = 'passed'
    }
    $resultPath = Join-Path $artifactsRoot 'portable-smoke-result.json'
    [System.IO.File]::WriteAllText(
        $resultPath,
        (($result | ConvertTo-Json -Depth 5).Replace("`r`n", "`n") + "`n"),
        [System.Text.UTF8Encoding]::new($false))
    Write-Output "Windows portable smoke test passed: $($manifest.version)"
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
