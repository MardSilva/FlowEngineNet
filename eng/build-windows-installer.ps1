[CmdletBinding()]
param(
    [ValidateSet('Release')][string]$Configuration = 'Release',
    [string]$OutputDirectory,
    [string]$PayloadDirectory,
    [string]$InstallerVersion,
    [string]$UpgradeCode,
    [string]$ProductCode,
    [string]$ExecutableComponentGuid,
    [string]$RegistrationComponentGuid,
    [string]$MetadataComponentGuid,
    [string]$StartMenuComponentGuid,
    [string]$PayloadComponentNamespace,
    [string]$InstallDirectoryName = 'FlowEngineNet',
    [string]$ProductRegistryKey = 'Software\FlowEngineNet\Installer',
    [string]$ProductName,
    [ValidateSet('en-US', 'pt-BR')][string[]]$Cultures = @('en-US', 'pt-BR'),
    [ValidateSet('none', 'low', 'medium', 'high', 'mszip')][string]$CompressionLevel = 'high',
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') { throw 'The MSI builder must run on Windows.' }

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'installer/Flow.WindowsInstaller/Flow.WindowsInstaller.wixproj'
$cliProjectPath = Join-Path $repositoryRoot 'src/Flow.Cli/Flow.Cli.csproj'
$combinedBuilder = Join-Path $PSScriptRoot 'build-windows-combined-payload.ps1'
$productIcon = Join-Path $repositoryRoot 'assets/branding/windows/flow.ico'
$licenseRtf = Join-Path $repositoryRoot 'installer/Flow.WindowsInstaller/License.rtf'
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $allowedArtifactsRoot 'windows-installer' }
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not $outputRoot.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The installer output directory must be a child of '$allowedArtifactsRoot'."
}
if (Test-Path -LiteralPath $outputRoot) {
    $existingOutput = Get-Item -LiteralPath $outputRoot -Force
    if (($existingOutput.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw "The installer output directory cannot be a reparse point: $outputRoot" }
    if (-not $Force) { throw "The installer output directory already exists. Pass -Force to replace it: $outputRoot" }
}

function Write-Utf8Lf {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Content)
    $normalized = $Content.Replace("`r`n", "`n").Replace("`r", "`n")
    if (-not $normalized.EndsWith("`n", [System.StringComparison]::Ordinal)) { $normalized += "`n" }
    [System.IO.File]::WriteAllText($Path, $normalized, [System.Text.UTF8Encoding]::new($false))
}

function Get-MsBuildProperty {
    param([Parameter(Mandatory)][string]$Name)
    $value = (& dotnet msbuild $cliProjectPath -nologo "-getProperty:$Name").Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value)) { throw "Could not resolve MSBuild property '$Name'." }
    return $value
}

function Get-Sha256Lower {
    param([Parameter(Mandatory)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Test-GuidText {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Value)
    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParse($Value, [ref]$parsed) -or $parsed -eq [Guid]::Empty) { throw "$Name must be a non-empty GUID: $Value" }
}

function Get-StableHex {
    param([Parameter(Mandatory)][string]$Value)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant())
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function Get-StableComponentGuid {
    param([Parameter(Mandatory)][Guid]$Namespace, [Parameter(Mandatory)][string]$RelativePath)
    $namespaceBytes = $Namespace.ToByteArray()
    $pathBytes = [System.Text.Encoding]::UTF8.GetBytes($RelativePath.ToLowerInvariant())
    $input = [byte[]]::new($namespaceBytes.Length + $pathBytes.Length)
    [Array]::Copy($namespaceBytes, 0, $input, 0, $namespaceBytes.Length)
    [Array]::Copy($pathBytes, 0, $input, $namespaceBytes.Length, $pathBytes.Length)
    $hash = [System.Security.Cryptography.SHA256]::HashData($input)
    $guidBytes = [byte[]]$hash[0..15]
    $guidBytes[7] = ($guidBytes[7] -band 0x0f) -bor 0x50
    $guidBytes[8] = ($guidBytes[8] -band 0x3f) -bor 0x80
    return '{' + [Guid]::new($guidBytes).ToString().ToUpperInvariant() + '}'
}

function New-WixApplicationFragment {
    param(
        [Parameter(Mandatory)][string]$ApplicationDirectory,
        [Parameter(Mandatory)][string]$DestinationPath,
        [Parameter(Mandatory)][Guid]$ComponentNamespace
    )

    $directoryIds = @{ '' = 'APPLICATIONFOLDER' }
    foreach ($directory in @(Get-ChildItem -LiteralPath $ApplicationDirectory -Directory -Recurse | Sort-Object FullName)) {
        $relative = [System.IO.Path]::GetRelativePath($ApplicationDirectory, $directory.FullName).Replace('\', '/')
        $directoryIds[$relative] = 'FlowAppDir_' + (Get-StableHex -Value $relative).Substring(0, 20)
    }

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $settings.Indent = $true
    $settings.NewLineChars = "`n"
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace
    $writer = [System.Xml.XmlWriter]::Create($DestinationPath, $settings)
    try {
        $writer.WriteStartDocument()
        $writer.WriteStartElement('Wix', 'http://wixtoolset.org/schemas/v4/wxs')
        $writer.WriteStartElement('Fragment')
        $writer.WriteStartElement('DirectoryRef'); $writer.WriteAttributeString('Id', 'INSTALLFOLDER')
        $writer.WriteStartElement('Directory'); $writer.WriteAttributeString('Id', 'APPLICATIONFOLDER'); $writer.WriteAttributeString('Name', 'app')

        function Write-DirectoryChildren {
            param([string]$ParentPath)
            $parent = if ([string]::IsNullOrEmpty($ParentPath)) { $ApplicationDirectory } else { Join-Path $ApplicationDirectory $ParentPath }
            foreach ($child in @(Get-ChildItem -LiteralPath $parent -Directory | Sort-Object Name)) {
                $relative = [System.IO.Path]::GetRelativePath($ApplicationDirectory, $child.FullName).Replace('\', '/')
                $writer.WriteStartElement('Directory')
                $writer.WriteAttributeString('Id', $directoryIds[$relative])
                $writer.WriteAttributeString('Name', $child.Name)
                Write-DirectoryChildren -ParentPath $relative
                $writer.WriteEndElement()
            }
        }
        Write-DirectoryChildren -ParentPath ''
        $writer.WriteEndElement(); $writer.WriteEndElement(); $writer.WriteEndElement()

        $writer.WriteStartElement('Fragment')
        $writer.WriteStartElement('ComponentGroup'); $writer.WriteAttributeString('Id', 'FlowApplicationComponents')
        foreach ($file in @(Get-ChildItem -LiteralPath $ApplicationDirectory -File -Recurse | Sort-Object FullName)) {
            $relative = [System.IO.Path]::GetRelativePath($ApplicationDirectory, $file.FullName).Replace('\', '/')
            $parent = [System.IO.Path]::GetDirectoryName($relative).Replace('\', '/')
            $hash = Get-StableHex -Value $relative
            $writer.WriteStartElement('Component')
            $writer.WriteAttributeString('Id', 'FlowAppComponent_' + $hash.Substring(0, 20))
            $writer.WriteAttributeString('Directory', $directoryIds[$parent])
            $writer.WriteAttributeString('Guid', (Get-StableComponentGuid -Namespace $ComponentNamespace -RelativePath $relative))
            $writer.WriteStartElement('File')
            $writer.WriteAttributeString('Id', 'FlowAppFile_' + $hash.Substring(0, 20))
            $writer.WriteAttributeString('Source', $file.FullName)
            $writer.WriteAttributeString('Name', $file.Name)
            $writer.WriteAttributeString('Checksum', 'yes')
            $writer.WriteEndElement()
            $writer.WriteStartElement('RegistryValue')
            $writer.WriteAttributeString('Root', 'HKCU')
            $writer.WriteAttributeString('Key', '$(var.ProductRegistryKey)\Components')
            $writer.WriteAttributeString('Name', 'App_' + $hash.Substring(0, 32))
            $writer.WriteAttributeString('Type', 'integer')
            $writer.WriteAttributeString('Value', '1')
            $writer.WriteAttributeString('KeyPath', 'yes')
            $writer.WriteEndElement(); $writer.WriteEndElement()
        }
        $writer.WriteEndElement(); $writer.WriteEndElement(); $writer.WriteEndElement(); $writer.WriteEndDocument()
    }
    finally { $writer.Dispose() }
}

function Invoke-WixBuild {
    param([string]$Culture, [string]$Language, [string]$BuildDirectory, [string]$Payload, [string]$PayloadFragment,
        [string]$Version, [string]$PublicVersion, [string]$Upgrade, [string]$Product, [string]$CliComponent,
        [string]$RegistrationComponent, [string]$MetadataComponent, [string]$StartMenuComponent, [string]$Name,
        [string]$Compression)
    $outputName = "FlowEngineNet.Setup.$Culture"
    & dotnet build $projectPath --configuration $Configuration --no-incremental --output $BuildDirectory `
        "-p:PayloadDirectory=$Payload" "-p:PayloadFragment=$PayloadFragment" "-p:ProductName=$Name" `
        '-p:Manufacturer=Flow Engine contributors' '-p:SupportUrl=https://github.com/MardSilva/FlowEngineNet' `
        "-p:PublicVersion=$PublicVersion" "-p:InstallerVersion=$Version" "-p:CompressionLevel=$Compression" `
        "-p:UpgradeCode=$Upgrade" "-p:ProductCode=$Product" `
        "-p:ExecutableComponentGuid=$CliComponent" "-p:RegistrationComponentGuid=$RegistrationComponent" `
        "-p:MetadataComponentGuid=$MetadataComponent" "-p:StartMenuComponentGuid=$StartMenuComponent" `
        "-p:ProductLanguage=$Language" "-p:InstallerCulture=$Culture" "-p:InstallDirectoryName=$InstallDirectoryName" `
        "-p:ProductRegistryKey=$ProductRegistryKey" "-p:ProductIcon=$productIcon" "-p:LicenseRtf=$licenseRtf" `
        "-p:InstallerOutputName=$outputName" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "WiX failed while building the $Culture MSI." }
    $msiPath = Join-Path $BuildDirectory "$Culture/$outputName.msi"
    if (-not (Test-Path -LiteralPath $msiPath -PathType Leaf)) { throw "WiX did not produce the expected MSI: $msiPath" }
    return $msiPath
}

New-Item -ItemType Directory -Path $allowedArtifactsRoot -Force | Out-Null
$stagingRoot = Join-Path $allowedArtifactsRoot ('.flow-windows-installer-' + [Guid]::NewGuid().ToString('N'))
$resultDirectory = Join-Path $stagingRoot 'result'
$combinedDirectory = Join-Path $stagingRoot 'combined'
$buildRoot = Join-Path $stagingRoot 'build'
$payloadFragment = Join-Path $stagingRoot 'FlowApplicationPayload.wxs'

try {
    New-Item -ItemType Directory -Path $resultDirectory, $buildRoot -Force | Out-Null
    $publicVersion = Get-MsBuildProperty -Name 'FlowPublicVersion'
    $propertyDefaults = @{
        InstallerVersion = 'FlowWindowsInstallerVersion'; UpgradeCode = 'FlowWindowsUpgradeCode'; ProductCode = 'FlowWindowsProductCode'
        ExecutableComponentGuid = 'FlowWindowsExecutableComponentGuid'; RegistrationComponentGuid = 'FlowWindowsRegistrationComponentGuid'
        MetadataComponentGuid = 'FlowWindowsMetadataComponentGuid'; StartMenuComponentGuid = 'FlowWindowsStartMenuComponentGuid'
        PayloadComponentNamespace = 'FlowWindowsPayloadComponentNamespace'; ProductName = 'FlowProductName'
    }
    foreach ($entry in $propertyDefaults.GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace((Get-Variable -Name $entry.Key -ValueOnly))) {
            Set-Variable -Name $entry.Key -Value (Get-MsBuildProperty -Name $entry.Value)
        }
    }
    $wixVersion = Get-MsBuildProperty -Name 'FlowWindowsInstallerToolVersion'
    $wixLicense = Get-MsBuildProperty -Name 'FlowWindowsInstallerToolLicense'
    $runtimeIdentifier = Get-MsBuildProperty -Name 'FlowWindowsRuntimeIdentifier'
    $installerVersionValue = [Version]::new($InstallerVersion)
    if ($installerVersionValue.Revision -gt 0 -or $installerVersionValue.Major -gt 255 -or $installerVersionValue.Minor -gt 255) { throw "InstallerVersion is outside the supported three-part MSI range: $InstallerVersion" }
    foreach ($guidName in @('UpgradeCode', 'ProductCode', 'ExecutableComponentGuid', 'RegistrationComponentGuid', 'MetadataComponentGuid', 'StartMenuComponentGuid', 'PayloadComponentNamespace')) {
        Test-GuidText -Name $guidName -Value (Get-Variable -Name $guidName -ValueOnly)
    }
    if ($InstallDirectoryName.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0 -or [string]::IsNullOrWhiteSpace($InstallDirectoryName)) { throw "InstallDirectoryName is invalid: $InstallDirectoryName" }
    if (-not $ProductRegistryKey.StartsWith('Software\', [System.StringComparison]::OrdinalIgnoreCase)) { throw 'ProductRegistryKey must stay below HKCU\Software.' }

    if ([string]::IsNullOrWhiteSpace($PayloadDirectory)) {
        & pwsh -NoProfile -ExecutionPolicy Bypass -File $combinedBuilder -OutputDirectory $combinedDirectory
        if ($LASTEXITCODE -ne 0) { throw 'The combined Windows payload build failed.' }
        $artifactRoot = $combinedDirectory
        $payloadRoot = Join-Path $artifactRoot 'payload'
    }
    else {
        $supplied = [System.IO.Path]::GetFullPath($PayloadDirectory)
        if (-not $supplied.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $supplied -PathType Container)) {
            throw "A supplied payload must be an existing directory below '$allowedArtifactsRoot'."
        }
        if (Test-Path -LiteralPath (Join-Path $supplied 'combined-payload-manifest.json') -PathType Leaf) {
            $artifactRoot = $supplied; $payloadRoot = Join-Path $supplied 'payload'
        }
        else {
            $payloadRoot = $supplied; $artifactRoot = Split-Path -Parent $supplied
        }
    }
    $combinedManifestPath = Join-Path $artifactRoot 'combined-payload-manifest.json'
    if (-not (Test-Path -LiteralPath $combinedManifestPath -PathType Leaf)) { throw 'The combined payload manifest is missing.' }
    $combinedManifest = Get-Content -LiteralPath $combinedManifestPath -Raw | ConvertFrom-Json
    $payloadVersion = Get-Content -LiteralPath (Join-Path $payloadRoot 'VERSION.json') -Raw | ConvertFrom-Json
    if ($combinedManifest.format -ne 'flow-windows-combined-payload-0.1' -or $combinedManifest.version -ne $publicVersion -or
        $combinedManifest.version -ne $payloadVersion.version -or $combinedManifest.runtimeIdentifier -ne $runtimeIdentifier -or
        $combinedManifest.sourceRevision -ne $payloadVersion.sourceRevision) { throw 'The combined payload does not match the installer version, revision or RID contract.' }

    $payloadFiles = @(Get-ChildItem -LiteralPath $payloadRoot -File -Recurse | Sort-Object FullName)
    $actualPaths = @($payloadFiles | ForEach-Object { [System.IO.Path]::GetRelativePath($payloadRoot, $_.FullName).Replace('\', '/') } | Sort-Object)
    $declaredPaths = @($combinedManifest.files | ForEach-Object { [string]$_.path } | Sort-Object)
    if ([string]::Join("`n", $actualPaths) -cne [string]::Join("`n", $declaredPaths)) { throw 'The combined payload manifest does not cover exactly the MSI input files.' }
    foreach ($entry in $combinedManifest.files) {
        $path = Join-Path $payloadRoot ([string]$entry.path)
        if ((Get-Sha256Lower -Path $path) -ne [string]$entry.sha256 -or (Get-Item -LiteralPath $path).Length -ne [long]$entry.bytes) { throw "Combined payload validation failed: $($entry.path)" }
    }
    foreach ($required in @('app/Flow.Windows.exe', 'cli/flow.exe', 'LICENSE.txt', 'VERSION.json')) {
        if ($required -notin $actualPaths) { throw "The combined MSI payload is missing: $required" }
    }
    $revision = [string]$combinedManifest.sourceRevision
    if ($revision -notmatch '^[0-9a-f]{40}$') { throw 'The payload source revision is invalid.' }

    New-WixApplicationFragment -ApplicationDirectory (Join-Path $payloadRoot 'app') -DestinationPath $payloadFragment -ComponentNamespace ([Guid]$PayloadComponentNamespace)
    $installerFiles = @()
    foreach ($culture in $Cultures) {
        $language = if ($culture -eq 'pt-BR') { '1046' } else { '1033' }
        $cultureBuildDirectory = Join-Path $buildRoot $culture
        New-Item -ItemType Directory -Path $cultureBuildDirectory -Force | Out-Null
        $builtMsi = Invoke-WixBuild -Culture $culture -Language $language -BuildDirectory $cultureBuildDirectory -Payload $payloadRoot -PayloadFragment $payloadFragment `
            -Version $InstallerVersion -PublicVersion $publicVersion -Upgrade $UpgradeCode -Product $ProductCode -CliComponent $ExecutableComponentGuid `
            -RegistrationComponent $RegistrationComponentGuid -MetadataComponent $MetadataComponentGuid -StartMenuComponent $StartMenuComponentGuid `
            -Name $ProductName -Compression $CompressionLevel
        $fileName = "FlowEngineNet.Setup.$publicVersion.$culture.$runtimeIdentifier.msi"
        $destination = Join-Path $resultDirectory $fileName
        Copy-Item -LiteralPath $builtMsi -Destination $destination
        $installerFiles += [ordered]@{ name = $fileName; culture = $culture; productLanguage = [int]$language; sha256 = Get-Sha256Lower -Path $destination; bytes = (Get-Item -LiteralPath $destination).Length }
    }

    $sbomPath = Join-Path $resultDirectory "FlowEngineNet.Setup.$publicVersion.cdx.json"
    $rootReference = "pkg:generic/FlowEngineNet.Setup@$publicVersion"
    $applicationReference = "pkg:generic/FlowEngineNet.Windows@$publicVersion"
    $cliReference = "pkg:generic/FlowEngineNet.Cli@$publicVersion"
    $sbom = [ordered]@{
        bomFormat = 'CycloneDX'; specVersion = '1.5'; version = 1
        metadata = [ordered]@{ component = [ordered]@{ type = 'application'; 'bom-ref' = $rootReference; name = 'FlowEngineNet.Setup'; version = $publicVersion; licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT' } }); properties = @([ordered]@{ name = 'flow:installerVersion'; value = $InstallerVersion }, [ordered]@{ name = 'flow:productCode'; value = $ProductCode.ToUpperInvariant() }, [ordered]@{ name = 'flow:sourceRevision'; value = $revision }, [ordered]@{ name = 'flow:runtimeIdentifier'; value = $runtimeIdentifier }) } }
        components = @(
            [ordered]@{ type = 'application'; 'bom-ref' = $applicationReference; name = 'FlowEngineNet.Windows'; version = $publicVersion; hashes = @([ordered]@{ alg = 'SHA-256'; content = (Get-Sha256Lower -Path (Join-Path $payloadRoot 'app/Flow.Windows.exe')) }); licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT' } }) }
            [ordered]@{ type = 'application'; 'bom-ref' = $cliReference; name = 'FlowEngineNet.Cli'; version = $publicVersion; hashes = @([ordered]@{ alg = 'SHA-256'; content = (Get-Sha256Lower -Path (Join-Path $payloadRoot 'cli/flow.exe')) }); licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT' } }) }
            [ordered]@{ type = 'application'; 'bom-ref' = "pkg:nuget/WixToolset.Sdk@$wixVersion"; name = 'WixToolset.Sdk'; version = $wixVersion; scope = 'excluded'; licenses = @([ordered]@{ license = [ordered]@{ id = $wixLicense } }); properties = @([ordered]@{ name = 'flow:distributionRole'; value = 'build-tool' }) }
            [ordered]@{ type = 'application'; 'bom-ref' = "pkg:nuget/WixToolset.UI.wixext@$wixVersion"; name = 'WixToolset.UI.wixext'; version = $wixVersion; scope = 'excluded'; licenses = @([ordered]@{ license = [ordered]@{ id = $wixLicense } }); properties = @([ordered]@{ name = 'flow:distributionRole'; value = 'build-tool' }) }
        )
        dependencies = @([ordered]@{ ref = $rootReference; dependsOn = @($applicationReference, $cliReference) })
    }
    Write-Utf8Lf -Path $sbomPath -Content ($sbom | ConvertTo-Json -Depth 12)

    $manifestPath = Join-Path $resultDirectory 'installer-manifest.json'
    $manifest = [ordered]@{
        format = 'flow-windows-installer-0.2'; product = $ProductName; publicVersion = $publicVersion; installerVersion = $InstallerVersion
        sourceRevision = $revision; sourceTreeDirty = [bool]$combinedManifest.sourceTreeDirty; productCode = $ProductCode.ToUpperInvariant(); upgradeCode = $UpgradeCode.ToUpperInvariant()
        runtimeIdentifier = $runtimeIdentifier; architecture = 'x64'; scope = 'perUser'; installRoot = 'LocalAppDataFolder\Programs'; installDirectoryName = $InstallDirectoryName
        entryPoints = [ordered]@{ application = 'app/Flow.Windows.exe'; cli = 'flow.exe' }; startMenuShortcut = [ordered]@{ name = $ProductName; target = 'app/Flow.Windows.exe'; cliShortcut = $false }
        pathRegistration = [ordered]@{ scope = 'current-user'; directory = '.' }; signed = $false; cultures = @($Cultures)
        compressionLevel = $CompressionLevel
        payloadFormat = [string]$combinedManifest.format; payloadFiles = @($combinedManifest.files); files = $installerFiles
        buildTool = [ordered]@{ name = 'WixToolset.Sdk'; version = $wixVersion; license = $wixLicense }
    }
    Write-Utf8Lf -Path $manifestPath -Content ($manifest | ConvertTo-Json -Depth 10)
    $checksumPaths = @($installerFiles | ForEach-Object { Join-Path $resultDirectory $_.name }) + @($sbomPath, $manifestPath)
    $checksumLines = foreach ($path in $checksumPaths) { "$(Get-Sha256Lower -Path $path)  $([System.IO.Path]::GetFileName($path))" }
    Write-Utf8Lf -Path (Join-Path $resultDirectory 'SHA256SUMS') -Content ($checksumLines -join "`n")

    if (Test-Path -LiteralPath $outputRoot) { Remove-Item -LiteralPath $outputRoot -Recurse -Force }
    Move-Item -LiteralPath $resultDirectory -Destination $outputRoot
    Write-Output "Windows installer artifacts created: $outputRoot"
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
}
