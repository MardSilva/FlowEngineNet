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
$licensePath = Join-Path $repositoryRoot 'LICENSE'
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $allowedArtifactsRoot 'windows-portable'
}

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$pathComparison = if ($env:OS -eq 'Windows_NT') {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
if (-not $outputRoot.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, $pathComparison)) {
    throw "The portable output directory must be a child of '$allowedArtifactsRoot'."
}
if (Test-Path -LiteralPath $outputRoot) {
    $existingOutput = Get-Item -LiteralPath $outputRoot -Force
    if (($existingOutput.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The portable output directory cannot be a reparse point: $outputRoot"
    }
    if (-not $Force) {
        throw "The portable output directory already exists. Pass -Force to replace it: $outputRoot"
    }
}

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

function Get-MsBuildProperty {
    param([Parameter(Mandatory)][string]$Name)

    $value = (& dotnet msbuild $projectPath -nologo "-getProperty:$Name").Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value)) {
        throw "Could not resolve MSBuild property '$Name'."
    }
    return $value
}

function Get-Sha256Lower {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [System.IO.File]::Open(
        $DestinationPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $stream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            $fixedTimestamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $files = [string[]]@(Get-ChildItem -LiteralPath $SourceDirectory -File -Recurse |
                ForEach-Object { $_.FullName })
            [Array]::Sort($files, [System.StringComparer]::Ordinal)
            foreach ($file in $files) {
                $relativePath = [System.IO.Path]::GetRelativePath($SourceDirectory, $file).Replace('\', '/')
                $entry = $archive.CreateEntry($relativePath, [System.IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTimestamp
                $entry.ExternalAttributes = 0
                $source = [System.IO.File]::OpenRead($file)
                $target = $entry.Open()
                try {
                    $source.CopyTo($target)
                }
                finally {
                    $target.Dispose()
                    $source.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Publish-PortablePayload {
    param(
        [Parameter(Mandatory)][string]$PublishDirectory,
        [Parameter(Mandatory)][string]$PayloadDirectory,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$RuntimeIdentifier,
        [Parameter(Mandatory)][string]$Architecture,
        [Parameter(Mandatory)][string]$Format,
        [switch]$NoRestore
    )

    $arguments = @(
        'publish', $projectPath,
        '--configuration', $Configuration,
        '--runtime', $RuntimeIdentifier,
        '--self-contained', 'true',
        '--output', $PublishDirectory,
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=false',
        '-p:IncludeAllContentForSelfExtract=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-p:GenerateDocumentationFile=false',
        '-p:ContinuousIntegrationBuild=true',
        "-p:SourceRevisionId=$Revision"
    )
    if ($NoRestore) {
        $arguments += '--no-restore'
    }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet publish failed while building the Windows portable payload.'
    }

    New-Item -ItemType Directory -Path $PayloadDirectory -Force | Out-Null
    $executable = Join-Path $PublishDirectory 'flow.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "The self-contained executable was not produced: $executable"
    }
    $unexpected = @(Get-ChildItem -LiteralPath $PublishDirectory -File |
        Where-Object { $_.Name -ne 'flow.exe' })
    if ($unexpected.Count -ne 0) {
        throw "Single-file publish produced unexpected sidecar files: $($unexpected.Name -join ', ')"
    }

    Copy-Item -LiteralPath $executable -Destination (Join-Path $PayloadDirectory 'flow.exe')
    Copy-Item -LiteralPath $licensePath -Destination (Join-Path $PayloadDirectory 'LICENSE.txt')
    $versionDocument = [ordered]@{
        format = $Format
        product = 'Flow Engine .NET'
        version = $Version
        sourceRevision = $Revision
        runtimeIdentifier = $RuntimeIdentifier
        architecture = $Architecture
        selfContained = $true
        singleFile = $true
        locales = @('en-US', 'pt-BR')
        entryPoint = 'flow.exe'
    }
    Write-Utf8Lf -Path (Join-Path $PayloadDirectory 'VERSION.json') -Content ($versionDocument | ConvertTo-Json -Depth 4)
}

function New-PortableSbom {
    param(
        [Parameter(Mandatory)][string]$DependenciesPath,
        [Parameter(Mandatory)][string]$DestinationPath,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$Revision,
        [Parameter(Mandatory)][string]$ExecutablePath
    )

    $dependencies = Get-Content -LiteralPath $DependenciesPath -Raw | ConvertFrom-Json -AsHashtable
    $targetName = [string]@($dependencies.targets.Keys |
        Where-Object { $dependencies.targets[$_].Count -gt 0 } |
        Select-Object -First 1)[0]
    if ([string]::IsNullOrWhiteSpace($targetName)) {
        throw 'The published dependency graph has no populated runtime target.'
    }
    $target = $dependencies.targets[$targetName]
    $rootKey = "flow/$Version"
    $references = @{}
    foreach ($libraryKey in $target.Keys) {
        $separator = $libraryKey.LastIndexOf('/')
        $name = $libraryKey.Substring(0, $separator)
        $libraryVersion = $libraryKey.Substring($separator + 1)
        $references[$libraryKey] = if ($libraryKey -eq $rootKey) {
            "pkg:generic/FlowEngineNet@$Version"
        }
        elseif ($name.StartsWith('Flow.', [System.StringComparison]::Ordinal)) {
            "pkg:generic/$name@$libraryVersion"
        }
        else {
            "pkg:nuget/$name@$libraryVersion"
        }
    }

    $components = @()
    foreach ($libraryKey in @($target.Keys | Sort-Object)) {
        if ($libraryKey -eq $rootKey) {
            continue
        }
        $separator = $libraryKey.LastIndexOf('/')
        $name = $libraryKey.Substring(0, $separator)
        $libraryVersion = $libraryKey.Substring($separator + 1)
        $componentType = if ($name.StartsWith('Flow.', [System.StringComparison]::Ordinal)) { 'library' } else { 'framework' }
        if ($name.StartsWith('Spectre.Console', [System.StringComparison]::Ordinal)) {
            $componentType = 'library'
        }
        $components += [ordered]@{
            type = $componentType
            'bom-ref' = $references[$libraryKey]
            name = $name
            version = $libraryVersion
            licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT' } })
            purl = $references[$libraryKey]
        }
    }

    $dependencyGraph = @()
    foreach ($libraryKey in @($target.Keys | Sort-Object)) {
        $targets = @()
        if ($target[$libraryKey].ContainsKey('dependencies')) {
            foreach ($dependencyName in @($target[$libraryKey].dependencies.Keys | Sort-Object)) {
                $dependencyVersion = [string]$target[$libraryKey].dependencies[$dependencyName]
                $candidate = "$dependencyName/$dependencyVersion"
                if ($references.ContainsKey($candidate)) {
                    $targets += $references[$candidate]
                }
            }
        }
        $dependencyGraph += [ordered]@{ ref = $references[$libraryKey]; dependsOn = $targets }
    }

    $sbom = [ordered]@{
        bomFormat = 'CycloneDX'
        specVersion = '1.5'
        version = 1
        metadata = [ordered]@{
            tools = [ordered]@{ components = @([ordered]@{ type = 'application'; name = 'Flow Windows portable builder'; version = '0.1' }) }
            component = [ordered]@{
                type = 'application'
                'bom-ref' = $references[$rootKey]
                name = 'FlowEngineNet'
                version = $Version
                hashes = @([ordered]@{ alg = 'SHA-256'; content = (Get-Sha256Lower -Path $ExecutablePath) })
                licenses = @([ordered]@{ license = [ordered]@{ id = 'MIT' } })
                properties = @([ordered]@{ name = 'flow:sourceRevision'; value = $Revision })
            }
        }
        components = $components
        dependencies = $dependencyGraph
    }
    Write-Utf8Lf -Path $DestinationPath -Content ($sbom | ConvertTo-Json -Depth 12)
}

New-Item -ItemType Directory -Path $allowedArtifactsRoot -Force | Out-Null
$stagingRoot = Join-Path $allowedArtifactsRoot ('.flow-windows-portable-' + [Guid]::NewGuid().ToString('N'))
$resultDirectory = Join-Path $stagingRoot 'result'
$publishA = Join-Path $stagingRoot 'publish-a'
$publishB = Join-Path $stagingRoot 'publish-b'
$payloadA = Join-Path $stagingRoot 'payload-a'
$payloadB = Join-Path $stagingRoot 'payload-b'

try {
    New-Item -ItemType Directory -Path $resultDirectory, $publishA, $publishB -Force | Out-Null
    $version = Get-MsBuildProperty -Name 'FlowPublicVersion'
    $runtimeIdentifier = Get-MsBuildProperty -Name 'FlowWindowsRuntimeIdentifier'
    $architecture = Get-MsBuildProperty -Name 'FlowWindowsArchitecture'
    $format = Get-MsBuildProperty -Name 'FlowWindowsPortableFormat'
    $revision = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-f]{40}$') {
        throw 'Could not resolve the source Git revision.'
    }

    Publish-PortablePayload -PublishDirectory $publishA -PayloadDirectory $payloadA -Version $version -Revision $revision -RuntimeIdentifier $runtimeIdentifier -Architecture $architecture -Format $format
    Publish-PortablePayload -PublishDirectory $publishB -PayloadDirectory $payloadB -Version $version -Revision $revision -RuntimeIdentifier $runtimeIdentifier -Architecture $architecture -Format $format -NoRestore

    foreach ($name in @('flow.exe', 'LICENSE.txt', 'VERSION.json')) {
        $firstHash = Get-Sha256Lower -Path (Join-Path $payloadA $name)
        $secondHash = Get-Sha256Lower -Path (Join-Path $payloadB $name)
        if ($firstHash -ne $secondHash) {
            throw "Portable payload is not reproducible: $name ($firstHash != $secondHash)"
        }
    }

    $artifactBaseName = "FlowEngineNet.Portable.$version.$runtimeIdentifier"
    $zipA = Join-Path $stagingRoot 'portable-a.zip'
    $zipB = Join-Path $stagingRoot 'portable-b.zip'
    New-DeterministicZip -SourceDirectory $payloadA -DestinationPath $zipA
    New-DeterministicZip -SourceDirectory $payloadB -DestinationPath $zipB
    if ((Get-Sha256Lower -Path $zipA) -ne (Get-Sha256Lower -Path $zipB)) {
        throw 'The two independently generated portable ZIP archives differ.'
    }

    $zipPath = Join-Path $resultDirectory "$artifactBaseName.zip"
    Move-Item -LiteralPath $zipA -Destination $zipPath
    $dependenciesPath = Join-Path $repositoryRoot 'src/Flow.Cli/obj/Release/net10.0/win-x64/flow.deps.json'
    if (-not (Test-Path -LiteralPath $dependenciesPath -PathType Leaf)) {
        throw "The publish dependency graph is missing: $dependenciesPath"
    }
    $sbomPath = Join-Path $resultDirectory "$artifactBaseName.cdx.json"
    New-PortableSbom -DependenciesPath $dependenciesPath -DestinationPath $sbomPath -Version $version -Revision $revision -ExecutablePath (Join-Path $payloadA 'flow.exe')

    $manifestPath = Join-Path $resultDirectory 'portable-manifest.json'
    $manifest = [ordered]@{
        format = $format
        product = 'Flow Engine .NET'
        version = $version
        sourceRevision = $revision
        runtimeIdentifier = $runtimeIdentifier
        architecture = $architecture
        selfContained = $true
        singleFile = $true
        extractionPolicy = 'no-forced-self-extraction'
        locales = @('en-US', 'pt-BR')
        files = @(
            [ordered]@{ name = [System.IO.Path]::GetFileName($zipPath); sha256 = (Get-Sha256Lower -Path $zipPath); bytes = (Get-Item -LiteralPath $zipPath).Length }
            [ordered]@{ name = [System.IO.Path]::GetFileName($sbomPath); sha256 = (Get-Sha256Lower -Path $sbomPath); bytes = (Get-Item -LiteralPath $sbomPath).Length }
        )
    }
    Write-Utf8Lf -Path $manifestPath -Content ($manifest | ConvertTo-Json -Depth 8)

    $checksumLines = foreach ($path in @($zipPath, $sbomPath, $manifestPath)) {
        "$(Get-Sha256Lower -Path $path)  $([System.IO.Path]::GetFileName($path))"
    }
    Write-Utf8Lf -Path (Join-Path $resultDirectory 'SHA256SUMS') -Content ($checksumLines -join "`n")

    if (Test-Path -LiteralPath $outputRoot) {
        Remove-Item -LiteralPath $outputRoot -Recurse -Force
    }
    Move-Item -LiteralPath $resultDirectory -Destination $outputRoot
    Write-Output "Windows portable artifact created: $outputRoot"
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
