[CmdletBinding()]
param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDirectory,

    [string]$PortableArtifactsDirectory,

    [switch]$Force
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') {
    throw 'The combined win-x64 payload must be built on Windows.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$applicationProject = Join-Path $repositoryRoot 'src/Flow.Windows/Flow.Windows.csproj'
$cliProject = Join-Path $repositoryRoot 'src/Flow.Cli/Flow.Cli.csproj'
$portableBuilder = Join-Path $PSScriptRoot 'build-windows-portable.ps1'
$licensePath = Join-Path $repositoryRoot 'LICENSE'
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $allowedArtifactsRoot 'windows-combined-payload'
}

$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not $outputRoot.StartsWith(
        $allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The combined payload output directory must be a child of '$allowedArtifactsRoot'."
}
if (Test-Path -LiteralPath $outputRoot) {
    $existingOutput = Get-Item -LiteralPath $outputRoot -Force
    if (($existingOutput.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "The combined payload output directory cannot be a reparse point: $outputRoot"
    }
    if (-not $Force) {
        throw "The combined payload output directory already exists. Pass -Force to replace it: $outputRoot"
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

    $value = (& dotnet msbuild $cliProject -nologo "-getProperty:$Name").Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($value)) {
        throw "Could not resolve MSBuild property '$Name'."
    }
    return $value
}

function Get-Sha256Lower {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Publish-WindowsApplication {
    param(
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$RuntimeIdentifier,
        [Parameter(Mandatory)][string]$Revision,
        [switch]$NoRestore
    )

    $arguments = @(
        'publish', $applicationProject,
        '--configuration', $Configuration,
        '--runtime', $RuntimeIdentifier,
        '--self-contained', 'true',
        '--output', $Destination,
        '-p:Platform=x64',
        '-p:PublishSingleFile=false',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-p:GenerateDocumentationFile=false',
        '-p:ContinuousIntegrationBuild=true',
        "-p:PathMap=$repositoryRoot=/_/",
        "-p:SourceRevisionId=$Revision"
    )
    if ($NoRestore) {
        $arguments += '--no-restore'
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet publish failed while building the Windows application payload.'
    }

    $entryPoint = Join-Path $Destination 'Flow.Windows.exe'
    if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
        throw "The Windows application entry point was not produced: $entryPoint"
    }
}

function Remove-UnusedApplicationLocales {
    param([Parameter(Mandatory)][string]$Directory)

    foreach ($candidate in @(Get-ChildItem -LiteralPath $Directory -Directory)) {
        if ($candidate.Name -in @('en-us', 'pt-BR')) {
            continue
        }

        $files = @(Get-ChildItem -LiteralPath $candidate.FullName -File -Recurse)
        if ($files.Count -gt 0 -and @($files | Where-Object {
                    $_.Name -notlike '*.mui' -and $_.Name -notlike '*.resources.dll'
                }).Count -eq 0) {
            Remove-Item -LiteralPath $candidate.FullName -Recurse -Force
        }
    }
}

function Compare-DirectoryContent {
    param(
        [Parameter(Mandatory)][string]$First,
        [Parameter(Mandatory)][string]$Second
    )

    $firstFiles = @(Get-ChildItem -LiteralPath $First -File -Recurse |
        ForEach-Object { [System.IO.Path]::GetRelativePath($First, $_.FullName).Replace('\', '/') } |
        Sort-Object)
    $secondFiles = @(Get-ChildItem -LiteralPath $Second -File -Recurse |
        ForEach-Object { [System.IO.Path]::GetRelativePath($Second, $_.FullName).Replace('\', '/') } |
        Sort-Object)
    if ([string]::Join("`n", $firstFiles) -cne [string]::Join("`n", $secondFiles)) {
        throw 'The two Windows application publishes contain different file sets.'
    }

    foreach ($relativePath in $firstFiles) {
        $firstHash = Get-Sha256Lower -Path (Join-Path $First $relativePath)
        $secondHash = Get-Sha256Lower -Path (Join-Path $Second $relativePath)
        if ($firstHash -ne $secondHash) {
            throw "The Windows application publish is not reproducible: $relativePath"
        }
    }
}

function Get-PayloadRole {
    param([Parameter(Mandatory)][string]$RelativePath)

    if ($RelativePath -eq 'app/Flow.Windows.exe') { return 'application-entry-point' }
    if ($RelativePath -eq 'cli/flow.exe') { return 'cli-entry-point' }
    if ($RelativePath -eq 'LICENSE.txt') { return 'license' }
    if ($RelativePath -eq 'VERSION.json') { return 'version' }
    if ($RelativePath.StartsWith('app/Assets/', [System.StringComparison]::Ordinal)) { return 'application-asset' }
    if ($RelativePath.StartsWith('app/en-us/', [System.StringComparison]::OrdinalIgnoreCase) -or
        $RelativePath.StartsWith('app/pt-BR/', [System.StringComparison]::OrdinalIgnoreCase)) { return 'application-locale' }
    if ($RelativePath.StartsWith('app/', [System.StringComparison]::Ordinal)) { return 'application-runtime' }
    return 'payload'
}

New-Item -ItemType Directory -Path $allowedArtifactsRoot -Force | Out-Null
$stagingRoot = Join-Path $allowedArtifactsRoot ('.flow-windows-combined-' + [Guid]::NewGuid().ToString('N'))
$resultDirectory = Join-Path $stagingRoot 'result'
$payloadDirectory = Join-Path $resultDirectory 'payload'
$applicationA = Join-Path $stagingRoot 'application-a'
$applicationB = Join-Path $stagingRoot 'application-b'
$portableArtifacts = Join-Path $stagingRoot 'portable'
$portableExtract = Join-Path $stagingRoot 'portable-extract'

try {
    New-Item -ItemType Directory -Path $resultDirectory, $payloadDirectory, $applicationA, $applicationB -Force | Out-Null

    $version = Get-MsBuildProperty -Name 'FlowPublicVersion'
    $runtimeIdentifier = Get-MsBuildProperty -Name 'FlowWindowsRuntimeIdentifier'
    $architecture = Get-MsBuildProperty -Name 'FlowWindowsArchitecture'
    $format = Get-MsBuildProperty -Name 'FlowWindowsCombinedPayloadFormat'
    $applicationEntryPoint = (Get-MsBuildProperty -Name 'FlowWindowsApplicationEntryPoint').Replace('\', '/')
    $cliEntryPoint = (Get-MsBuildProperty -Name 'FlowWindowsCliEntryPoint').Replace('\', '/')
    $revision = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-f]{40}$') {
        throw 'Could not resolve the source Git revision.'
    }
    $sourceStatus = @(& git -C $repositoryRoot status --porcelain=v1 --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect the source tree state.'
    }
    $sourceTreeDirty = $sourceStatus.Count -ne 0

    if ([string]::IsNullOrWhiteSpace($PortableArtifactsDirectory)) {
        & $portableBuilder -Configuration $Configuration -OutputDirectory $portableArtifacts
        if ($LASTEXITCODE -ne 0) {
            throw 'The existing Windows portable builder failed while producing the CLI payload.'
        }
    }
    else {
        $suppliedPortable = [System.IO.Path]::GetFullPath($PortableArtifactsDirectory)
        if (-not $suppliedPortable.StartsWith(
                $allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar,
                [System.StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $suppliedPortable -PathType Container)) {
            throw "The supplied portable artifacts must be an existing directory below '$allowedArtifactsRoot'."
        }
        $portableItem = Get-Item -LiteralPath $suppliedPortable -Force
        if (($portableItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'The supplied portable artifacts directory cannot be a reparse point.'
        }
        $portableArtifacts = $suppliedPortable
    }
    $portableZip = @(Get-ChildItem -LiteralPath $portableArtifacts -Filter '*.zip' -File)
    if ($portableZip.Count -ne 1) {
        throw 'The portable builder must produce exactly one CLI ZIP.'
    }
    [System.IO.Compression.ZipFile]::ExtractToDirectory($portableZip[0].FullName, $portableExtract)
    $portableVersion = Get-Content -LiteralPath (Join-Path $portableExtract 'VERSION.json') -Raw | ConvertFrom-Json
    if ($portableVersion.version -ne $version -or
        $portableVersion.sourceRevision -ne $revision -or
        $portableVersion.runtimeIdentifier -ne $runtimeIdentifier) {
        throw 'The CLI portable payload does not identify the same version, revision and RID as the combined payload.'
    }

    Publish-WindowsApplication -Destination $applicationA -RuntimeIdentifier $runtimeIdentifier -Revision $revision
    Publish-WindowsApplication -Destination $applicationB -RuntimeIdentifier $runtimeIdentifier -Revision $revision -NoRestore
    Remove-UnusedApplicationLocales -Directory $applicationA
    Remove-UnusedApplicationLocales -Directory $applicationB
    Compare-DirectoryContent -First $applicationA -Second $applicationB

    $applicationPayload = Join-Path $payloadDirectory 'app'
    $cliPayload = Join-Path $payloadDirectory 'cli'
    New-Item -ItemType Directory -Path $applicationPayload, $cliPayload -Force | Out-Null
    Copy-Item -Path (Join-Path $applicationA '*') -Destination $applicationPayload -Recurse
    Copy-Item -LiteralPath (Join-Path $portableExtract 'flow.exe') -Destination (Join-Path $cliPayload 'flow.exe')
    Copy-Item -LiteralPath $licensePath -Destination (Join-Path $payloadDirectory 'LICENSE.txt')

    $versionDocument = [ordered]@{
        format = $format
        product = 'Flow Engine .NET'
        version = $version
        sourceRevision = $revision
        sourceTreeDirty = $sourceTreeDirty
        runtimeIdentifier = $runtimeIdentifier
        architecture = $architecture
        selfContained = $true
        locales = @('en-US', 'pt-BR')
        entryPoints = [ordered]@{
            application = $applicationEntryPoint
            cli = $cliEntryPoint
        }
    }
    Write-Utf8Lf -Path (Join-Path $payloadDirectory 'VERSION.json') -Content ($versionDocument | ConvertTo-Json -Depth 6)

    foreach ($forbiddenExtension in @('*.pdb', '*.cache', '*.user', '*.suo')) {
        if (@(Get-ChildItem -LiteralPath $payloadDirectory -File -Recurse -Filter $forbiddenExtension).Count -ne 0) {
            throw "The combined payload contains forbidden files: $forbiddenExtension"
        }
    }

    $payloadFiles = @(Get-ChildItem -LiteralPath $payloadDirectory -File -Recurse |
        Sort-Object { [System.IO.Path]::GetRelativePath($payloadDirectory, $_.FullName) })
    $manifestFiles = foreach ($file in $payloadFiles) {
        $relativePath = [System.IO.Path]::GetRelativePath($payloadDirectory, $file.FullName).Replace('\', '/')
        [ordered]@{
            path = $relativePath
            role = Get-PayloadRole -RelativePath $relativePath
            sha256 = Get-Sha256Lower -Path $file.FullName
            bytes = $file.Length
        }
    }

    $applicationVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
        (Join-Path $payloadDirectory $applicationEntryPoint)).ProductVersion
    $cliVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
        (Join-Path $payloadDirectory $cliEntryPoint)).ProductVersion
    if (-not $applicationVersion.StartsWith($version, [System.StringComparison]::Ordinal) -or
        -not $cliVersion.StartsWith($version, [System.StringComparison]::Ordinal)) {
        throw "Application and CLI binaries must carry the public version '$version'."
    }

    $manifest = [ordered]@{
        format = $format
        product = 'Flow Engine .NET'
        version = $version
        sourceRevision = $revision
        sourceTreeDirty = $sourceTreeDirty
        runtimeIdentifier = $runtimeIdentifier
        architecture = $architecture
        locales = @('en-US', 'pt-BR')
        entryPoints = [ordered]@{
            application = [ordered]@{ path = $applicationEntryPoint; kind = 'winui3'; subsystem = 'windows' }
            cli = [ordered]@{ path = $cliEntryPoint; kind = 'command-line'; subsystem = 'console' }
        }
        packaging = [ordered]@{
            application = [ordered]@{ method = 'unpackaged-multi-file'; selfContained = $true; singleFile = $false }
            cli = [ordered]@{ method = 'portable-single-file'; selfContained = $true; singleFile = $true }
        }
        files = @($manifestFiles)
    }
    $manifestPath = Join-Path $resultDirectory 'combined-payload-manifest.json'
    Write-Utf8Lf -Path $manifestPath -Content ($manifest | ConvertTo-Json -Depth 8)

    $checksumLines = @(
        "$(Get-Sha256Lower -Path $manifestPath)  combined-payload-manifest.json"
        foreach ($file in $payloadFiles) {
            $relativePath = [System.IO.Path]::GetRelativePath($resultDirectory, $file.FullName).Replace('\', '/')
            "$(Get-Sha256Lower -Path $file.FullName)  $relativePath"
        }
    )
    Write-Utf8Lf -Path (Join-Path $resultDirectory 'SHA256SUMS') -Content ($checksumLines -join "`n")

    if (Test-Path -LiteralPath $outputRoot) {
        Remove-Item -LiteralPath $outputRoot -Recurse -Force
    }
    Move-Item -LiteralPath $resultDirectory -Destination $outputRoot
    Write-Output "Combined Windows application and CLI payload created: $outputRoot"
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
