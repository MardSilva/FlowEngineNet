[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CombinedPayloadDirectory,
    [string]$OutputDirectory,
    [string]$MakeAppxPath,
    [switch]$AllowDevelopmentPayload
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:OS -ne 'Windows_NT') { throw 'MSIX packaging requires Windows and PowerShell 7.' }
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $repository 'artifacts'
$inputRoot = [IO.Path]::GetFullPath($CombinedPayloadDirectory)
$payloadRoot = Join-Path $inputRoot 'payload'
$manifestPath = Join-Path $inputRoot 'combined-payload-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$head = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve source revision.' }
$dirty = @(& git -C $repository status --porcelain=v1 --untracked-files=normal)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect source tree.' }
if ($manifest.format -ne 'flow-windows-combined-payload-0.1' -or
    $manifest.runtimeIdentifier -ne 'win-x64' -or $manifest.architecture -ne 'x64' -or
    $manifest.sourceRevision -ne $head -or
    $manifest.entryPoints.application.path -ne 'app/Flow.Windows.exe' -or
    $manifest.entryPoints.cli.path -ne 'cli/flow.exe') {
    throw 'Expected a combined win-x64 payload from the current source revision.'
}
if (-not $AllowDevelopmentPayload -and ($manifest.sourceTreeDirty -or $dirty.Count -ne 0)) {
    throw 'Store candidates require a clean source tree and clean payload. Use -AllowDevelopmentPayload only for local rehearsal.'
}

# Reject links and validate the complete file set before copying any payload.
$items = @(Get-ChildItem -LiteralPath $payloadRoot -Recurse -Force)
if ((Get-Item -LiteralPath $payloadRoot).Attributes -band [IO.FileAttributes]::ReparsePoint -or
    @($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count) {
    throw 'Payload links are not allowed.'
}
$actual = @($items | Where-Object { -not $_.PSIsContainer } |
    ForEach-Object { [IO.Path]::GetRelativePath($payloadRoot, $_.FullName).Replace('\', '/') } | Sort-Object)
$declared = @($manifest.files.path | Sort-Object)
if (($actual -join "`n") -cne ($declared -join "`n")) { throw 'Payload file coverage mismatch.' }
foreach ($file in $manifest.files) {
    if ($file.path -match '(^|/)\.\.(/|$)|\\|:' -or [IO.Path]::IsPathRooted($file.path)) {
        throw 'Unsafe payload path.'
    }
    $path = Join-Path $payloadRoot $file.path
    if ((Get-Item -LiteralPath $path).Length -ne $file.bytes -or
        (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ine $file.sha256) {
        throw "Payload hash or length mismatch: $($file.path)"
    }
}
$version = Get-Content -LiteralPath (Join-Path $payloadRoot 'VERSION.json') -Raw | ConvertFrom-Json
if ($version.version -ne $manifest.version -or $version.sourceRevision -ne $manifest.sourceRevision -or
    $version.sourceTreeDirty -ne $manifest.sourceTreeDirty) { throw 'VERSION.json does not match the payload.' }

if (-not $MakeAppxPath) {
    $assetsPath = Join-Path $repository 'src/Flow.Windows/obj/project.assets.json'
    if (Test-Path -LiteralPath $assetsPath) {
        $assetsDocument = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json
        foreach ($folder in $assetsDocument.packageFolders.PSObject.Properties.Name) {
            $sdk = Join-Path $folder 'microsoft.windows.sdk.buildtools'
            if (Test-Path -LiteralPath $sdk) {
                $MakeAppxPath = Get-ChildItem -LiteralPath $sdk -Recurse -Filter makeappx.exe |
                    Where-Object { $_.Directory.Name -eq 'x64' } |
                    Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
                if ($MakeAppxPath) { break }
            }
        }
    }
}
if (-not $MakeAppxPath -or -not (Test-Path -LiteralPath $MakeAppxPath -PathType Leaf)) {
    throw 'Pass -MakeAppxPath pointing to x64/makeappx.exe from the Windows SDK, or restore Flow.Windows first.'
}
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $artifacts 'windows-store' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $output.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Output must be a new directory below artifacts/.'
}
if (Test-Path -LiteralPath $output) { throw 'Output already exists. Choose a new directory; previous artifacts are preserved.' }
# Retain staging for inspection and development registration; never install or trust a certificate here.
$stage = Join-Path $output 'staging'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
foreach ($file in $manifest.files) {
    $destination = Join-Path $stage $file.path
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $payloadRoot $file.path) -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $repository 'installer/Flow.Store/AppxManifest.xml') -Destination $stage
$logos = Join-Path $stage 'StoreAssets'
New-Item -ItemType Directory -Path $logos | Out-Null
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Image]::FromFile((Join-Path $repository 'assets/branding/windows/flow-app-tile-512.png'))
try {
    foreach ($logo in @(@('StoreLogo', 50), @('Square44x44Logo', 44), @('Square150x150Logo', 150))) {
        $bitmap = [Drawing.Bitmap]::new([int]$logo[1], [int]$logo[1])
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.DrawImage($source, 0, 0, $bitmap.Width, $bitmap.Height)
            $bitmap.Save((Join-Path $logos ($logo[0] + '.png')), [Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
}
finally { $source.Dispose() }
[xml]$appx = Get-Content -LiteralPath (Join-Path $stage 'AppxManifest.xml') -Raw
$packageVersion = $appx.Package.Identity.Version
if ($packageVersion -notmatch '^\d+\.\d+\.\d+\.0$') { throw 'Store version must end in .0.' }
$package = Join-Path $output "FlowEngine.Store.$packageVersion.x64.msix"
& $MakeAppxPath pack /d $stage /p $package /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx package validation failed. Staging was retained for diagnosis.' }
$report = [ordered]@{
    format = 'flow-store-package-0.1'
    publicVersion = $manifest.version
    packageVersion = $packageVersion
    sourceRevision = $manifest.sourceRevision
    developmentOnly = [bool]($AllowDevelopmentPayload -or $manifest.sourceTreeDirty -or $dirty.Count)
    payloadManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    packageSha256 = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    signed = $false
    installedTest = 'not-run'
    storeCertification = 'not-submitted'
}
[IO.File]::WriteAllText((Join-Path $output 'store-package.json'), ($report | ConvertTo-Json) + "`n")
[IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS'), "$($report.packageSha256)  $([IO.Path]::GetFileName($package))`n")
Write-Output "Unsigned MSIX created; installation and Store certification are still pending: $package"
