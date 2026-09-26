[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CanonicalArtifactsDirectory,
    [Parameter(Mandatory)][string]$PortableArtifactsDirectory,
    [Parameter(Mandatory)][string]$CombinedPayloadDirectory,
    [Parameter(Mandatory)][string]$InstallerArtifactsDirectory,
    [string]$OutputDirectory,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows-release-contract.ps1')
. (Join-Path $PSScriptRoot 'windows-installer-test-support.ps1')
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

function Test-CombinedPayloadChecksums {
    param([Parameter(Mandatory)][string]$Directory)

    $checksumsPath = Join-Path $Directory 'SHA256SUMS'
    if (-not (Test-Path -LiteralPath $checksumsPath -PathType Leaf)) {
        throw "Combined payload SHA256SUMS is missing: $checksumsPath"
    }
    $entries = @()
    $names = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($line in Get-Content -LiteralPath $checksumsPath -Encoding UTF8) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^([0-9a-f]{64})  (.+)$') {
            throw "Invalid combined payload SHA256SUMS entry: $line"
        }
        $name = $Matches[2].Replace('\', '/')
        if ($name.StartsWith('/', [System.StringComparison]::Ordinal) -or
            $name.Split('/') -contains '..' -or
            -not $names.Add($name)) {
            throw "Unsafe or duplicate combined payload checksum path: $name"
        }
        $path = Join-Path $Directory $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Sha256Lower -Path $path) -ne $Matches[1]) {
            throw "Combined payload checksum validation failed: $name"
        }
        $entries += [ordered]@{ name = $name; sha256 = $Matches[1]; bytes = (Get-Item -LiteralPath $path).Length }
    }

    $expected = @('combined-payload-manifest.json') + @(Get-ChildItem -LiteralPath (Join-Path $Directory 'payload') -File -Recurse |
        ForEach-Object { [System.IO.Path]::GetRelativePath($Directory, $_.FullName).Replace('\', '/') })
    if ([string]::Join("`n", @($names | Sort-Object)) -cne [string]::Join("`n", @($expected | Sort-Object))) {
        throw 'Combined payload SHA256SUMS does not cover exactly the promoted artifact.'
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
$combinedRoot = Resolve-ArtifactDirectory -Path $CombinedPayloadDirectory -Description 'Combined payload directory'
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
$combinedManifestPath = Join-Path $combinedRoot 'combined-payload-manifest.json'
$combinedManifest = Get-JsonFile -Path $combinedManifestPath
$combinedSmokePath = Join-Path $combinedRoot 'combined-payload-smoke-result.json'
$combinedSmoke = Get-JsonFile -Path $combinedSmokePath
Test-CombinedSmokeEvidence -Smoke $combinedSmoke -Manifest $combinedManifest -ManifestSha256 (Get-Sha256Lower -Path $combinedManifestPath)
$combinedVersion = Get-JsonFile -Path (Join-Path $combinedRoot 'payload/VERSION.json')
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
    $combinedManifest.format -ne 'flow-windows-combined-payload-0.1' -or
    $installerManifest.format -ne 'flow-windows-installer-0.2' -or
    $installerSmoke.format -ne 'flow-windows-installer-smoke-0.2' -or
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
    $combinedManifest.version -ne $version -or
    $combinedVersion.version -ne $version -or
    $installerManifest.publicVersion -ne $version -or
    $installerSmoke.productionVersion -ne $version) {
    throw 'The canonical package, portable ZIP, MSI and smoke evidence do not share one public version.'
}
if ($portableManifest.sourceRevision -ne $revision -or
    $portableSmoke.sourceRevision -ne $revision -or
    $combinedManifest.sourceRevision -ne $revision -or
    $combinedVersion.sourceRevision -ne $revision -or
    $installerManifest.sourceRevision -ne $revision) {
    throw 'The canonical package, portable ZIP, combined payload and MSI do not share one source revision.'
}
if ($portableManifest.product -ne 'Flow Engine .NET' -or
    $combinedManifest.product -ne 'Flow Engine .NET' -or
    $installerManifest.product -ne 'Flow Engine .NET' -or
    $canonicalEvidence.packageId -ne 'FlowEngineNet.Tool') {
    throw 'The release artifacts do not match the expected Flow product identity.'
}
if ($portableManifest.runtimeIdentifier -ne 'win-x64' -or
    $combinedManifest.runtimeIdentifier -ne 'win-x64' -or
    $installerManifest.runtimeIdentifier -ne 'win-x64' -or
    $portableManifest.architecture -ne 'x64' -or
    $combinedManifest.architecture -ne 'x64' -or
    $installerManifest.architecture -ne 'x64' -or
    $installerManifest.scope -ne 'perUser' -or
    $combinedManifest.sourceTreeDirty -or
    $installerManifest.sourceTreeDirty -or
    $installerManifest.signed) {
    throw 'The Windows distribution identity, scope or unsigned-state declaration is invalid.'
}
if ($installerSmoke.productionInstallerVersion -ne $installerManifest.installerVersion) {
    throw 'The installer smoke evidence does not identify the generated MSI version.'
}
if ($installerSmoke.installerManifestSha256 -ne (Get-Sha256Lower -Path (Join-Path $installerRoot 'installer-manifest.json')) -or
    $installerSmoke.sourceRevision -ne $installerManifest.sourceRevision -or
    $installerSmoke.testedVersion -ne $installerManifest.publicVersion -or
    $installerSmoke.baselineVersion -ne '0.2.0-alpha.3' -or
    $installerSmoke.baselineRevision -ne '1a2fbc50a7a79bc1f9fdccbb7347b39949743bbe' -or
    (@($installerSmoke.testedCultures | Sort-Object) -join ',') -ne 'en-US,pt-BR' -or
    @($installerSmoke.phases).Count -ne 7 -or @($installerSmoke.phases | Where-Object { -not $_.passed }).Count -ne 0) {
    throw 'Installer lifecycle evidence is incomplete or belongs to a different candidate.'
}
foreach ($phaseName in @('clean-alpha4.log', 'repair-alpha4.log', 'remove-clean-alpha4.log', 'install-alpha3.log',
        'upgrade-alpha3-alpha4.log', 'downgrade.log', 'uninstall.log')) {
    $phase = @($installerSmoke.phases | Where-Object log -EQ $phaseName)
    $allowedCodes = if ($phaseName -eq 'downgrade.log') { @(1603,1638) } else { @(0,3010) }
    if ($phase.Count -ne 1 -or $phase[0].exitCode -notin $allowedCodes) { throw "Missing or failed installer phase: $phaseName" }
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
$requiredOperations = @('clean-install', 'installed-application', 'start-menu-launch', 'installed-cli', 'repair', 'major-upgrade',
    'downgrade-refused', 'uninstall', 'user-data-preserved', 'path-preserved', 'alpha3-to-alpha4', 'powershell-cli', 'cmd-cli', 'public-epub', 'settings-preserved')
foreach ($operation in $requiredOperations) {
    if ($operation -notin @($installerSmoke.operations)) {
        throw "The installer smoke evidence is missing operation '$operation'."
    }
}

$canonicalFiles = Test-Checksums -Directory $canonicalRoot
$portableFiles = Test-Checksums -Directory $portableRoot
$combinedFiles = Test-CombinedPayloadChecksums -Directory $combinedRoot
$installerFiles = Test-Checksums -Directory $installerRoot
Get-TestMsiPackages -Directory $installerRoot -Manifest $installerManifest -ExpectedCultures @('en-US', 'pt-BR') | Out-Null
$installerSboms = @(Get-ChildItem -LiteralPath $installerRoot -Filter '*.cdx.json' -File)
if ($installerSboms.Count -ne 1 -or $installerSboms[0].Name -notin @($installerFiles.name)) {
    throw 'Exactly one checksummed installer SBOM is required.'
}
Test-InstallerSbom -Sbom (Get-JsonFile -Path $installerSboms[0].FullName) -Manifest $installerManifest
$combinedPayloadRoot = Join-Path $combinedRoot 'payload'
$combinedActualPaths = @(Get-ChildItem -LiteralPath $combinedPayloadRoot -File -Recurse |
    ForEach-Object { [System.IO.Path]::GetRelativePath($combinedPayloadRoot, $_.FullName).Replace('\', '/') } | Sort-Object)
$combinedDeclaredPaths = @($combinedManifest.files | ForEach-Object { [string]$_.path } | Sort-Object)
if ([string]::Join("`n", $combinedActualPaths) -cne [string]::Join("`n", $combinedDeclaredPaths)) {
    throw 'The combined payload manifest does not cover exactly the promoted payload.'
}
foreach ($entry in $combinedManifest.files) {
    $path = Join-Path $combinedPayloadRoot ([string]$entry.path)
    if ((Get-Item -LiteralPath $path).Length -ne [long]$entry.bytes -or
        (Get-Sha256Lower -Path $path) -ne [string]$entry.sha256) {
        throw "The promoted combined payload failed hash validation: $($entry.path)"
    }
}
$installerDeclaredPaths = @($installerManifest.payloadFiles | ForEach-Object { [string]$_.path } | Sort-Object)
if ([string]::Join("`n", $combinedDeclaredPaths) -cne [string]::Join("`n", $installerDeclaredPaths)) {
    throw 'The MSI manifest does not declare the promoted combined payload.'
}
foreach ($entry in $installerManifest.payloadFiles) {
    $combinedEntry = $combinedManifest.files | Where-Object { $_.path -ceq $entry.path } | Select-Object -First 1
    if ($null -eq $combinedEntry -or [string]$combinedEntry.sha256 -ne [string]$entry.sha256 -or
        [long]$combinedEntry.bytes -ne [long]$entry.bytes) {
        throw "The MSI payload differs from the promoted combined payload: $($entry.path)"
    }
}
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
    foreach ($mapping in @(
            @{ Portable = 'flow.exe'; Combined = 'cli/flow.exe' },
            @{ Portable = 'LICENSE.txt'; Combined = 'LICENSE.txt' })) {
        $portablePath = Join-Path $extractRoot $mapping.Portable
        $combinedPath = Join-Path $combinedPayloadRoot $mapping.Combined
        if ((Get-Sha256Lower -Path $portablePath) -ne (Get-Sha256Lower -Path $combinedPath)) {
            throw "The combined payload did not preserve the promoted portable file: $($mapping.Portable)"
        }
    }

    $evidenceFileName = "FlowEngineNet.$version.windows-release-evidence.json"
    $checksumsFileName = "FlowEngineNet.$version.windows-SHA256SUMS"
    $evidencePath = Join-Path $resultRoot $evidenceFileName
    $checksumsPath = Join-Path $resultRoot $checksumsFileName
    $evidence = [ordered]@{
        format = 'flow-windows-release-evidence-0.2'
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
            combinedPayloadHashes = 'passed'
            installerChecksums = 'passed'
            declaredPayloadMatch = 'passed'
            combinedApplicationSmoke = 'passed'
            installerSbom = 'passed'
            silentInstall = 'passed'
            installedApplication = 'passed'
            startMenuLaunch = 'passed'
            installedCli = 'passed'
            repair = 'passed'
            majorUpgrade = 'passed'
            downgradeRefusal = 'passed'
            uninstall = 'passed'
            ownedRemoval = 'passed'
            userDataPreserved = 'passed'
        }
        artifacts = [ordered]@{
            canonical = @($canonicalFiles)
            portable = @($portableFiles)
            combined = @($combinedFiles)
            installer = @($installerFiles)
        }
    }
    Write-Utf8Lf -Path $evidencePath -Content ($evidence | ConvertTo-Json -Depth 12)

    $publicFiles = @(
        $portableZip[0].FullName
        (Get-ChildItem -LiteralPath $portableRoot -Filter '*.cdx.json' -File).FullName
        (Join-Path $portableRoot 'portable-manifest.json')
        (Join-Path $portableRoot 'portable-smoke-result.json')
        $combinedManifestPath
        $combinedSmokePath
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
