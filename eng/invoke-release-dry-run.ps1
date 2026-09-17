[CmdletBinding()]
param(
    [string]$PlanPath,

    [string]$ArtifactsDirectory,

    [switch]$AllowDirty,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($PlanPath)) {
    $PlanPath = Join-Path $PSScriptRoot 'release-plan.json'
}
if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $allowedArtifactsRoot 'release'
}

$planFile = [System.IO.Path]::GetFullPath($PlanPath)
$artifactsRoot = [System.IO.Path]::GetFullPath($ArtifactsDirectory)
$runningOnWindows = $env:OS -eq 'Windows_NT'
$pathComparison = if ($runningOnWindows) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}

if (-not $artifactsRoot.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, $pathComparison)) {
    throw "The release artifacts directory must be a child of '$allowedArtifactsRoot'."
}
if (-not (Test-Path -LiteralPath $planFile -PathType Leaf)) {
    throw "The release plan was not found: $planFile"
}
if (-not (Test-Path -LiteralPath $artifactsRoot -PathType Container)) {
    throw "The release artifacts directory was not found: $artifactsRoot"
}

$artifactsItem = Get-Item -LiteralPath $artifactsRoot -Force
if (($artifactsItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "The release artifacts directory cannot be a reparse point: $artifactsRoot"
}

function Write-Utf8LfAtomic {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)][bool]$Replace
    )

    if ((Test-Path -LiteralPath $Path) -and -not $Replace) {
        throw "The dry-run evidence already exists. Pass -Force to replace it: $Path"
    }

    $temporaryPath = $Path + '.tmp-' + [Guid]::NewGuid().ToString('N')
    $normalized = $Content.Replace("`r`n", "`n").Replace("`r", "`n")
    if (-not $normalized.EndsWith("`n", [System.StringComparison]::Ordinal)) {
        $normalized += "`n"
    }

    try {
        [System.IO.File]::WriteAllText($temporaryPath, $normalized, [System.Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $Path) {
            Remove-Item -LiteralPath $Path -Force
        }
        Move-Item -LiteralPath $temporaryPath -Destination $Path
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Get-JsonFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required release evidence is missing: $Path"
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

$plan = Get-JsonFile -Path $planFile
if ($plan.format -ne 'flow-cli-release-plan-0.1') {
    throw "Unsupported release plan format: $($plan.format)"
}
if ($plan.publication -ne 'disabled') {
    throw 'This dry-run accepts only a release plan with publication set to disabled.'
}
if ([string]::IsNullOrWhiteSpace($plan.packageId) -or
    $plan.packageVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?(?:\+[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$') {
    throw 'The release plan package identity or semantic version is invalid.'
}
$expectedTag = 'v' + $plan.packageVersion
if ($plan.tag -ne $expectedTag) {
    throw "The release tag must be '$expectedTag' for package version '$($plan.packageVersion)'."
}
if ($plan.channel -notin @('prerelease', 'stable')) {
    throw "Unsupported release channel: $($plan.channel)"
}

$manifestPath = Join-Path $artifactsRoot 'release-manifest.json'
$checksumsPath = Join-Path $artifactsRoot 'SHA256SUMS'
$manifest = Get-JsonFile -Path $manifestPath
if ($manifest.format -ne 'flow-cli-release-manifest-0.1' -or
    $manifest.packageId -ne $plan.packageId -or
    $manifest.packageVersion -ne $plan.packageVersion) {
    throw 'The release manifest does not match the versioned release plan.'
}
if (-not (Test-Path -LiteralPath $checksumsPath -PathType Leaf)) {
    throw "Required release checksums are missing: $checksumsPath"
}

$verifiedFiles = @()
foreach ($line in Get-Content -LiteralPath $checksumsPath -Encoding UTF8) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        continue
    }
    if ($line -notmatch '^([0-9a-f]{64})  ([^/\\]+)$') {
        throw "Invalid SHA256SUMS line: $line"
    }
    $expectedHash = $Matches[1]
    $fileName = $Matches[2]
    $filePath = Join-Path $artifactsRoot $fileName
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        throw "Checksummed release file is missing: $fileName"
    }
    $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "Checksum validation failed for $fileName."
    }
    $verifiedFiles += [ordered]@{ path = $fileName; sha256 = $actualHash }
}
if ($verifiedFiles.Count -lt 3) {
    throw 'SHA256SUMS does not cover the package, SBOM and release manifest.'
}

$packageEntry = $manifest.files | Where-Object { $_.path -like '*.nupkg' } | Select-Object -First 1
$sbomEntry = $manifest.files | Where-Object { $_.mediaType -eq 'application/vnd.cyclonedx+json' } | Select-Object -First 1
if ($null -eq $packageEntry -or $null -eq $sbomEntry) {
    throw 'The release manifest does not identify both the package and CycloneDX SBOM.'
}
if ($null -eq $manifest.packageEntries -or $manifest.packageEntries.Count -eq 0) {
    throw 'The release manifest does not contain package-entry evidence.'
}
$packageEntryPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($entry in $manifest.packageEntries) {
    if ([string]::IsNullOrWhiteSpace($entry.path) -or
        $entry.bytes -lt 0 -or
        $entry.sha256 -notmatch '^[0-9a-f]{64}$' -or
        -not $packageEntryPaths.Add([string]$entry.path)) {
        throw 'The release manifest contains invalid package-entry evidence.'
    }
}

$packageEvidence = $verifiedFiles | Where-Object { $_.path -eq $packageEntry.path } | Select-Object -First 1
$sbomEvidence = $verifiedFiles | Where-Object { $_.path -eq $sbomEntry.path } | Select-Object -First 1
if ($null -eq $packageEvidence -or $packageEvidence.sha256 -ne $packageEntry.sha256 -or
    $null -eq $sbomEvidence -or $sbomEvidence.sha256 -ne $sbomEntry.sha256) {
    throw 'The release manifest and SHA256SUMS disagree about the package or SBOM.'
}

$provenanceFileName = "$($plan.packageId).$($plan.packageVersion).intoto.jsonl"
$provenancePath = Join-Path $artifactsRoot $provenanceFileName
$dryRunFileName = 'release-dry-run.json'
$dryRunPath = Join-Path $artifactsRoot $dryRunFileName
$evidencePath = Join-Path $artifactsRoot 'release-evidence.json'
if (-not $Force) {
    foreach ($outputFile in @($provenancePath, $dryRunPath, $evidencePath)) {
        if (Test-Path -LiteralPath $outputFile) {
            throw "The dry-run evidence already exists. Pass -Force to replace it: $outputFile"
        }
    }
}

$sourceRevision = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceRevision -notmatch '^[0-9a-f]{40}$') {
    throw 'Could not resolve the source Git revision.'
}
$sourceChanges = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect the source Git state.'
}
$sourceState = if ($sourceChanges.Count -eq 0) { 'clean' } else { 'dirty' }
if ($sourceState -eq 'dirty' -and -not $AllowDirty) {
    throw 'The source tree is dirty. Commit or stash changes, or pass -AllowDirty for a non-releasable local dry-run.'
}

$tagReference = "refs/tags/$($plan.tag)"
$tagRevision = [string]((& git -C $repositoryRoot tag --list --format='%(objectname)' $plan.tag) -join '')
$tagRevision = $tagRevision.Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Could not inspect release tag '$($plan.tag)'."
}
if (-not [string]::IsNullOrWhiteSpace($tagRevision)) {
    $tagCommit = (& git -C $repositoryRoot rev-list -n 1 $tagReference).Trim()
    if ($LASTEXITCODE -ne 0 -or $tagCommit -ne $sourceRevision) {
        throw "Existing tag '$($plan.tag)' does not point to the source revision under test."
    }
    $tagState = 'points-to-source'
}
else {
    $tagState = 'absent'
}

$sdkVersion = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sdkVersion)) {
    throw 'Could not resolve the .NET SDK version.'
}
$platform = Get-NormalizedPlatform
$repositoryUri = 'https://github.com/MardSilva/FlowEngineNet'
$resolvedDependencies = @()
if ($sourceState -eq 'clean') {
    $resolvedDependencies = @([ordered]@{
        uri = "git+$repositoryUri@$sourceRevision"
        digest = [ordered]@{ gitCommit = $sourceRevision }
    })
}

$provenance = [ordered]@{
    '_type' = 'https://in-toto.io/Statement/v1'
    subject = @(
        [ordered]@{
            name = $packageEntry.path
            digest = [ordered]@{ sha256 = $packageEvidence.sha256 }
        },
        [ordered]@{
            name = $sbomEntry.path
            digest = [ordered]@{ sha256 = $sbomEvidence.sha256 }
        })
    predicateType = 'https://slsa.dev/provenance/v1'
    predicate = [ordered]@{
        buildDefinition = [ordered]@{
            buildType = "$repositoryUri/build-types/dotnet-tool-release/v1"
            externalParameters = [ordered]@{
                configuration = $manifest.configuration
                packageId = $plan.packageId
                packageVersion = $plan.packageVersion
                targetFramework = $manifest.targetFramework
                expectedTag = $plan.tag
                publication = $plan.publication
            }
            internalParameters = [ordered]@{
                sourceRevision = $sourceRevision
                sourceState = $sourceState
                tagState = $tagState
                buildPlatform = $platform
                dotnetSdkVersion = $sdkVersion
            }
            resolvedDependencies = @($resolvedDependencies)
        }
        runDetails = [ordered]@{
            builder = [ordered]@{
                id = "$repositoryUri/eng/invoke-release-dry-run.ps1@v1"
            }
            byproducts = @(
                [ordered]@{
                    name = 'release-manifest.json'
                    digest = [ordered]@{
                        sha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
                    }
                },
                [ordered]@{
                    name = 'SHA256SUMS'
                    digest = [ordered]@{
                        sha256 = (Get-FileHash -LiteralPath $checksumsPath -Algorithm SHA256).Hash.ToLowerInvariant()
                    }
                })
        }
    }
}

$provenanceJson = $provenance | ConvertTo-Json -Depth 20 -Compress
Write-Utf8LfAtomic -Path $provenancePath -Content $provenanceJson -Replace ([bool]$Force)
$provenanceHash = (Get-FileHash -LiteralPath $provenancePath -Algorithm SHA256).Hash.ToLowerInvariant()

$releaseReady = $sourceState -eq 'clean'
$dryRun = [ordered]@{
    format = 'flow-cli-release-dry-run-0.1'
    packageId = $plan.packageId
    packageVersion = $plan.packageVersion
    expectedTag = $plan.tag
    channel = $plan.channel
    source = [ordered]@{
        revision = $sourceRevision
        state = $sourceState
        tagState = $tagState
    }
    platform = $platform
    dotnetSdkVersion = $sdkVersion
    releaseReady = $releaseReady
    status = if ($releaseReady) { 'passed' } else { 'passed-with-warnings' }
    validation = [ordered]@{
        versionPlan = 'passed'
        checksums = 'passed'
        manifest = 'passed'
        provenance = 'passed'
        publication = 'not-performed'
    }
    publicationPerformed = $false
}

Write-Utf8LfAtomic -Path $dryRunPath -Content ($dryRun | ConvertTo-Json -Depth 12 -Compress) -Replace ([bool]$Force)
$dryRunHash = (Get-FileHash -LiteralPath $dryRunPath -Algorithm SHA256).Hash.ToLowerInvariant()

$coreEvidence = @()
foreach ($evidenceFile in @($verifiedFiles | Sort-Object path)) {
    $coreEvidence += [ordered]@{
        path = $evidenceFile.path
        sha256 = $evidenceFile.sha256
    }
}
$coreEvidence += [ordered]@{
    path = 'SHA256SUMS'
    sha256 = (Get-FileHash -LiteralPath $checksumsPath -Algorithm SHA256).Hash.ToLowerInvariant()
}

$evidence = [ordered]@{
    format = 'flow-cli-release-evidence-0.1'
    packageId = $plan.packageId
    packageVersion = $plan.packageVersion
    expectedTag = $plan.tag
    platform = $platform
    sourceRevision = $sourceRevision
    sourceState = $sourceState
    releaseReady = $releaseReady
    publicationPerformed = $false
    coreFiles = @($coreEvidence)
    packageEntries = @($manifest.packageEntries)
    provenance = [ordered]@{ path = $provenanceFileName; sha256 = $provenanceHash }
    dryRun = [ordered]@{ path = $dryRunFileName; sha256 = $dryRunHash }
}

Write-Utf8LfAtomic -Path $evidencePath -Content ($evidence | ConvertTo-Json -Depth 12 -Compress) -Replace ([bool]$Force)

Write-Output "Versioned release dry-run passed without publication: $($plan.tag) [$platform, source=$sourceState]"
if (-not $releaseReady) {
    Write-Warning 'The dry-run used -AllowDirty and is not releasable evidence. Run again from a clean source tree.'
}
