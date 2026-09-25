[CmdletBinding()]
param(
    [string]$SourceDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets/branding/source'),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets/branding/windows')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Windows)) {
    throw 'Windows brand assets must be generated on Windows because the pinned renderer is GDI+.'
}

Add-Type -AssemblyName System.Drawing

$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$brandManifestPath = Join-Path $repositoryRoot 'assets/branding/brand-assets.json'
$sourceRoot = [System.IO.Path]::GetFullPath($SourceDirectory)
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

function Get-Sha256Upper {
    param([Parameter(Mandatory)][string]$Path)

    [System.Convert]::ToHexString(
        [System.Security.Cryptography.SHA256]::HashData(
            [System.IO.File]::ReadAllBytes($Path)))
}

function Get-VisibleBounds {
    param([Parameter(Mandatory)][System.Drawing.Bitmap]$Bitmap)

    $left = $Bitmap.Width
    $top = $Bitmap.Height
    $right = -1
    $bottom = -1

    for ($y = 0; $y -lt $Bitmap.Height; $y++) {
        for ($x = 0; $x -lt $Bitmap.Width; $x++) {
            if ($Bitmap.GetPixel($x, $y).A -eq 0) {
                continue
            }

            $left = [System.Math]::Min($left, $x)
            $top = [System.Math]::Min($top, $y)
            $right = [System.Math]::Max($right, $x)
            $bottom = [System.Math]::Max($bottom, $y)
        }
    }

    if ($right -lt $left -or $bottom -lt $top) {
        throw 'The source image has no visible pixels.'
    }

    [System.Drawing.Rectangle]::FromLTRB($left, $top, $right + 1, $bottom + 1)
}

function New-ReframedBitmap {
    param(
        [Parameter(Mandatory)][System.Drawing.Bitmap]$Source,
        [Parameter(Mandatory)][int]$Size,
        [Parameter(Mandatory)][int]$Padding,
        [System.Drawing.Color]$Background = [System.Drawing.Color]::Transparent
    )

    $bounds = Get-VisibleBounds -Bitmap $Source
    $available = $Size - (2 * $Padding)
    if ($available -le 0) {
        throw "Padding $Padding leaves no drawable area in a ${Size}x${Size} image."
    }

    $scale = [System.Math]::Min($available / $bounds.Width, $available / $bounds.Height)
    $width = [System.Math]::Max(1, [int][System.Math]::Round($bounds.Width * $scale))
    $height = [System.Math]::Max(1, [int][System.Math]::Round($bounds.Height * $scale))
    $left = [int][System.Math]::Floor(($Size - $width) / 2)
    $top = [int][System.Math]::Floor(($Size - $height) / 2)

    $result = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($result)
    try {
        $graphics.CompositingMode = if ($Background.A -eq 0) {
            [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        }
        else {
            [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        }
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.Clear($Background)
        $destination = [System.Drawing.Rectangle]::new($left, $top, $width, $height)
        $graphics.DrawImage(
            $Source,
            $destination,
            $bounds.X,
            $bounds.Y,
            $bounds.Width,
            $bounds.Height,
            [System.Drawing.GraphicsUnit]::Pixel)
    }
    finally {
        $graphics.Dispose()
    }

    $result
}

function New-ResizedBitmap {
    param(
        [Parameter(Mandatory)][System.Drawing.Bitmap]$Source,
        [Parameter(Mandatory)][int]$Size
    )

    $result = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($result)
    try {
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceOver
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.Clear($Source.GetPixel(0, 0))
        $graphics.DrawImage($Source, [System.Drawing.Rectangle]::new(0, 0, $Size, $Size))
    }
    finally {
        $graphics.Dispose()
    }

    $result
}

function Write-PngAtomic {
    param(
        [Parameter(Mandatory)][System.Drawing.Bitmap]$Bitmap,
        [Parameter(Mandatory)][string]$Path
    )

    $directory = Split-Path -Parent $Path
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporary = Join-Path $directory ('.' + [System.IO.Path]::GetFileName($Path) + '.' + [System.Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        $Bitmap.Save($temporary, [System.Drawing.Imaging.ImageFormat]::Png)
        [System.IO.File]::Move($temporary, $Path, $true)
    }
    finally {
        if ([System.IO.File]::Exists($temporary)) {
            [System.IO.File]::Delete($temporary)
        }
    }
}

function Write-IcoAtomic {
    param(
        [Parameter(Mandatory)][hashtable]$PngPathsBySize,
        [Parameter(Mandatory)][string]$Path
    )

    $sizes = @(16, 32, 48, 256)
    $images = [System.Collections.Generic.List[byte[]]]::new()
    foreach ($size in $sizes) {
        $images.Add([System.IO.File]::ReadAllBytes($PngPathsBySize[$size]))
    }
    $directory = Split-Path -Parent $Path
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporary = Join-Path $directory ('.' + [System.IO.Path]::GetFileName($Path) + '.' + [System.Guid]::NewGuid().ToString('N') + '.tmp')

    try {
        $stream = [System.IO.File]::Open($temporary, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
        $writer = [System.IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([uint16]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]$sizes.Count)
            $offset = 6 + (16 * $sizes.Count)
            for ($index = 0; $index -lt $sizes.Count; $index++) {
                $size = $sizes[$index]
                $encodedSize = if ($size -eq 256) { 0 } else { $size }
                $writer.Write([byte]$encodedSize)
                $writer.Write([byte]$encodedSize)
                $writer.Write([byte]0)
                $writer.Write([byte]0)
                $writer.Write([uint16]1)
                $writer.Write([uint16]32)
                $writer.Write([uint32]$images[$index].Length)
                $writer.Write([uint32]$offset)
                $offset += $images[$index].Length
            }

            foreach ($image in $images) {
                $writer.Write($image)
            }
        }
        finally {
            $writer.Dispose()
            $stream.Dispose()
        }

        [System.IO.File]::Move($temporary, $Path, $true)
    }
    finally {
        if ([System.IO.File]::Exists($temporary)) {
            [System.IO.File]::Delete($temporary)
        }
    }
}

function Get-RelativeLuminance {
    param([Parameter(Mandatory)][System.Drawing.Color]$Color)

    $channels = @($Color.R, $Color.G, $Color.B) | ForEach-Object {
        $value = $_ / 255.0
        if ($value -le 0.04045) { $value / 12.92 } else { [System.Math]::Pow(($value + 0.055) / 1.055, 2.4) }
    }
    (0.2126 * $channels[0]) + (0.7152 * $channels[1]) + (0.0722 * $channels[2])
}

function Get-ContrastRatio {
    param(
        [Parameter(Mandatory)][System.Drawing.Color]$First,
        [Parameter(Mandatory)][System.Drawing.Color]$Second
    )

    $firstLuminance = Get-RelativeLuminance -Color $First
    $secondLuminance = Get-RelativeLuminance -Color $Second
    $lighter = [System.Math]::Max($firstLuminance, $secondLuminance)
    $darker = [System.Math]::Min($firstLuminance, $secondLuminance)
    ($lighter + 0.05) / ($darker + 0.05)
}

function Assert-AlphaPolicy {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][bool]$MustBeOpaque
    )

    $bitmap = [System.Drawing.Bitmap]::new($Path)
    try {
        $transparent = 0
        $visible = 0
        for ($y = 0; $y -lt $bitmap.Height; $y++) {
            for ($x = 0; $x -lt $bitmap.Width; $x++) {
                $alpha = $bitmap.GetPixel($x, $y).A
                if ($alpha -eq 0) { $transparent++ }
                if ($alpha -gt 0) { $visible++ }
                if ($MustBeOpaque -and $alpha -ne 255) {
                    throw "Opaque asset contains alpha at ${x},${y}: $Path"
                }
            }
        }

        if (-not $MustBeOpaque -and ($transparent -eq 0 -or $visible -eq 0)) {
            throw "Transparent symbol asset must contain both transparent and visible pixels: $Path"
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

if (-not [System.IO.File]::Exists($brandManifestPath)) {
    throw "Brand manifest was not found: $brandManifestPath"
}

$brandManifest = Get-Content -LiteralPath $brandManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($brandManifest.format -ne 'flow-brand-assets-0.1') {
    throw "Unsupported brand manifest format: $($brandManifest.format)"
}

$sourceNames = [ordered]@{
    'symbol-on-light' = 'flow-symbol-on-light-512.png'
    'symbol-on-dark' = 'flow-symbol-on-dark-512.png'
    'symbol-monochrome' = 'flow-symbol-monochrome-512.png'
}

foreach ($source in $brandManifest.sources) {
    if (-not $sourceNames.Contains($source.id)) {
        throw "Unexpected brand source id: $($source.id)"
    }

    $sourcePath = Join-Path $sourceRoot $sourceNames[$source.id]
    if (-not [System.IO.File]::Exists($sourcePath)) {
        throw "Brand source was not found: $($source.id)"
    }

    $actualHash = Get-Sha256Upper -Path $sourcePath
    if ($actualHash -ne $source.sha256) {
        throw "Brand source hash mismatch for $($source.id): $actualHash"
    }
}

[System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$onLightSource = [System.Drawing.Bitmap]::new((Join-Path $sourceRoot $sourceNames['symbol-on-light']))
$onDarkSource = [System.Drawing.Bitmap]::new((Join-Path $sourceRoot $sourceNames['symbol-on-dark']))
try {
    $onLight = New-ReframedBitmap -Source $onLightSource -Size 512 -Padding 48
    $onDark = New-ReframedBitmap -Source $onDarkSource -Size 512 -Padding 48
    $tile = New-ReframedBitmap -Source $onDarkSource -Size 512 -Padding 48 -Background ([System.Drawing.ColorTranslator]::FromHtml('#101828'))
    try {
        Write-PngAtomic -Bitmap $onLight -Path (Join-Path $outputRoot 'flow-app-symbol-on-light-512.png')
        Write-PngAtomic -Bitmap $onDark -Path (Join-Path $outputRoot 'flow-app-symbol-on-dark-512.png')
        Write-PngAtomic -Bitmap $tile -Path (Join-Path $outputRoot 'flow-app-tile-512.png')

        $tilePaths = @{}
        foreach ($size in @(16, 32, 48, 256)) {
            $resized = New-ResizedBitmap -Source $tile -Size $size
            try {
                $tilePath = Join-Path $outputRoot "flow-app-tile-$size.png"
                Write-PngAtomic -Bitmap $resized -Path $tilePath
                $tilePaths[$size] = $tilePath
            }
            finally {
                $resized.Dispose()
            }
        }

        Write-IcoAtomic -PngPathsBySize $tilePaths -Path (Join-Path $outputRoot 'flow.ico')
    }
    finally {
        $onLight.Dispose()
        $onDark.Dispose()
        $tile.Dispose()
    }
}
finally {
    $onLightSource.Dispose()
    $onDarkSource.Dispose()
}

Assert-AlphaPolicy -Path (Join-Path $outputRoot 'flow-app-symbol-on-light-512.png') -MustBeOpaque $false
Assert-AlphaPolicy -Path (Join-Path $outputRoot 'flow-app-symbol-on-dark-512.png') -MustBeOpaque $false
foreach ($size in @(16, 32, 48, 256, 512)) {
    Assert-AlphaPolicy -Path (Join-Path $outputRoot "flow-app-tile-$size.png") -MustBeOpaque $true
}

$navy = [System.Drawing.ColorTranslator]::FromHtml('#101828')
$light = [System.Drawing.ColorTranslator]::FromHtml('#E8EEF6')
$lightBlue = [System.Drawing.ColorTranslator]::FromHtml('#60A5FA')
$lightContrast = Get-ContrastRatio -First $light -Second $navy
$blueContrast = Get-ContrastRatio -First $lightBlue -Second $navy
if ($lightContrast -lt 4.5 -or $blueContrast -lt 4.5) {
    throw "The opaque icon palette does not meet the 4.5:1 contrast floor: light=$lightContrast, blue=$blueContrast"
}

$outputDefinitions = @(
    [ordered]@{ path = 'flow-app-symbol-on-light-512.png'; width = 512; height = 512; alpha = 'transparent' },
    [ordered]@{ path = 'flow-app-symbol-on-dark-512.png'; width = 512; height = 512; alpha = 'transparent' },
    [ordered]@{ path = 'flow-app-tile-16.png'; width = 16; height = 16; alpha = 'opaque' },
    [ordered]@{ path = 'flow-app-tile-32.png'; width = 32; height = 32; alpha = 'opaque' },
    [ordered]@{ path = 'flow-app-tile-48.png'; width = 48; height = 48; alpha = 'opaque' },
    [ordered]@{ path = 'flow-app-tile-256.png'; width = 256; height = 256; alpha = 'opaque' },
    [ordered]@{ path = 'flow-app-tile-512.png'; width = 512; height = 512; alpha = 'opaque' },
    [ordered]@{ path = 'flow.ico'; sizes = @(16, 32, 48, 256); alpha = 'opaque-png-frames' }
)

foreach ($definition in $outputDefinitions) {
    $definition.sha256 = (Get-Sha256Upper -Path (Join-Path $outputRoot $definition.path)).ToLowerInvariant()
}

$generatedManifest = [ordered]@{
    format = 'flow-windows-brand-assets-0.1'
    renderer = 'Windows GDI+'
    sourceManifest = 'assets/branding/brand-assets.json'
    framing = [ordered]@{
        canvas = 512
        padding = 48
        preserveAspectRatio = $true
        tileBackground = '#101828'
    }
    contrast = [ordered]@{
        method = 'WCAG relative luminance'
        minimum = 4.5
        lightOnNavy = [System.Math]::Round($lightContrast, 4)
        lightBlueOnNavy = [System.Math]::Round($blueContrast, 4)
    }
    files = $outputDefinitions
}

$manifestJson = ($generatedManifest | ConvertTo-Json -Depth 8).Replace("`r`n", "`n").Replace("`r", "`n") + "`n"
$manifestPath = Join-Path $outputRoot 'manifest.json'
$manifestTemporary = Join-Path $outputRoot ('.manifest.' + [System.Guid]::NewGuid().ToString('N') + '.tmp')
try {
    [System.IO.File]::WriteAllText($manifestTemporary, $manifestJson, $utf8NoBom)
    [System.IO.File]::Move($manifestTemporary, $manifestPath, $true)
}
finally {
    if ([System.IO.File]::Exists($manifestTemporary)) {
        [System.IO.File]::Delete($manifestTemporary)
    }
}

Write-Output "Windows brand assets generated and validated: $outputRoot"
