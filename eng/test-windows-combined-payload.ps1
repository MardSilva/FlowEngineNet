[CmdletBinding()]
param(
    [string]$ArtifactsDirectory,

    [switch]$SkipApplicationLaunch
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') {
    throw 'The combined win-x64 payload test must run on Windows.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $allowedArtifactsRoot 'windows-combined-payload'
}
$artifactsRoot = [System.IO.Path]::GetFullPath($ArtifactsDirectory)
if (-not $artifactsRoot.StartsWith(
        $allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The combined payload artifacts directory must be a child of '$allowedArtifactsRoot'."
}

function Get-Sha256Lower {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-PrivatePathPrefixes {
    param([Parameter(Mandatory)][string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
    $unicode = [System.Text.Encoding]::Unicode.GetString($bytes)
    foreach ($prefix in @('C:\Users\', '/home/runner/', '/Users/')) {
        if ($ascii.IndexOf($prefix, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
            $unicode.IndexOf($prefix, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            throw "Combined payload file contains a private build path prefix: $Path ($prefix)"
        }
    }
}

function Invoke-CombinedCli {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string]$WorkingDirectory
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
    foreach ($argument in @('--plain', 'help')) {
        $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    $output = $process.StandardOutput.ReadToEnd()
    $errorOutput = $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0 -or $output -notmatch 'Flow Engine .NET' -or $errorOutput.Length -ne 0) {
        throw "Combined CLI smoke test failed. Exit code: $($process.ExitCode). stderr: $errorOutput"
    }
}

function Invoke-ControlledApplicationLaunch {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string]$WorkingDirectory
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.Environment['PATH'] = [Environment]::GetFolderPath([Environment+SpecialFolder]::System)
    $startInfo.Environment.Remove('DOTNET_ROOT') | Out-Null
    $startInfo.Environment.Remove('DOTNET_ROOT_X64') | Out-Null

    $process = [System.Diagnostics.Process]::Start($startInfo)
    try {
        $null = $process.WaitForInputIdle(15000)
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            $process.Refresh()
            if ($process.HasExited) {
                throw "The Windows application exited during launch with code $($process.ExitCode)."
            }
            if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                break
            }
            Start-Sleep -Milliseconds 200
        } while ([DateTime]::UtcNow -lt $deadline)

        if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
            throw 'The Windows application did not create a main window within 15 seconds.'
        }
        if (-not $process.CloseMainWindow()) {
            throw 'The Windows application main window could not be closed normally.'
        }
        if (-not $process.WaitForExit(5000)) {
            throw 'The Windows application did not stop after its main window was closed.'
        }
    }
    finally {
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $process.Dispose()
    }
}

$payloadRoot = Join-Path $artifactsRoot 'payload'
$manifestPath = Join-Path $artifactsRoot 'combined-payload-manifest.json'
$checksumsPath = Join-Path $artifactsRoot 'SHA256SUMS'
foreach ($requiredPath in @($payloadRoot, $manifestPath, $checksumsPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Combined payload artifact is missing: $requiredPath"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$versionDocument = Get-Content -LiteralPath (Join-Path $payloadRoot 'VERSION.json') -Raw | ConvertFrom-Json
if ($manifest.format -ne 'flow-windows-combined-payload-0.1' -or
    $manifest.version -ne $versionDocument.version -or
    $manifest.sourceRevision -ne $versionDocument.sourceRevision -or
    $manifest.runtimeIdentifier -ne $versionDocument.runtimeIdentifier -or
    $manifest.architecture -ne $versionDocument.architecture) {
    throw 'Combined payload manifest and VERSION.json identify different builds.'
}

$applicationEntryPoint = [string]$manifest.entryPoints.application.path
$cliEntryPoint = [string]$manifest.entryPoints.cli.path
if ($applicationEntryPoint -ne [string]$versionDocument.entryPoints.application -or
    $cliEntryPoint -ne [string]$versionDocument.entryPoints.cli) {
    throw 'Combined payload entry points are inconsistent.'
}

$actualFiles = @(Get-ChildItem -LiteralPath $payloadRoot -File -Recurse |
    ForEach-Object { [System.IO.Path]::GetRelativePath($payloadRoot, $_.FullName).Replace('\', '/') } |
    Sort-Object)
$declaredFiles = @($manifest.files | ForEach-Object { [string]$_.path } | Sort-Object)
if ([string]::Join("`n", $actualFiles) -cne [string]::Join("`n", $declaredFiles)) {
    throw 'The combined payload manifest does not cover exactly the installable files.'
}

foreach ($entry in $manifest.files) {
    $path = Join-Path $payloadRoot ([string]$entry.path)
    if ((Get-Item -LiteralPath $path).Length -ne [long]$entry.bytes -or
        (Get-Sha256Lower -Path $path) -ne [string]$entry.sha256) {
        throw "Combined payload file validation failed: $($entry.path)"
    }
}

$checksumEntries = @{}
foreach ($line in Get-Content -LiteralPath $checksumsPath) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') {
        throw "Invalid checksum line: $line"
    }
    $checksumEntries[$Matches[2]] = $Matches[1]
}
foreach ($entry in $checksumEntries.GetEnumerator()) {
    $path = Join-Path $artifactsRoot $entry.Key
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or
        (Get-Sha256Lower -Path $path) -ne $entry.Value) {
        throw "Combined payload checksum validation failed: $($entry.Key)"
    }
}

foreach ($relativePath in $actualFiles) {
    if ($relativePath -match '(^|/)(bin|obj|tests?|cache|\.git)(/|$)' -or
        $relativePath -match '\.(pdb|cache|user|suo)$') {
        throw "Combined payload contains a forbidden path: $relativePath"
    }
    Test-PrivatePathPrefixes -Path (Join-Path $payloadRoot $relativePath)
}

$applicationPath = Join-Path $payloadRoot $applicationEntryPoint
$cliPath = Join-Path $payloadRoot $cliEntryPoint
foreach ($entryPoint in @($applicationPath, $cliPath)) {
    if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
        throw "Combined payload entry point is missing: $entryPoint"
    }
    $productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($entryPoint).ProductVersion
    if (-not $productVersion.StartsWith([string]$manifest.version, [System.StringComparison]::Ordinal)) {
        throw "Combined payload entry point has a different public version: $entryPoint"
    }
}

Invoke-CombinedCli -Executable $cliPath -WorkingDirectory (Split-Path -Parent $cliPath)
if (-not $SkipApplicationLaunch) {
    Invoke-ControlledApplicationLaunch -Executable $applicationPath -WorkingDirectory (Split-Path -Parent $applicationPath)
}

$result = [ordered]@{
    format = 'flow-windows-combined-payload-smoke-0.1'
    version = $manifest.version
    sourceRevision = $manifest.sourceRevision
    runtimeIdentifier = $manifest.runtimeIdentifier
    manifestCoverage = 'passed'
    cliExecution = 'passed'
    applicationLaunch = if ($SkipApplicationLaunch) { 'skipped' } else { 'passed' }
    installedStateChanged = $false
    status = 'passed'
}
[System.IO.File]::WriteAllText(
    (Join-Path $artifactsRoot 'combined-payload-smoke-result.json'),
    (($result | ConvertTo-Json -Depth 5).Replace("`r`n", "`n") + "`n"),
    [System.Text.UTF8Encoding]::new($false))
Write-Output "Combined Windows payload smoke test passed: $($manifest.version)"
