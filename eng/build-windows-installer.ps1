[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [string]$PayloadDirectory,
    [string]$InstallerVersion,
    [string]$UpgradeCode,
    [string]$ProductCode,
    [string]$ExecutableComponentGuid,
    [string]$InstallDirectoryName = 'FlowEngineNet',
    [string]$ProductRegistryKey = 'Software\FlowEngineNet\Installer',
    [string]$ProductName,
    [ValidateSet('en-US', 'pt-BR')]
    [string[]]$Cultures = @('en-US', 'pt-BR'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') {
    throw 'The MSI builder must run on Windows.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'installer/Flow.WindowsInstaller/Flow.WindowsInstaller.wixproj'
$cliProjectPath = Join-Path $repositoryRoot 'src/Flow.Cli/Flow.Cli.csproj'
$portableBuilder = Join-Path $PSScriptRoot 'build-windows-portable.ps1'
$productIcon = Join-Path $repositoryRoot 'assets/branding/windows/flow.ico'
$licenseRtf = Join-Path $repositoryRoot 'installer/Flow.WindowsInstaller/License.rtf'
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $allowedArtifactsRoot 'windows-installer'
}
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not $outputRoot.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The installer output directory must be a child of '$allowedArtifactsRoot'."
}
if (Test-Path -LiteralPath $outputRoot) {
    $existingOutput = Get-Item -LiteralPath $outputRoot -Force
    if (($existingOutput.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The installer output directory cannot be a reparse point: $outputRoot"
    }
    if (-not $Force) {
        throw "The installer output directory already exists. Pass -Force to replace it: $outputRoot"
    }
}

function Write-Utf8Lf {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Content)
    $normalized = $Content.Replace("`r`n", "`n").Replace("`r", "`n")
    if (-not $normalized.EndsWith("`n", [System.StringComparison]::Ordinal)) {
        $normalized += "`n"
    }
    [System.IO.File]::WriteAllText($Path, $normalized, [System.Text.UTF8Encoding]::new($false))
}

function Get-MsBuildProperty {
    param([Parameter(Mandatory)][string]$Name)
    $value = (& dotnet msbuild $cliProjectPath -nologo "-getProperty:$Name").Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value)) {
        throw "Could not resolve MSBuild property '$Name'."
    }
    return $value
}

function Get-Sha256Lower {
    param([Parameter(Mandatory)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-GuidText {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Value)
    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParse($Value, [ref]$parsed) -or $parsed -eq [Guid]::Empty) {
        throw "$Name must be a non-empty GUID: $Value"
    }
}

function Invoke-WixBuild {
    param(
        [Parameter(Mandatory)][string]$Culture,
        [Parameter(Mandatory)][string]$Language,
        [Parameter(Mandatory)][string]$BuildDirectory,
        [Parameter(Mandatory)][string]$Payload,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$PublicVersion,
        [Parameter(Mandatory)][string]$Upgrade,
        [Parameter(Mandatory)][string]$Product,
        [Parameter(Mandatory)][string]$ComponentGuid,
        [Parameter(Mandatory)][string]$Name
    )
    $outputName = "FlowEngineNet.Setup.$Culture"
    & dotnet build $projectPath --configuration $Configuration --no-incremental --output $BuildDirectory `
        "-p:PayloadDirectory=$Payload" "-p:ProductName=$Name" `
        '-p:Manufacturer=Flow Engine contributors' `
        '-p:SupportUrl=https://github.com/MardSilva/FlowEngineNet' `
        "-p:PublicVersion=$PublicVersion" "-p:InstallerVersion=$Version" `
        "-p:UpgradeCode=$Upgrade" "-p:ProductCode=$Product" `
        "-p:ExecutableComponentGuid=$ComponentGuid" `
        "-p:ProductLanguage=$Language" "-p:InstallerCulture=$Culture" `
        "-p:InstallDirectoryName=$InstallDirectoryName" `
        "-p:ProductRegistryKey=$ProductRegistryKey" "-p:ProductIcon=$productIcon" `
        "-p:LicenseRtf=$licenseRtf" "-p:InstallerOutputName=$outputName" | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "WiX failed while building the $Culture MSI."
    }
    $msiPath = Join-Path $BuildDirectory "$Culture/$outputName.msi"
    if (-not (Test-Path -LiteralPath $msiPath -PathType Leaf)) {
        throw "WiX did not produce the expected MSI: $msiPath"
    }
    return $msiPath
}

New-Item -ItemType Directory -Path $allowedArtifactsRoot -Force | Out-Null
$stagingRoot = Join-Path $allowedArtifactsRoot ('.flow-windows-installer-' + [Guid]::NewGuid().ToString('N'))
$resultDirectory = Join-Path $stagingRoot 'result'
$portableDirectory = Join-Path $stagingRoot 'portable'
$payloadRoot = Join-Path $stagingRoot 'payload'
$buildRoot = Join-Path $stagingRoot 'build'

try {
    New-Item -ItemType Directory -Path $resultDirectory, $buildRoot -Force | Out-Null
    $publicVersion = Get-MsBuildProperty -Name 'FlowPublicVersion'
    if ([string]::IsNullOrWhiteSpace($InstallerVersion)) {
        $InstallerVersion = Get-MsBuildProperty -Name 'FlowWindowsInstallerVersion'
    }
    if ([string]::IsNullOrWhiteSpace($UpgradeCode)) {
        $UpgradeCode = Get-MsBuildProperty -Name 'FlowWindowsUpgradeCode'
    }
    if ([string]::IsNullOrWhiteSpace($ProductCode)) {
        $ProductCode = Get-MsBuildProperty -Name 'FlowWindowsProductCode'
    }
    if ([string]::IsNullOrWhiteSpace($ExecutableComponentGuid)) {
        $ExecutableComponentGuid = Get-MsBuildProperty -Name 'FlowWindowsExecutableComponentGuid'
    }
    if ([string]::IsNullOrWhiteSpace($ProductName)) {
        $ProductName = Get-MsBuildProperty -Name 'FlowProductName'
    }
    $wixVersion = Get-MsBuildProperty -Name 'FlowWindowsInstallerToolVersion'
    $wixLicense = Get-MsBuildProperty -Name 'FlowWindowsInstallerToolLicense'
    $runtimeIdentifier = Get-MsBuildProperty -Name 'FlowWindowsRuntimeIdentifier'
    $installerVersionValue = [Version]::new($InstallerVersion)
    if ($installerVersionValue.Revision -gt 0 -or $installerVersionValue.Major -gt 255 -or $installerVersionValue.Minor -gt 255) {
        throw "InstallerVersion is outside the supported three-part MSI range: $InstallerVersion"
    }
    Test-GuidText -Name 'UpgradeCode' -Value $UpgradeCode
    Test-GuidText -Name 'ProductCode' -Value $ProductCode
    Test-GuidText -Name 'ExecutableComponentGuid' -Value $ExecutableComponentGuid
    if ($InstallDirectoryName.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0 -or [string]::IsNullOrWhiteSpace($InstallDirectoryName)) {
        throw "InstallDirectoryName is invalid: $InstallDirectoryName"
    }
    if (-not $ProductRegistryKey.StartsWith('Software\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'ProductRegistryKey must stay below HKCU\Software.'
    }

    if ([string]::IsNullOrWhiteSpace($PayloadDirectory)) {
        & pwsh -NoProfile -ExecutionPolicy Bypass -File $portableBuilder -OutputDirectory $portableDirectory
        if ($LASTEXITCODE -ne 0) {
            throw 'The portable payload build failed.'
        }
        $portableZip = @(Get-ChildItem -LiteralPath $portableDirectory -Filter '*.zip' -File)
        if ($portableZip.Count -ne 1) {
            throw 'The portable build did not produce exactly one ZIP.'
        }
        [System.IO.Compression.ZipFile]::ExtractToDirectory($portableZip[0].FullName, $payloadRoot)
    }
    else {
        $payloadRoot = [System.IO.Path]::GetFullPath($PayloadDirectory)
        if (-not $payloadRoot.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $payloadRoot -PathType Container)) {
            throw "A supplied payload must be an existing directory below '$allowedArtifactsRoot'."
        }
    }
    $payloadFiles = @(Get-ChildItem -LiteralPath $payloadRoot -File)
    if (@($payloadFiles.Name | Sort-Object) -join '|' -ne 'flow.exe|LICENSE.txt|VERSION.json') {
        throw 'The MSI payload must contain only flow.exe, LICENSE.txt and VERSION.json.'
    }
    $payloadVersion = Get-Content -LiteralPath (Join-Path $payloadRoot 'VERSION.json') -Raw | ConvertFrom-Json
    if ($payloadVersion.version -ne $publicVersion -or $payloadVersion.runtimeIdentifier -ne $runtimeIdentifier) {
        throw 'The portable payload does not match the installer version or RID contract.'
    }
    $revision = [string]$payloadVersion.sourceRevision
    if ($revision -notmatch '^[0-9a-f]{40}$') {
        throw 'The payload source revision is invalid.'
    }

    $installerFiles = @()
    foreach ($culture in $Cultures) {
        $language = if ($culture -eq 'pt-BR') { '1046' } else { '1033' }
        $cultureBuildDirectory = Join-Path $buildRoot $culture
        New-Item -ItemType Directory -Path $cultureBuildDirectory -Force | Out-Null
        $builtMsi = Invoke-WixBuild -Culture $culture -Language $language -BuildDirectory $cultureBuildDirectory -Payload $payloadRoot -Version $InstallerVersion -PublicVersion $publicVersion -Upgrade $UpgradeCode -Product $ProductCode -ComponentGuid $ExecutableComponentGuid -Name $ProductName
        $fileName = "FlowEngineNet.Setup.$publicVersion.$culture.$runtimeIdentifier.msi"
        $destination = Join-Path $resultDirectory $fileName
        Copy-Item -LiteralPath $builtMsi -Destination $destination
        $installerFiles += [ordered]@{ name = $fileName; culture = $culture; productLanguage = [int]$language; sha256 = Get-Sha256Lower -Path $destination; bytes = (Get-Item -LiteralPath $destination).Length }
    }

    $sbomPath = Join-Path $resultDirectory "FlowEngineNet.Setup.$publicVersion.cdx.json"
    $rootReference = "pkg:generic/FlowEngineNet.Setup@$publicVersion"
    $applicationReference = "pkg:generic/FlowEngineNet@$publicVersion"
    $sbom = [ordered]@{
        bomFormat = 'CycloneDX'; specVersion = '1.5'; version = 1
        metadata = [ordered]@{ component = [ordered]@{
            type = 'application'; 'bom-ref' = $rootReference; name = 'FlowEngineNet.Setup'; version = $publicVersion
            licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT' } })
            properties = @([ordered]@{ name = 'flow:installerVersion'; value = $InstallerVersion }, [ordered]@{ name = 'flow:productCode'; value = $ProductCode.ToUpperInvariant() }, [ordered]@{ name = 'flow:sourceRevision'; value = $revision }, [ordered]@{ name = 'flow:runtimeIdentifier'; value = $runtimeIdentifier })
        } }
        components = @(
            [ordered]@{ type = 'application'; 'bom-ref' = $applicationReference; name = 'FlowEngineNet'; version = $publicVersion; hashes = @([ordered]@{ alg = 'SHA-256'; content = (Get-Sha256Lower -Path (Join-Path $payloadRoot 'flow.exe')) }); licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT' } }) }
            [ordered]@{ type = 'application'; 'bom-ref' = "pkg:nuget/WixToolset.Sdk@$wixVersion"; name = 'WixToolset.Sdk'; version = $wixVersion; scope = 'excluded'; licenses = @([ordered]@{ license = [ordered]@{ id = $wixLicense } }); properties = @([ordered]@{ name = 'flow:distributionRole'; value = 'build-tool' }) }
            [ordered]@{ type = 'application'; 'bom-ref' = "pkg:nuget/WixToolset.UI.wixext@$wixVersion"; name = 'WixToolset.UI.wixext'; version = $wixVersion; scope = 'excluded'; licenses = @([ordered]@{ license = [ordered]@{ id = $wixLicense } }); properties = @([ordered]@{ name = 'flow:distributionRole'; value = 'build-tool' }) }
        )
        dependencies = @([ordered]@{ ref = $rootReference; dependsOn = @($applicationReference) })
    }
    Write-Utf8Lf -Path $sbomPath -Content ($sbom | ConvertTo-Json -Depth 12)

    $manifestPath = Join-Path $resultDirectory 'installer-manifest.json'
    $manifest = [ordered]@{
        format = 'flow-windows-installer-0.1'; product = $ProductName; publicVersion = $publicVersion; installerVersion = $InstallerVersion
        sourceRevision = $revision; productCode = $ProductCode.ToUpperInvariant(); upgradeCode = $UpgradeCode.ToUpperInvariant(); runtimeIdentifier = $runtimeIdentifier; architecture = 'x64'; scope = 'perUser'
        installRoot = 'LocalAppDataFolder\Programs'; installDirectoryName = $InstallDirectoryName; pathRegistration = 'current-user'; signed = $false
        cultures = @($Cultures); files = $installerFiles; buildTool = [ordered]@{ name = 'WixToolset.Sdk'; version = $wixVersion; license = $wixLicense }
    }
    Write-Utf8Lf -Path $manifestPath -Content ($manifest | ConvertTo-Json -Depth 10)
    $checksumPaths = @($installerFiles | ForEach-Object { Join-Path $resultDirectory $_.name }) + @($sbomPath, $manifestPath)
    $checksumLines = foreach ($path in $checksumPaths) { "$(Get-Sha256Lower -Path $path)  $([System.IO.Path]::GetFileName($path))" }
    Write-Utf8Lf -Path (Join-Path $resultDirectory 'SHA256SUMS') -Content ($checksumLines -join "`n")

    if (Test-Path -LiteralPath $outputRoot) {
        Remove-Item -LiteralPath $outputRoot -Recurse -Force
    }
    Move-Item -LiteralPath $resultDirectory -Destination $outputRoot
    Write-Output "Windows installer artifacts created: $outputRoot"
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
