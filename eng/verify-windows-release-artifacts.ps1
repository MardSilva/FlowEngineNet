[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CanonicalArtifactsDirectory,
    [Parameter(Mandatory)][string]$PortableArtifactsDirectory,
    [Parameter(Mandatory)][string]$InstallerArtifactsDirectory,
    [string]$OutputDirectory,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $allowedArtifactsRoot 'windows-release-validation'
}

$comparison = if ($env:OS -eq 'Windows_NT') {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}

function Resolve-ArtifactDirectory {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Description)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, $comparison) -or
        -not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "$Description must be an existing directory below '$allowedArtifactsRoot': $resolved"
    }
    $item = Get-Item -LiteralPath $resolved -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Description cannot be a reparse point: $resolved"
    }
    return $resolved
}

function Get-JsonFile {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required Windows release evidence is missing: $Path"
    }
    return Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
}

function Get-Sha256Lower {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-Checksums {
    param([Parameter(Mandatory)][string]$Directory)

    $checksumsPath = Join-Path $Directory 'SHA256SUMS'
    if (-not (Test-Path -LiteralPath $checksumsPath -PathType Leaf)) {
        throw "SHA256SUMS is missing: $checksumsPath"
    }
    $entries = @()
    $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($line in Get-Content -LiteralPath $checksumsPath -Encoding UTF8) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        if ($line -notmatch '^([0-9a-f]{64})  ([^/\\]+)$' -or -not $names.Add($Matches[2])) {
            throw "Invalid or duplicate SHA256SUMS entry: $line"
        }
        $path = Join-Path $Directory $Matches[2]
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Sha256Lower -Path $path) -ne $Matches[1]) {
            throw "Checksum validation failed: $($Matches[2])"
        }
        $entries += [ordered]@{ name = $Matches[2]; sha256 = $Matches[1]; bytes = (Get-Item -LiteralPath $path).Length }
    }
    if ($entries.Count -eq 0) {
        throw "SHA256SUMS contains no entries: $checksumsPath"
    }
    return @($entries | Sort-Object { $_.name })
}

function Write-Utf8Lf {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Content)

    $normalized = $Content.Replace("`r`n", "`n").Replace("`r", "`n")
    if (-not $normalized.EndsWith("`n", [System.StringComparison]::Ordinal)) {
        $normalized += "`n"
    }
    [System.IO.File]::WriteAllText($Path, $normalized, [System.Text.UTF8Encoding]::new($false))
}

$canonicalRoot = Resolve-ArtifactDirectory -Path $CanonicalArtifactsDirectory -Description 'Canonical artifacts directory'
$portableRoot = Resolve-ArtifactDirectory -Path $PortableArtifactsDirectory -Description 'Portable artifacts directory'
$installerRoot = Resolve-ArtifactDirectory -Path $InstallerArtifactsDirectory -Description 'Installer artifacts directory'
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not $outputRoot.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, $comparison)) {
    throw "The Windows release validation output must be below '$allowedArtifactsRoot'."
}
if (Test-Path -LiteralPath $outputRoot) {
    $existing = Get-Item -LiteralPath $outputRoot -Force
    if (($existing.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The Windows release validation output cannot be a reparse point: $outputRoot"
    }
    if (-not $Force) {
        throw "The Windows release validation output already exists. Pass -Force to replace it: $outputRoot"
    }
}

$canonicalEvidence = Get-JsonFile -Path (Join-Path $canonicalRoot 'release-evidence.json')
$canonicalManifest = Get-JsonFile -Path (Join-Path $canonicalRoot 'release-manifest.json')
$portableManifest = Get-JsonFile -Path (Join-Path $portableRoot 'portable-manifest.json')
$portableSmoke = Get-JsonFile -Path (Join-Path $portableRoot 'portable-smoke-result.json')
$installerManifest = Get-JsonFile -Path (Join-Path $installerRoot 'installer-manifest.json')
$installerSmoke = Get-JsonFile -Path (Join-Path $installerRoot 'installer-smoke-result.json')

if ($canonicalEvidence.format -ne 'flow-cli-release-evidence-0.1' -or
    $canonicalEvidence.sourceState -ne 'clean' -or
    -not $canonicalEvidence.releaseReady -or
    $canonicalEvidence.publicationPerformed) {
    throw 'The canonical CLI candidate is not clean, release-ready evidence.'
}
if ($portableManifest.format -ne 'flow-windows-portable-0.1' -or
    $portableSmoke.format -ne 'flow-windows-portable-smoke-0.1' -or
    $portableSmoke.status -ne 'passed' -or
    $installerManifest.format -ne 'flow-windows-installer-0.1' -or
    $installerSmoke.format -ne 'flow-windows-installer-smoke-0.1' -or
    $installerSmoke.status -ne 'passed') {
    throw 'The portable, installer or installer-smoke evidence has an unsupported format or status.'
}

$version = [string]$canonicalEvidence.packageVersion
$revision = [string]$canonicalEvidence.sourceRevision
$actualRevision = (& git -C $repositoryRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $actualRevision -ne $revision) {
    throw 'The canonical evidence does not identify the checked-out source revision.'
}
if ($canonicalManifest.format -ne 'flow-cli-release-manifest-0.1' -or
    $canonicalManifest.packageId -ne $canonicalEvidence.packageId -or
    $canonicalManifest.packageVersion -ne $version) {
    throw 'The canonical release manifest and evidence do not share one package identity.'
}
if ($portableManifest.version -ne $version -or
    $portableSmoke.version -ne $version -or
    $installerManifest.publicVersion -ne $version -or
    $installerSmoke.productionVersion -ne $version) {
    throw 'The canonical package, portable ZIP, MSI and smoke evidence do not share one public version.'
}
if ($portableManifest.sourceRevision -ne $revision -or
    $portableSmoke.sourceRevision -ne $revision -or
    $installerManifest.sourceRevision -ne $revision) {
    throw 'The canonical package, portable ZIP and MSI do not share one source revision.'
}
if ($portableManifest.product -ne 'Flow Engine .NET' -or
    $installerManifest.product -ne 'Flow Engine .NET' -or
    $canonicalEvidence.packageId -ne 'FlowEngineNet.Tool') {
    throw 'The release artifacts do not match the expected Flow product identity.'
}
if ($portableManifest.runtimeIdentifier -ne 'win-x64' -or
    $installerManifest.runtimeIdentifier -ne 'win-x64' -or
    $portableManifest.architecture -ne 'x64' -or
    $installerManifest.architecture -ne 'x64' -or
    $installerManifest.scope -ne 'perUser' -or
    $installerManifest.signed) {
    throw 'The Windows distribution identity, scope or unsigned-state declaration is invalid.'
}
if ($installerSmoke.productionInstallerVersion -ne $installerManifest.installerVersion) {
    throw 'The installer smoke evidence does not identify the generated MSI version.'
}
$windowsPropertiesPath = Join-Path $repositoryRoot 'eng/Flow.WindowsProduct.props'
$windowsProperties = [xml](Get-Content -LiteralPath $windowsPropertiesPath -Raw -Encoding UTF8)
$properties = @{}
foreach ($element in $windowsProperties.Project.PropertyGroup.ChildNodes) {
    if ($element.NodeType -eq [System.Xml.XmlNodeType]::Element -and -not $properties.ContainsKey($element.Name)) {
        $properties[$element.Name] = [string]$element.InnerText
    }
}
if ($installerManifest.installerVersion -ne $properties.FlowWindowsInstallerVersion -or
    $installerManifest.productCode -ne $properties.FlowWindowsProductCode -or
    $installerManifest.upgradeCode -ne $properties.FlowWindowsUpgradeCode -or
    $installerManifest.runtimeIdentifier -ne $properties.FlowWindowsRuntimeIdentifier) {
    throw 'The MSI manifest does not match the checked-in Windows product identity.'
}
$requiredOperations = @('clean-install', 'installed-cli', 'repair', 'major-upgrade', 'downgrade-refused', 'uninstall', 'path-preserved')
foreach ($operation in $requiredOperations) {
    if ($operation -notin @($installerSmoke.operations)) {
        throw "The installer smoke evidence is missing operation '$operation'."
    }
}

$canonicalFiles = Test-Checksums -Directory $canonicalRoot
$portableFiles = Test-Checksums -Directory $portableRoot
$installerFiles = Test-Checksums -Directory $installerRoot
$portableZip = @(Get-ChildItem -LiteralPath $portableRoot -Filter '*.zip' -File)
if ($portableZip.Count -ne 1) {
    throw 'The portable distribution must contain exactly one ZIP archive.'
}
$msiFiles = @(Get-ChildItem -LiteralPath $installerRoot -Filter '*.msi' -File)
if ($msiFiles.Count -ne 2) {
    throw 'The installer distribution must contain exactly two localized MSI packages.'
}

$stagingRoot = Join-Path $allowedArtifactsRoot ('.flow-windows-release-validation-' + [Guid]::NewGuid().ToString('N'))
$extractRoot = Join-Path $stagingRoot 'portable-payload'
$resultRoot = Join-Path $stagingRoot 'result'
try {
    New-Item -ItemType Directory -Path $extractRoot, $resultRoot -Force | Out-Null
    [System.IO.Compression.ZipFile]::ExtractToDirectory($portableZip[0].FullName, $extractRoot)
    $payloadFiles = @(Get-ChildItem -LiteralPath $extractRoot -File | Sort-Object Name)
    $declaredPayload = @($installerManifest.payloadFiles | Sort-Object name)
    if ($payloadFiles.Count -ne 3 -or $declaredPayload.Count -ne $payloadFiles.Count) {
        throw 'The portable ZIP and installer manifest do not declare the same three-file payload.'
    }
    for ($index = 0; $index -lt $payloadFiles.Count; $index++) {
        $actual = $payloadFiles[$index]
        $declared = $declaredPayload[$index]
        if ($actual.Name -ne $declared.name -or
            $actual.Length -ne $declared.bytes -or
            (Get-Sha256Lower -Path $actual.FullName) -ne $declared.sha256) {
            throw "The MSI payload differs from the promoted portable payload: $($actual.Name)"
        }
    }

    $evidenceFileName = "FlowEngineNet.$version.windows-release-evidence.json"
    $checksumsFileName = "FlowEngineNet.$version.windows-SHA256SUMS"
    $evidencePath = Join-Path $resultRoot $evidenceFileName
    $checksumsPath = Join-Path $resultRoot $checksumsFileName
    $evidence = [ordered]@{
        format = 'flow-windows-release-evidence-0.1'
        product = 'Flow Engine .NET'
        packageId = [string]$canonicalEvidence.packageId
        publicVersion = $version
        installerVersion = [string]$installerManifest.installerVersion
        sourceRevision = $revision
        runtimeIdentifier = 'win-x64'
        architecture = 'x64'
        installer = [ordered]@{
            productCode = [string]$installerManifest.productCode
            upgradeCode = [string]$installerManifest.upgradeCode
            scope = 'perUser'
            signed = $false
            cultures = @($installerManifest.cultures)
        }
        validation = [ordered]@{
            canonicalPackage = 'passed'
            portableChecksums = 'passed'
            portableSmoke = 'passed'
            installerChecksums = 'passed'
            declaredPayloadMatch = 'passed'
            silentInstall = 'passed'
            installedCli = 'passed'
            repair = 'passed'
            majorUpgrade = 'passed'
            downgradeRefusal = 'passed'
            uninstall = 'passed'
            ownedRemoval = 'passed'
        }
        artifacts = [ordered]@{
            canonical = @($canonicalFiles)
            portable = @($portableFiles)
            installer = @($installerFiles)
        }
    }
    Write-Utf8Lf -Path $evidencePath -Content ($evidence | ConvertTo-Json -Depth 12)

    $publicFiles = @(
        $portableZip[0].FullName
        (Get-ChildItem -LiteralPath $portableRoot -Filter '*.cdx.json' -File).FullName
        (Join-Path $portableRoot 'portable-manifest.json')
        (Join-Path $portableRoot 'portable-smoke-result.json')
        $msiFiles.FullName
        (Get-ChildItem -LiteralPath $installerRoot -Filter '*.cdx.json' -File).FullName
        (Join-Path $installerRoot 'installer-manifest.json')
        (Join-Path $installerRoot 'installer-smoke-result.json')
        $evidencePath
    )
    $checksumLines = @($publicFiles | Sort-Object { [System.IO.Path]::GetFileName($_) } | ForEach-Object {
        "$(Get-Sha256Lower -Path $_)  $([System.IO.Path]::GetFileName($_))"
    })
    Write-Utf8Lf -Path $checksumsPath -Content ($checksumLines -join "`n")

    if (Test-Path -LiteralPath $outputRoot) {
        Remove-Item -LiteralPath $outputRoot -Recurse -Force
    }
    Move-Item -LiteralPath $resultRoot -Destination $outputRoot
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}

Write-Output "Windows release artifacts validated: $version [$revision]"
