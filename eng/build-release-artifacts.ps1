[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'src/Flow.Cli/Flow.Cli.csproj'
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $allowedArtifactsRoot 'release'
}

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$runningOnWindows = $env:OS -eq 'Windows_NT'
$pathComparison = if ($runningOnWindows) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}

if (-not $outputRoot.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, $pathComparison)) {
    throw "The release output directory must be a child of '$allowedArtifactsRoot'."
}

$replaceExistingOutput = Test-Path -LiteralPath $outputRoot
if ($replaceExistingOutput) {
    $existingOutput = Get-Item -LiteralPath $outputRoot -Force
    if (($existingOutput.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The release output directory cannot be a reparse point: $outputRoot"
    }

    if (-not $Force) {
        throw "The release output directory already exists. Pass -Force to replace it: $outputRoot"
    }

}

New-Item -ItemType Directory -Path $allowedArtifactsRoot -Force | Out-Null
$stagingRoot = Join-Path $allowedArtifactsRoot ('.flow-release-staging-' + [Guid]::NewGuid().ToString('N'))
$firstPackDirectory = Join-Path $stagingRoot 'pack-a'
$secondPackDirectory = Join-Path $stagingRoot 'pack-b'
$releaseDirectory = Join-Path $stagingRoot 'release'
$toolDirectory = Join-Path $stagingRoot 'tool'
$nugetConfig = Join-Path $stagingRoot 'NuGet.Config'

function Write-Utf8Lf {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content
    )

    $normalized = $Content.Replace("`r`n", "`n").Replace("`r", "`n")
    if (-not $normalized.EndsWith("`n", [System.StringComparison]::Ordinal)) {
        $normalized += "`n"
    }

    [System.IO.File]::WriteAllText($Path, $normalized, [System.Text.UTF8Encoding]::new($false))
}

function Normalize-ZipPlatformMetadata {
    param([Parameter(Mandatory)][string]$Path)

    [byte[]]$bytes = [System.IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 22) {
        throw "The normalized package is too small to contain a ZIP directory: $Path"
    }

    $endOffset = -1
    for ($offset = $bytes.Length - 22; $offset -ge 0; $offset--) {
        if ($bytes[$offset] -eq 0x50 -and
            $bytes[$offset + 1] -eq 0x4B -and
            $bytes[$offset + 2] -eq 0x05 -and
            $bytes[$offset + 3] -eq 0x06) {
            $endOffset = $offset
            break
        }
    }
    if ($endOffset -lt 0) {
        throw "The normalized package has no ZIP end-of-central-directory record: $Path"
    }

    $entryCount = [System.BitConverter]::ToUInt16($bytes, $endOffset + 10)
    $centralOffset = [long][System.BitConverter]::ToUInt32($bytes, $endOffset + 16)
    for ($index = 0; $index -lt $entryCount; $index++) {
        if ($centralOffset -gt $bytes.Length - 46 -or
            $bytes[$centralOffset] -ne 0x50 -or
            $bytes[$centralOffset + 1] -ne 0x4B -or
            $bytes[$centralOffset + 2] -ne 0x01 -or
            $bytes[$centralOffset + 3] -ne 0x02) {
            throw "The normalized package has an invalid ZIP central directory: $Path"
        }

        # Use the MS-DOS creator platform and clear host-specific file attributes.
        $bytes[$centralOffset + 5] = 0
        $bytes[$centralOffset + 38] = 0
        $bytes[$centralOffset + 39] = 0
        $bytes[$centralOffset + 40] = 0
        $bytes[$centralOffset + 41] = 0
        $nameLength = [System.BitConverter]::ToUInt16($bytes, $centralOffset + 28)
        $extraLength = [System.BitConverter]::ToUInt16($bytes, $centralOffset + 30)
        $commentLength = [System.BitConverter]::ToUInt16($bytes, $centralOffset + 32)
        $centralOffset = $centralOffset + 46 + $nameLength + $extraLength + $commentLength
    }

    $metadataPath = $Path + '.metadata-normalized'
    try {
        [System.IO.File]::WriteAllBytes($metadataPath, $bytes)
        Remove-Item -LiteralPath $Path -Force
        Move-Item -LiteralPath $metadataPath -Destination $Path
    }
    finally {
        if (Test-Path -LiteralPath $metadataPath) {
            Remove-Item -LiteralPath $metadataPath -Force
        }
    }
}

function Test-NupkgTextEntry {
    param([Parameter(Mandatory)][string]$EntryName)

    foreach ($suffix in @('.json', '.md', '.nuspec', '.psmdcp', '.rels', '.xml')) {
        if ($EntryName.EndsWith($suffix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Normalize-Nupkg {
    param([Parameter(Mandatory)][string]$Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $temporaryPath = $Path + '.normalized'
    $source = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entryNames = [string[]]@($source.Entries | ForEach-Object { $_.FullName })
        [Array]::Sort($entryNames, [System.StringComparer]::Ordinal)
        $targetStream = [System.IO.File]::Open(
            $temporaryPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None)
        try {
            $target = [System.IO.Compression.ZipArchive]::new(
                $targetStream,
                [System.IO.Compression.ZipArchiveMode]::Create,
                $false)
            try {
                $fixedTimestamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                foreach ($entryName in $entryNames) {
                    $sourceEntry = $source.GetEntry($entryName)
                    $targetEntry = $target.CreateEntry(
                        $entryName,
                        [System.IO.Compression.CompressionLevel]::NoCompression)
                    $targetEntry.LastWriteTime = $fixedTimestamp
                    $targetEntry.ExternalAttributes = 0
                    $sourceStream = $sourceEntry.Open()
                    $targetEntryStream = $targetEntry.Open()
                    try {
                        if (Test-NupkgTextEntry -EntryName $entryName) {
                            $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
                            $reader = [System.IO.StreamReader]::new($sourceStream, $utf8, $true, 4096, $true)
                            try {
                                $text = $reader.ReadToEnd().Replace("`r`n", "`n").Replace("`r", "`n")
                            }
                            finally {
                                $reader.Dispose()
                            }
                            [byte[]]$normalizedBytes = $utf8.GetBytes($text)
                            $targetEntryStream.Write($normalizedBytes, 0, $normalizedBytes.Length)
                        }
                        else {
                            $sourceStream.CopyTo($targetEntryStream)
                        }
                    }
                    finally {
                        $targetEntryStream.Dispose()
                        $sourceStream.Dispose()
                    }
                }
            }
            finally {
                $target.Dispose()
            }
        }
        finally {
            $targetStream.Dispose()
        }
    }
    finally {
        $source.Dispose()
    }

    Remove-Item -LiteralPath $Path -Force
    Move-Item -LiteralPath $temporaryPath -Destination $Path
    Normalize-ZipPlatformMetadata -Path $Path
}

function Read-ZipEntryText {
    param(
        [Parameter(Mandatory)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory)][string]$EntryName
    )

    $entry = $Archive.GetEntry($EntryName)
    if ($null -eq $entry) {
        throw "Required package entry is missing: $EntryName"
    }

    $stream = $entry.Open()
    $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true)
    try {
        return $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Get-LibraryIdentity {
    param([Parameter(Mandatory)][string]$LibraryKey)

    $separator = $LibraryKey.LastIndexOf('/')
    if ($separator -le 0 -or $separator -eq $LibraryKey.Length - 1) {
        throw "Invalid dependency identity in flow.deps.json: $LibraryKey"
    }

    return [pscustomobject]@{
        Name = $LibraryKey.Substring(0, $separator)
        Version = $LibraryKey.Substring($separator + 1)
    }
}

function Get-BomReference {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$RootLibraryKey,
        [Parameter(Mandatory)][string]$LibraryKey,
        [Parameter(Mandatory)][string]$PackageId
    )

    if ($LibraryKey -eq $RootLibraryKey) {
        return "pkg:nuget/$PackageId@$Version"
    }

    return "pkg:nuget/$Name@$Version"
}

try {
    New-Item -ItemType Directory -Path $firstPackDirectory, $secondPackDirectory, $releaseDirectory, $toolDirectory -Force | Out-Null

    $packageId = (& dotnet msbuild $projectPath -nologo -getProperty:PackageId).Trim()
    $packageVersion = (& dotnet msbuild $projectPath -nologo -getProperty:PackageVersion).Trim()
    $targetFramework = (& dotnet msbuild $projectPath -nologo -getProperty:TargetFramework).Trim()
    $toolCommand = (& dotnet msbuild $projectPath -nologo -getProperty:ToolCommandName).Trim()
    if ($LASTEXITCODE -ne 0 -or
        [string]::IsNullOrWhiteSpace($packageId) -or
        [string]::IsNullOrWhiteSpace($packageVersion) -or
        [string]::IsNullOrWhiteSpace($targetFramework) -or
        [string]::IsNullOrWhiteSpace($toolCommand)) {
        throw 'Could not resolve the CLI release identity from MSBuild.'
    }

    $pathMap = $repositoryRoot + '=/_/'
    $commonPackArguments = @(
        'pack',
        $projectPath,
        '--configuration', $Configuration,
        '--no-restore',
        '-p:ContinuousIntegrationBuild=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        "-p:PathMap=$pathMap")

    & dotnet @commonPackArguments '--output' $firstPackDirectory
    if ($LASTEXITCODE -ne 0) {
        throw 'The first deterministic dotnet pack invocation failed.'
    }

    & dotnet @commonPackArguments '--output' $secondPackDirectory
    if ($LASTEXITCODE -ne 0) {
        throw 'The second deterministic dotnet pack invocation failed.'
    }

    $packageFileName = "$packageId.$packageVersion.nupkg"
    $firstPackage = Join-Path $firstPackDirectory $packageFileName
    $secondPackage = Join-Path $secondPackDirectory $packageFileName
    if (-not (Test-Path -LiteralPath $firstPackage -PathType Leaf) -or
        -not (Test-Path -LiteralPath $secondPackage -PathType Leaf)) {
        throw "One of the expected packages was not produced: $packageFileName"
    }

    Normalize-Nupkg -Path $firstPackage
    Normalize-Nupkg -Path $secondPackage
    $firstHash = (Get-FileHash -LiteralPath $firstPackage -Algorithm SHA256).Hash.ToLowerInvariant()
    $secondHash = (Get-FileHash -LiteralPath $secondPackage -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($firstHash -ne $secondHash) {
        throw "Independent normalized package builds are not reproducible: $firstHash != $secondHash"
    }

    $releasePackage = Join-Path $releaseDirectory $packageFileName
    Copy-Item -LiteralPath $firstPackage -Destination $releasePackage

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($releasePackage)
    try {
        $entryNames = [string[]]@($archive.Entries | ForEach-Object { $_.FullName })
        $entrySet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($entryName in $entryNames) {
            if ([string]::IsNullOrWhiteSpace($entryName) -or
                $entryName.StartsWith('/', [System.StringComparison]::Ordinal) -or
                $entryName.Contains('\') -or
                $entryName.Split('/') -contains '..') {
                throw "Unsafe package entry path: $entryName"
            }

            if (-not $entrySet.Add($entryName)) {
                throw "Duplicate package entry path: $entryName"
            }
        }

        [Array]::Sort($entryNames, [System.StringComparer]::Ordinal)
        $packageEntries = @()
        foreach ($entryName in $entryNames) {
            $entry = $archive.GetEntry($entryName)
            $entryStream = $entry.Open()
            $sha256 = [System.Security.Cryptography.SHA256]::Create()
            try {
                $entryHash = ([System.BitConverter]::ToString($sha256.ComputeHash($entryStream))).Replace('-', '').ToLowerInvariant()
            }
            finally {
                $sha256.Dispose()
                $entryStream.Dispose()
            }
            $packageEntries += [ordered]@{
                path = $entryName
                bytes = $entry.Length
                sha256 = $entryHash
            }
        }

        $requiredEntries = @(
            "$packageId.nuspec",
            'README.md',
            "tools/$targetFramework/any/DotnetToolSettings.xml",
            "tools/$targetFramework/any/flow.dll",
            "tools/$targetFramework/any/flow.deps.json",
            "tools/$targetFramework/any/flow.runtimeconfig.json",
            "tools/$targetFramework/any/pt-BR/flow.resources.dll")
        foreach ($requiredEntry in $requiredEntries) {
            if (-not $entrySet.Contains($requiredEntry)) {
                throw "Required package entry is missing: $requiredEntry"
            }
        }

        $nuspecText = Read-ZipEntryText -Archive $archive -EntryName "$packageId.nuspec"
        Add-Type -AssemblyName System.Xml.Linq
        $xmlSettings = [System.Xml.XmlReaderSettings]::new()
        $xmlSettings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
        $xmlSettings.XmlResolver = $null
        $stringReader = [System.IO.StringReader]::new($nuspecText)
        $xmlReader = [System.Xml.XmlReader]::Create($stringReader, $xmlSettings)
        try {
            $nuspec = [System.Xml.Linq.XDocument]::Load($xmlReader)
        }
        finally {
            $xmlReader.Dispose()
            $stringReader.Dispose()
        }

        $metadata = $nuspec.Root.Elements() | Where-Object { $_.Name.LocalName -eq 'metadata' } | Select-Object -First 1
        $nuspecId = ($metadata.Elements() | Where-Object { $_.Name.LocalName -eq 'id' } | Select-Object -First 1).Value
        $nuspecVersion = ($metadata.Elements() | Where-Object { $_.Name.LocalName -eq 'version' } | Select-Object -First 1).Value
        $nuspecPackageType = $metadata.Descendants() | Where-Object {
            $_.Name.LocalName -eq 'packageType' -and $_.Attribute('name').Value -eq 'DotnetTool'
        } | Select-Object -First 1
        if ($nuspecId -ne $packageId -or $nuspecVersion -ne $packageVersion -or $null -eq $nuspecPackageType) {
            throw 'The package nuspec identity or DotnetTool type is invalid.'
        }

        $depsText = Read-ZipEntryText -Archive $archive -EntryName "tools/$targetFramework/any/flow.deps.json"
    }
    finally {
        $archive.Dispose()
    }

    $deps = $depsText | ConvertFrom-Json
    $targetProperty = $deps.targets.PSObject.Properties | Select-Object -First 1
    if ($null -eq $targetProperty) {
        throw 'flow.deps.json has no runtime target.'
    }

    $targetLibraries = $targetProperty.Value.PSObject.Properties
    $libraryProperties = $deps.libraries.PSObject.Properties
    $rootLibrary = $targetLibraries | Where-Object { $_.Name.StartsWith('flow/', [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if ($null -eq $rootLibrary) {
        throw 'flow.deps.json does not identify the CLI root component.'
    }

    $rootLibraryKey = $rootLibrary.Name
    $rootIdentity = Get-LibraryIdentity -LibraryKey $rootLibraryKey
    if ($rootIdentity.Version -ne $packageVersion) {
        throw 'The package version and flow.deps.json root version do not match.'
    }

    $libraryKeys = [string[]]@($libraryProperties | ForEach-Object { $_.Name })
    [Array]::Sort($libraryKeys, [System.StringComparer]::Ordinal)
    $bomReferences = @{}
    foreach ($libraryKey in $libraryKeys) {
        $identity = Get-LibraryIdentity -LibraryKey $libraryKey
        $bomReferences[$libraryKey] = Get-BomReference `
            -Name $identity.Name `
            -Version $identity.Version `
            -RootLibraryKey $rootLibraryKey `
            -LibraryKey $libraryKey `
            -PackageId $packageId
    }

    $components = @()
    foreach ($libraryKey in $libraryKeys) {
        if ($libraryKey -eq $rootLibraryKey) {
            continue
        }

        $identity = Get-LibraryIdentity -LibraryKey $libraryKey
        $library = $deps.libraries.PSObject.Properties[$libraryKey].Value
        $component = [ordered]@{
            type = 'library'
            'bom-ref' = $bomReferences[$libraryKey]
            name = $identity.Name
            version = $identity.Version
            purl = $bomReferences[$libraryKey]
        }
        if ($library.type -eq 'project') {
            $component.licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT' } })
        }
        $component.properties = @([ordered]@{ name = 'flow:dependencyType'; value = [string]$library.type })
        $components += $component
    }

    $dependencyEntries = @()
    foreach ($libraryKey in $libraryKeys) {
        $targetLibraryProperty = $targetLibraries | Where-Object { $_.Name -eq $libraryKey } | Select-Object -First 1
        if ($null -eq $targetLibraryProperty) {
            throw "SBOM dependency is absent from the runtime target: $libraryKey"
        }

        $dependencyReferences = @()
        $declaredDependencies = $targetLibraryProperty.Value.dependencies
        if ($null -ne $declaredDependencies) {
            $dependencyNames = [string[]]@($declaredDependencies.PSObject.Properties | ForEach-Object { $_.Name })
            [Array]::Sort($dependencyNames, [System.StringComparer]::Ordinal)
            foreach ($dependencyName in $dependencyNames) {
                $dependencyVersion = [string]$declaredDependencies.$dependencyName
                $dependencyKey = "$dependencyName/$dependencyVersion"
                if (-not $bomReferences.ContainsKey($dependencyKey)) {
                    throw "SBOM dependency target is missing: $dependencyKey"
                }
                $dependencyReferences += $bomReferences[$dependencyKey]
            }
        }

        $dependencyEntries += [ordered]@{
            ref = $bomReferences[$libraryKey]
            dependsOn = @($dependencyReferences)
        }
    }

    $packageHash = (Get-FileHash -LiteralPath $releasePackage -Algorithm SHA256).Hash.ToLowerInvariant()
    $rootBomReference = $bomReferences[$rootLibraryKey]
    $sbom = [ordered]@{
        bomFormat = 'CycloneDX'
        specVersion = '1.5'
        version = 1
        metadata = [ordered]@{
            component = [ordered]@{
                type = 'application'
                'bom-ref' = $rootBomReference
                group = 'FlowEngineNet'
                name = $packageId
                version = $packageVersion
                hashes = @([ordered]@{ alg = 'SHA-256'; content = $packageHash })
                licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT' } })
                purl = $rootBomReference
                properties = @(
                    [ordered]@{ name = 'flow:targetFramework'; value = $targetFramework },
                    [ordered]@{ name = 'flow:toolCommand'; value = $toolCommand },
                    [ordered]@{ name = 'flow:deployment'; value = 'framework-dependent' })
            }
        }
        components = @($components)
        dependencies = @($dependencyEntries)
    }

    $sbomFileName = "$packageId.$packageVersion.cdx.json"
    $sbomPath = Join-Path $releaseDirectory $sbomFileName
    Write-Utf8Lf -Path $sbomPath -Content ($sbom | ConvertTo-Json -Depth 20 -Compress)
    $sbomHash = (Get-FileHash -LiteralPath $sbomPath -Algorithm SHA256).Hash.ToLowerInvariant()

    $parsedSbom = Get-Content -LiteralPath $sbomPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($parsedSbom.bomFormat -ne 'CycloneDX' -or
        $parsedSbom.specVersion -ne '1.5' -or
        $parsedSbom.metadata.component.version -ne $packageVersion -or
        $parsedSbom.components.Count -ne ($libraryKeys.Count - 1)) {
        throw 'The generated CycloneDX SBOM failed structural validation.'
    }

    $knownBomReferences = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    [void]$knownBomReferences.Add($rootBomReference)
    foreach ($component in $parsedSbom.components) {
        if (-not $knownBomReferences.Add([string]$component.'bom-ref')) {
            throw "Duplicate SBOM component reference: $($component.'bom-ref')"
        }
    }
    foreach ($dependency in $parsedSbom.dependencies) {
        if (-not $knownBomReferences.Contains([string]$dependency.ref)) {
            throw "Unknown SBOM dependency source: $($dependency.ref)"
        }
        foreach ($dependencyReference in $dependency.dependsOn) {
            if (-not $knownBomReferences.Contains([string]$dependencyReference)) {
                throw "Unknown SBOM dependency target: $dependencyReference"
            }
        }
    }

    $escapedReleaseDirectory = [System.Security.SecurityElement]::Escape($releaseDirectory)
    $nugetXml = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="flow-local-release" value="$escapedReleaseDirectory" />
  </packageSources>
</configuration>
"@
    Write-Utf8Lf -Path $nugetConfig -Content $nugetXml

    $installed = $false
    try {
        & dotnet tool install $packageId --tool-path $toolDirectory --version $packageVersion --configfile $nugetConfig
        if ($LASTEXITCODE -ne 0) {
            throw 'The release package could not be installed in the isolated tool directory.'
        }
        $installed = $true

        $flowExecutable = if ($runningOnWindows) {
            Join-Path $toolDirectory 'flow.exe'
        }
        else {
            Join-Path $toolDirectory 'flow'
        }
        if (-not (Test-Path -LiteralPath $flowExecutable -PathType Leaf)) {
            throw "The installed release launcher was not found: $flowExecutable"
        }

        $help = & $flowExecutable help 2>&1
        if ($LASTEXITCODE -ne 0 -or
            (($help -join "`n").IndexOf("Flow Engine .NET $packageVersion", [System.StringComparison]::Ordinal) -lt 0)) {
            throw 'The installed release launcher did not return the expected help and version.'
        }
    }
    finally {
        if ($installed) {
            & dotnet tool uninstall $packageId --tool-path $toolDirectory | Out-Host
            if ($LASTEXITCODE -ne 0) {
                throw 'The isolated release tool uninstall failed.'
            }
        }
    }

    $manifest = [ordered]@{
        format = 'flow-cli-release-manifest-0.1'
        packageId = $packageId
        packageVersion = $packageVersion
        configuration = $Configuration
        targetFramework = $targetFramework
        toolCommand = $toolCommand
        packageEntries = @($packageEntries)
        files = @(
            [ordered]@{
                path = $packageFileName
                mediaType = 'application/zip'
                bytes = (Get-Item -LiteralPath $releasePackage).Length
                sha256 = $packageHash
            },
            [ordered]@{
                path = $sbomFileName
                mediaType = 'application/vnd.cyclonedx+json'
                bytes = (Get-Item -LiteralPath $sbomPath).Length
                sha256 = $sbomHash
            })
        reproducibility = [ordered]@{
            independentPackCount = 2
            normalizedPackageBytesMatch = $true
            sha256 = $packageHash
        }
        validation = [ordered]@{
            packageStructure = 'passed'
            dependencyGraph = 'passed'
            sbomStructure = 'passed'
            isolatedToolInstall = 'passed'
            installedHelp = 'passed'
        }
    }

    $manifestFileName = 'release-manifest.json'
    $manifestPath = Join-Path $releaseDirectory $manifestFileName
    Write-Utf8Lf -Path $manifestPath -Content ($manifest | ConvertTo-Json -Depth 12 -Compress)

    $checksumEntries = @(
        [pscustomobject]@{ Name = $packageFileName; Path = $releasePackage },
        [pscustomobject]@{ Name = $sbomFileName; Path = $sbomPath },
        [pscustomobject]@{ Name = $manifestFileName; Path = $manifestPath })
    $checksumLines = @()
    foreach ($checksumEntry in $checksumEntries | Sort-Object Name) {
        $hash = (Get-FileHash -LiteralPath $checksumEntry.Path -Algorithm SHA256).Hash.ToLowerInvariant()
        $checksumLines += "$hash  $($checksumEntry.Name)"
    }

    $checksumsPath = Join-Path $releaseDirectory 'SHA256SUMS'
    Write-Utf8Lf -Path $checksumsPath -Content ($checksumLines -join "`n")
    foreach ($line in Get-Content -LiteralPath $checksumsPath -Encoding UTF8) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        if ($line -notmatch '^([0-9a-f]{64})  ([^/\\]+)$') {
            throw "Invalid SHA256SUMS line: $line"
        }
        $expectedHash = $Matches[1]
        $checkedFile = Join-Path $releaseDirectory $Matches[2]
        $actualHash = (Get-FileHash -LiteralPath $checkedFile -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "Checksum validation failed for $($Matches[2])."
        }
    }

    if ($replaceExistingOutput) {
        $backupDirectory = Join-Path $stagingRoot 'previous-release'
        Move-Item -LiteralPath $outputRoot -Destination $backupDirectory
        try {
            Move-Item -LiteralPath $releaseDirectory -Destination $outputRoot
        }
        catch {
            if (-not (Test-Path -LiteralPath $outputRoot) -and (Test-Path -LiteralPath $backupDirectory)) {
                Move-Item -LiteralPath $backupDirectory -Destination $outputRoot
            }
            throw
        }
    }
    else {
        Move-Item -LiteralPath $releaseDirectory -Destination $outputRoot
    }
    Write-Host "Flow CLI release artifacts passed reproducibility and validation checks: $outputRoot"
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        $stagingItem = Get-Item -LiteralPath $stagingRoot -Force
        if (($stagingItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) {
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
    }
}
