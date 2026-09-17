[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$WindowsEvidenceDirectory,
    [Parameter(Mandatory)][string]$LinuxEvidenceDirectory,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $allowedArtifactsRoot 'release-comparison.json'
}

$windowsRoot = [System.IO.Path]::GetFullPath($WindowsEvidenceDirectory)
$linuxRoot = [System.IO.Path]::GetFullPath($LinuxEvidenceDirectory)
$comparisonPath = [System.IO.Path]::GetFullPath($OutputPath)
$pathComparison = if ($env:OS -eq 'Windows_NT') {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}

foreach ($path in @($windowsRoot, $linuxRoot, $comparisonPath)) {
    if (-not $path.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, $pathComparison)) {
        throw "Release comparison inputs and output must stay under '$allowedArtifactsRoot': $path"
    }
}

function Read-JsonFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required cross-platform evidence is missing: $Path"
    }
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Read-EvidenceSet {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string]$ExpectedPlatform
    )

    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        throw "Release evidence directory was not found: $Directory"
    }
    $directoryItem = Get-Item -LiteralPath $Directory -Force
    if (($directoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Release evidence directory cannot be a reparse point: $Directory"
    }

    $evidence = Read-JsonFile -Path (Join-Path $Directory 'release-evidence.json')
    $dryRunPath = Join-Path $Directory $evidence.dryRun.path
    $provenancePath = Join-Path $Directory $evidence.provenance.path
    $dryRun = Read-JsonFile -Path $dryRunPath
    $provenance = Read-JsonFile -Path $provenancePath

    if ($evidence.format -ne 'flow-cli-release-evidence-0.1' -or
        $dryRun.format -ne 'flow-cli-release-dry-run-0.1' -or
        $provenance.'_type' -ne 'https://in-toto.io/Statement/v1' -or
        $provenance.predicateType -ne 'https://slsa.dev/provenance/v1') {
        throw "Unsupported release evidence format in $Directory."
    }
    if ($evidence.platform -ne $ExpectedPlatform -or
        $dryRun.platform -ne $ExpectedPlatform -or
        $provenance.predicate.buildDefinition.internalParameters.buildPlatform -ne $ExpectedPlatform) {
        throw "Release evidence platform does not match '$ExpectedPlatform'."
    }
    if ($evidence.sourceState -ne 'clean' -or
        -not $evidence.releaseReady -or
        $evidence.publicationPerformed -or
        $dryRun.source.state -ne 'clean' -or
        -not $dryRun.releaseReady -or
        $dryRun.publicationPerformed -or
        $dryRun.validation.publication -ne 'not-performed') {
        throw "The $ExpectedPlatform evidence is dirty, non-releasable or claims publication."
    }

    $actualDryRunHash = (Get-FileHash -LiteralPath $dryRunPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $actualProvenanceHash = (Get-FileHash -LiteralPath $provenancePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualDryRunHash -ne $evidence.dryRun.sha256 -or
        $actualProvenanceHash -ne $evidence.provenance.sha256) {
        throw "The $ExpectedPlatform evidence file hashes are invalid."
    }

    $coreFiles = @{}
    foreach ($file in $evidence.coreFiles) {
        if ([string]::IsNullOrWhiteSpace($file.path) -or $file.sha256 -notmatch '^[0-9a-f]{64}$') {
            throw "The $ExpectedPlatform portable file evidence is invalid."
        }
        if ($coreFiles.ContainsKey([string]$file.path)) {
            throw "The $ExpectedPlatform evidence repeats portable file '$($file.path)'."
        }
        $coreFiles[[string]$file.path] = [string]$file.sha256
    }

    $packageEntries = @{}
    foreach ($entry in $evidence.packageEntries) {
        if ([string]::IsNullOrWhiteSpace($entry.path) -or
            $entry.bytes -lt 0 -or
            $entry.sha256 -notmatch '^[0-9a-f]{64}$' -or
            $packageEntries.ContainsKey([string]$entry.path)) {
            throw "The $ExpectedPlatform package-entry evidence is invalid."
        }
        $packageEntries[[string]$entry.path] = [pscustomobject]@{
            Bytes = [long]$entry.bytes
            Sha256 = [string]$entry.sha256
        }
    }
    if ($packageEntries.Count -eq 0) {
        throw "The $ExpectedPlatform package-entry evidence is empty."
    }

    $subjectHashes = @{}
    foreach ($subject in $provenance.subject) {
        $subjectHashes[[string]$subject.name] = [string]$subject.digest.sha256
    }
    foreach ($fileName in @($coreFiles.Keys | Where-Object { $_ -like '*.nupkg' -or $_ -like '*.cdx.json' })) {
        if (-not $subjectHashes.ContainsKey($fileName) -or $subjectHashes[$fileName] -ne $coreFiles[$fileName]) {
            throw "The $ExpectedPlatform provenance subject does not match '$fileName'."
        }
    }

    return [pscustomobject]@{
        Evidence = $evidence
        DryRun = $dryRun
        Provenance = $provenance
        CoreFiles = $coreFiles
        PackageEntries = $packageEntries
    }
}

$windows = Read-EvidenceSet -Directory $windowsRoot -ExpectedPlatform 'windows'
$linux = Read-EvidenceSet -Directory $linuxRoot -ExpectedPlatform 'linux'

foreach ($property in @('packageId', 'packageVersion', 'expectedTag', 'sourceRevision')) {
    if ($windows.Evidence.$property -ne $linux.Evidence.$property) {
        throw "Windows and Linux release evidence disagree on '$property'."
    }
}
if ($windows.DryRun.dotnetSdkVersion -ne $linux.DryRun.dotnetSdkVersion) {
    throw 'Windows and Linux used different .NET SDK versions.'
}
if ($windows.CoreFiles.Count -ne $linux.CoreFiles.Count) {
    throw 'Windows and Linux produced different portable file sets.'
}
if ($windows.PackageEntries.Count -ne $linux.PackageEntries.Count) {
    throw 'Windows and Linux produced different package entry sets.'
}

$portableFiles = @()
$hashMismatches = @()
$packageEntryNames = [string[]]@($windows.PackageEntries.Keys)
[Array]::Sort($packageEntryNames, [System.StringComparer]::Ordinal)
foreach ($entryName in $packageEntryNames) {
    if (-not $linux.PackageEntries.ContainsKey($entryName)) {
        throw "Linux evidence is missing package entry '$entryName'."
    }
    $windowsEntry = $windows.PackageEntries[$entryName]
    $linuxEntry = $linux.PackageEntries[$entryName]
    if ($windowsEntry.Bytes -ne $linuxEntry.Bytes -or $windowsEntry.Sha256 -ne $linuxEntry.Sha256) {
        $hashMismatches += "package entry ${entryName}: $($windowsEntry.Bytes)/$($windowsEntry.Sha256) != $($linuxEntry.Bytes)/$($linuxEntry.Sha256)"
    }
}

$portableFileNames = [string[]]@($windows.CoreFiles.Keys)
[Array]::Sort($portableFileNames, [System.StringComparer]::Ordinal)
foreach ($fileName in $portableFileNames) {
    if (-not $linux.CoreFiles.ContainsKey($fileName)) {
        throw "Linux evidence is missing portable file '$fileName'."
    }
    if ($windows.CoreFiles[$fileName] -ne $linux.CoreFiles[$fileName]) {
        $hashMismatches += "${fileName}: $($windows.CoreFiles[$fileName]) != $($linux.CoreFiles[$fileName])"
        continue
    }
    $portableFiles += [ordered]@{
        path = $fileName
        sha256 = $windows.CoreFiles[$fileName]
    }
}
if ($hashMismatches.Count -gt 0) {
    throw "Cross-platform hash mismatches:`n$($hashMismatches -join "`n")"
}

$comparison = [ordered]@{
    format = 'flow-cli-cross-platform-release-comparison-0.1'
    packageId = $windows.Evidence.packageId
    packageVersion = $windows.Evidence.packageVersion
    expectedTag = $windows.Evidence.expectedTag
    sourceRevision = $windows.Evidence.sourceRevision
    dotnetSdkVersion = $windows.DryRun.dotnetSdkVersion
    platforms = @('linux', 'windows')
    portableFiles = @($portableFiles)
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

Write-Host "Windows/Linux release hashes match for $($portableFiles.Count) portable files: $comparisonPath"
