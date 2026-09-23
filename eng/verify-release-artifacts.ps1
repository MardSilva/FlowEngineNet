[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactsDirectory,

    [string]$OutputDirectory,

    [switch]$AllowDirty,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$artifactsRoot = [System.IO.Path]::GetFullPath($ArtifactsDirectory)
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $allowedArtifactsRoot 'release-validation'
}
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$runningOnWindows = $env:OS -eq 'Windows_NT'
$pathComparison = if ($runningOnWindows) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}

foreach ($path in @($artifactsRoot, $outputRoot)) {
    if (-not $path.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, $pathComparison)) {
        throw "Release validation paths must stay under '$allowedArtifactsRoot': $path"
    }
}
if ($artifactsRoot -eq $outputRoot -or
    $artifactsRoot.StartsWith($outputRoot + [System.IO.Path]::DirectorySeparatorChar, $pathComparison) -or
    $outputRoot.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar, $pathComparison)) {
    throw 'The release input and validation output directories must be disjoint.'
}
if (-not (Test-Path -LiteralPath $artifactsRoot -PathType Container)) {
    throw "The canonical release directory was not found: $artifactsRoot"
}
if (((Get-Item -LiteralPath $artifactsRoot -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "The canonical release directory cannot be a reparse point: $artifactsRoot"
}
if (Test-Path -LiteralPath $outputRoot) {
    if (-not $Force) {
        throw "The release validation output already exists. Pass -Force to replace it: $outputRoot"
    }
    $outputItem = Get-Item -LiteralPath $outputRoot -Force
    if (($outputItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The release validation output cannot be a reparse point: $outputRoot"
    }
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

function Read-JsonFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required release file is missing: $Path"
    }
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Get-NormalizedPlatform {
    if ($runningOnWindows) {
        return 'windows'
    }
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::Linux)) {
        return 'linux'
    }
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
            [System.Runtime.InteropServices.OSPlatform]::OSX)) {
        return 'macos'
    }
    return 'unknown'
}

$manifestPath = Join-Path $artifactsRoot 'release-manifest.json'
$checksumsPath = Join-Path $artifactsRoot 'SHA256SUMS'
$manifest = Read-JsonFile -Path $manifestPath
if ($manifest.format -ne 'flow-cli-release-manifest-0.1' -or
    [string]::IsNullOrWhiteSpace($manifest.packageId) -or
    [string]::IsNullOrWhiteSpace($manifest.packageVersion) -or
    $manifest.reproducibility.independentPackCount -ne 2 -or
    -not $manifest.reproducibility.normalizedPackageBytesMatch) {
    throw 'The canonical release manifest is invalid or lacks local reproducibility evidence.'
}
if (-not (Test-Path -LiteralPath $checksumsPath -PathType Leaf)) {
    throw "Canonical release checksums are missing: $checksumsPath"
}

$coreFiles = @()
$seenFiles = @{}
foreach ($line in Get-Content -LiteralPath $checksumsPath -Encoding UTF8) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }
    if ($line -notmatch '^([0-9a-f]{64})  ([^/\\]+)$') {
        throw "Invalid SHA256SUMS line: $line"
    }
    $expectedHash = $Matches[1]
    $fileName = $Matches[2]
    if ($seenFiles.ContainsKey($fileName)) {
        throw "SHA256SUMS repeats '$fileName'."
    }
    $seenFiles[$fileName] = $true
    $filePath = Join-Path $artifactsRoot $fileName
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        throw "Checksummed release file is missing: $fileName"
    }
    $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "Checksum validation failed for $fileName."
    }
    $coreFiles += [ordered]@{ path = $fileName; sha256 = $actualHash }
}
if ($coreFiles.Count -lt 3) {
    throw 'SHA256SUMS must cover the package, SBOM and release manifest.'
}
$coreFiles += [ordered]@{
    path = 'SHA256SUMS'
    sha256 = (Get-FileHash -LiteralPath $checksumsPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

$packageFiles = @($coreFiles | Where-Object { $_.path -like '*.nupkg' })
if ($packageFiles.Count -ne 1) {
    throw 'SHA256SUMS must identify exactly one NuGet package.'
}
$packageFile = $packageFiles[0]
$packagePath = Join-Path $artifactsRoot $packageFile.path
$manifestPackage = @($manifest.files | Where-Object { $_.path -eq $packageFile.path })
if ($manifestPackage.Count -ne 1 -or $manifestPackage[0].sha256 -ne $packageFile.sha256) {
    throw 'The release manifest and SHA256SUMS disagree on the canonical package.'
}

$plan = Read-JsonFile -Path (Join-Path $PSScriptRoot 'release-plan.json')
if ($plan.format -ne 'flow-cli-release-plan-0.1' -or
    $plan.packageId -ne $manifest.packageId -or
    $plan.packageVersion -ne $manifest.packageVersion -or
    $plan.tag -ne ('v' + $manifest.packageVersion) -or
    $plan.publication -ne 'disabled' -or
    $plan.githubRelease -ne 'draft-only') {
    throw 'The canonical release does not match the checked-in release plan.'
}

$sourceRevision = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$sourceStatus = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0 -or $sourceRevision -notmatch '^[0-9a-f]{40}$') {
    throw 'Could not resolve the Git revision for cross-platform release validation.'
}
$sourceState = if ($sourceStatus.Count -eq 0) { 'clean' } else { 'dirty' }
if ($sourceState -ne 'clean' -and -not $AllowDirty) {
    throw 'Cross-platform release validation requires a clean Git checkout. Pass -AllowDirty only for local development.'
}

$smokeDirectory = Join-Path $outputRoot 'smoke'
& (Join-Path $PSScriptRoot 'smoke-test-cli.ps1') `
    -Configuration Release `
    -ArtifactsDirectory $smokeDirectory `
    -PackagePath $packagePath
if ($LASTEXITCODE -ne 0) {
    throw 'The canonical package smoke test failed.'
}
$smokeResult = Read-JsonFile -Path (Join-Path $smokeDirectory 'smoke-result.json')
if ($smokeResult.format -ne 'flow-cli-distribution-smoke-0.1' -or
    $smokeResult.packageId -ne $manifest.packageId -or
    $smokeResult.packageVersion -ne $manifest.packageVersion -or
    $smokeResult.packageSha256 -ne $packageFile.sha256 -or
    $smokeResult.packageOrigin -ne 'supplied' -or
    $smokeResult.status -ne 'passed') {
    throw 'The installed-package smoke evidence is invalid.'
}

$platform = Get-NormalizedPlatform
$sdkVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sdkVersion)) {
    throw 'Could not resolve the .NET SDK version used for validation.'
}

$validation = [ordered]@{
    format = 'flow-cli-release-platform-validation-0.1'
    platform = $platform
    packageId = $manifest.packageId
    packageVersion = $manifest.packageVersion
    expectedTag = $plan.tag
    sourceRevision = $sourceRevision
    sourceState = $sourceState
    releaseReady = $sourceState -eq 'clean'
    dotnetSdkVersion = $sdkVersion
    canonicalPackage = [ordered]@{
        path = $packageFile.path
        bytes = [long](Get-Item -LiteralPath $packagePath).Length
        sha256 = $packageFile.sha256
    }
    coreFiles = @($coreFiles)
    validation = [ordered]@{
        checksums = 'passed'
        manifest = 'passed'
        isolatedToolInstall = 'passed'
        installedCommands = @($smokeResult.commands)
    }
    result = 'passed'
    publicationPerformed = $false
}

$outputPath = Join-Path $outputRoot 'release-validation.json'
$temporaryPath = $outputPath + '.tmp-' + [Guid]::NewGuid().ToString('N')
try {
    $json = ($validation | ConvertTo-Json -Depth 12 -Compress).Replace("`r`n", "`n").Replace("`r", "`n") + "`n"
    [System.IO.File]::WriteAllText($temporaryPath, $json, [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryPath -Destination $outputPath
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}

Write-Output "Canonical Flow CLI package passed $platform validation: $outputPath"
