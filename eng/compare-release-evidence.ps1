[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CanonicalEvidenceDirectory,
    [Parameter(Mandatory)][string]$WindowsValidationDirectory,
    [Parameter(Mandatory)][string]$LinuxValidationDirectory,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $allowedArtifactsRoot 'release-comparison.json'
}

$canonicalRoot = [System.IO.Path]::GetFullPath($CanonicalEvidenceDirectory)
$windowsRoot = [System.IO.Path]::GetFullPath($WindowsValidationDirectory)
$linuxRoot = [System.IO.Path]::GetFullPath($LinuxValidationDirectory)
$comparisonPath = [System.IO.Path]::GetFullPath($OutputPath)
$pathComparison = if ($env:OS -eq 'Windows_NT') {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}

foreach ($path in @($canonicalRoot, $windowsRoot, $linuxRoot, $comparisonPath)) {
    if (-not $path.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, $pathComparison)) {
        throw "Release comparison inputs and output must stay under '$allowedArtifactsRoot': $path"
    }
}

function Read-JsonFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required release evidence is missing: $Path"
    }
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Read-CoreFileEvidence {
    param(
        [Parameter(Mandatory)]$Files,
        [Parameter(Mandatory)][string]$Description
    )

    $result = @{}
    foreach ($file in $Files) {
        if ([string]::IsNullOrWhiteSpace($file.path) -or
            $file.path -match '[/\\]' -or
            $file.sha256 -notmatch '^[0-9a-f]{64}$' -or
            $result.ContainsKey([string]$file.path)) {
            throw "$Description contains invalid or repeated core-file evidence."
        }
        $result[[string]$file.path] = [string]$file.sha256
    }
    if ($result.Count -lt 3) {
        throw "$Description does not cover the package, SBOM and release manifest."
    }
    return $result
}

function Read-CanonicalEvidence {
    param([Parameter(Mandatory)][string]$Directory)

    if (-not (Test-Path -LiteralPath $Directory -PathType Container) -or
        ((Get-Item -LiteralPath $Directory -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The canonical release evidence directory is missing or unsafe: $Directory"
    }

    $evidence = Read-JsonFile -Path (Join-Path $Directory 'release-evidence.json')
    if ($evidence.format -ne 'flow-cli-release-evidence-0.1' -or
        $evidence.platform -ne 'linux' -or
        $evidence.sourceState -ne 'clean' -or
        -not $evidence.releaseReady -or
        $evidence.publicationPerformed) {
        throw 'The canonical release evidence is unsupported, dirty or non-releasable.'
    }
    foreach ($relativePath in @($evidence.dryRun.path, $evidence.provenance.path)) {
        if ([string]::IsNullOrWhiteSpace($relativePath) -or $relativePath -match '[/\\]') {
            throw 'Canonical evidence contains an unsafe related-file path.'
        }
    }

    $dryRunPath = Join-Path $Directory $evidence.dryRun.path
    $provenancePath = Join-Path $Directory $evidence.provenance.path
    $dryRun = Read-JsonFile -Path $dryRunPath
    $provenance = Read-JsonFile -Path $provenancePath
    if ($dryRun.format -ne 'flow-cli-release-dry-run-0.1' -or
        $dryRun.platform -ne 'linux' -or
        $dryRun.source.state -ne 'clean' -or
        -not $dryRun.releaseReady -or
        $dryRun.publicationPerformed -or
        $dryRun.validation.publication -ne 'not-performed' -or
        $provenance.'_type' -ne 'https://in-toto.io/Statement/v1' -or
        $provenance.predicateType -ne 'https://slsa.dev/provenance/v1' -or
        $provenance.predicate.buildDefinition.internalParameters.buildPlatform -ne 'linux') {
        throw 'The canonical dry-run or provenance evidence is invalid.'
    }

    $actualDryRunHash = (Get-FileHash -LiteralPath $dryRunPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $actualProvenanceHash = (Get-FileHash -LiteralPath $provenancePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualDryRunHash -ne $evidence.dryRun.sha256 -or
        $actualProvenanceHash -ne $evidence.provenance.sha256) {
        throw 'Canonical related-file hashes are invalid.'
    }

    $coreFiles = Read-CoreFileEvidence -Files $evidence.coreFiles -Description 'Canonical release evidence'
    foreach ($fileName in $coreFiles.Keys) {
        $filePath = Join-Path $Directory $fileName
        if (-not (Test-Path -LiteralPath $filePath -PathType Leaf) -or
            (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $coreFiles[$fileName]) {
            throw "Canonical release file '$fileName' does not match its evidence."
        }
    }

    $subjectHashes = @{}
    foreach ($subject in $provenance.subject) {
        $subjectHashes[[string]$subject.name] = [string]$subject.digest.sha256
    }
    foreach ($fileName in @($coreFiles.Keys | Where-Object { $_ -like '*.nupkg' -or $_ -like '*.cdx.json' })) {
        if (-not $subjectHashes.ContainsKey($fileName) -or $subjectHashes[$fileName] -ne $coreFiles[$fileName]) {
            throw "Canonical provenance does not bind '$fileName'."
        }
    }

    return [pscustomobject]@{
        Evidence = $evidence
        DryRun = $dryRun
        CoreFiles = $coreFiles
    }
}

function Read-PlatformValidation {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$ExpectedPlatform
    )

    if (-not (Test-Path -LiteralPath $Directory -PathType Container) -or
        ((Get-Item -LiteralPath $Directory -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The $ExpectedPlatform validation directory is missing or unsafe: $Directory"
    }
    $path = Join-Path $Directory 'release-validation.json'
    $validation = Read-JsonFile -Path $path
    if ($validation.format -ne 'flow-cli-release-platform-validation-0.1' -or
        $validation.platform -ne $ExpectedPlatform -or
        $validation.sourceState -ne 'clean' -or
        -not $validation.releaseReady -or
        $validation.result -ne 'passed' -or
        $validation.publicationPerformed -or
        $validation.validation.checksums -ne 'passed' -or
        $validation.validation.manifest -ne 'passed' -or
        $validation.validation.isolatedToolInstall -ne 'passed') {
        throw "The $ExpectedPlatform release validation is invalid or non-releasable."
    }
    if ($validation.canonicalPackage.path -match '[/\\]' -or
        $validation.canonicalPackage.bytes -le 0 -or
        $validation.canonicalPackage.sha256 -notmatch '^[0-9a-f]{64}$') {
        throw "The $ExpectedPlatform canonical-package evidence is invalid."
    }

    return [pscustomobject]@{
        Evidence = $validation
        CoreFiles = Read-CoreFileEvidence -Files $validation.coreFiles -Description "$ExpectedPlatform validation"
        EvidenceSha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$canonical = Read-CanonicalEvidence -Directory $canonicalRoot
$windows = Read-PlatformValidation -Directory $windowsRoot -ExpectedPlatform 'windows'
$linux = Read-PlatformValidation -Directory $linuxRoot -ExpectedPlatform 'linux'

foreach ($validation in @($windows, $linux)) {
    foreach ($property in @('packageId', 'packageVersion', 'expectedTag', 'sourceRevision', 'dotnetSdkVersion')) {
        $canonicalValue = if ($property -eq 'dotnetSdkVersion') {
            $canonical.DryRun.dotnetSdkVersion
        }
        else {
            $canonical.Evidence.$property
        }
        if ($validation.Evidence.$property -ne $canonicalValue) {
            throw "$($validation.Evidence.platform) validation disagrees with canonical evidence on '$property'."
        }
    }
    if ($validation.CoreFiles.Count -ne $canonical.CoreFiles.Count) {
        throw "$($validation.Evidence.platform) validation has a different canonical file set."
    }
    foreach ($fileName in $canonical.CoreFiles.Keys) {
        if (-not $validation.CoreFiles.ContainsKey($fileName) -or
            $validation.CoreFiles[$fileName] -ne $canonical.CoreFiles[$fileName]) {
            throw "$($validation.Evidence.platform) did not validate canonical file '$fileName'."
        }
    }
}

$packageFileNames = @($canonical.CoreFiles.Keys | Where-Object { $_ -like '*.nupkg' })
if ($packageFileNames.Count -ne 1) {
    throw 'Canonical evidence must identify exactly one NuGet package.'
}
$packageFileName = $packageFileNames[0]
$packageHash = $canonical.CoreFiles[$packageFileName]
foreach ($validation in @($windows, $linux)) {
    if ($validation.Evidence.canonicalPackage.path -ne $packageFileName -or
        $validation.Evidence.canonicalPackage.sha256 -ne $packageHash) {
        throw "$($validation.Evidence.platform) validated a different NuGet package."
    }
}

$portableFileNames = [string[]]@($canonical.CoreFiles.Keys)
[Array]::Sort($portableFileNames, [System.StringComparer]::Ordinal)
$portableFiles = @(
    foreach ($fileName in $portableFileNames) {
        [ordered]@{ path = $fileName; sha256 = $canonical.CoreFiles[$fileName] }
    }
)
$comparison = [ordered]@{
    format = 'flow-cli-multiplatform-release-validation-0.1'
    packageId = $canonical.Evidence.packageId
    packageVersion = $canonical.Evidence.packageVersion
    expectedTag = $canonical.Evidence.expectedTag
    sourceRevision = $canonical.Evidence.sourceRevision
    dotnetSdkVersion = $canonical.DryRun.dotnetSdkVersion
    canonicalBuilder = 'linux'
    validators = @(
        [ordered]@{ platform = 'linux'; evidenceSha256 = $linux.EvidenceSha256 },
        [ordered]@{ platform = 'windows'; evidenceSha256 = $windows.EvidenceSha256 }
    )
    portableFiles = $portableFiles
    result = 'passed'
    publicationPerformed = $false
}

$outputDirectory = [System.IO.Path]::GetDirectoryName($comparisonPath)
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$temporaryPath = $comparisonPath + '.tmp-' + [Guid]::NewGuid().ToString('N')
try {
    $json = ($comparison | ConvertTo-Json -Depth 12 -Compress).Replace("`r`n", "`n").Replace("`r", "`n") + "`n"
    [System.IO.File]::WriteAllText($temporaryPath, $json, [System.Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $comparisonPath) {
        Remove-Item -LiteralPath $comparisonPath -Force
    }
    Move-Item -LiteralPath $temporaryPath -Destination $comparisonPath
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}

Write-Output "The canonical Flow CLI package passed Windows/Linux validation: $comparisonPath"
