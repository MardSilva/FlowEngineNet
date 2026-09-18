[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$repository = [System.IO.Path]::GetFullPath($RepositoryRoot)
$manifestPath = Join-Path $repository 'docs/translation-manifest.json'

function Get-RelativePathNormalized {
    param([Parameter(Mandatory)][string]$Path)

    [System.IO.Path]::GetRelativePath($repository, [System.IO.Path]::GetFullPath($Path)).Replace('\', '/')
}

function Get-NormalizedTextSha256Upper {
    param([Parameter(Mandatory)][string]$Path)

    $text = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $normalized = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($normalized)
    [System.Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes))
}

function Test-RepositoryRelativePath {
    param([Parameter(Mandatory)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path) -or $Path.Contains('\')) {
        throw "Documentation path must be repository-relative and use '/': $Path"
    }

    $resolved = [System.IO.Path]::GetFullPath((Join-Path $repository $Path))
    $prefix = $repository.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Documentation path escapes the repository: $Path"
    }

    $resolved
}

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'docs/translation-manifest.json is missing.'
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.format -ne 'flow-documentation-translations-0.1' -or
    $manifest.sourceLanguage -ne 'en-US' -or
    $manifest.targetLanguage -ne 'pt-BR') {
    throw 'The documentation translation manifest header is invalid.'
}

$expectedSources = @('README.md', 'samples/SampleBook/README.md')
$expectedSources += Get-ChildItem -LiteralPath (Join-Path $repository 'docs') -Filter '*.md' -File |
    ForEach-Object { Get-RelativePathNormalized $_.FullName }
$expectedSources = @($expectedSources | Sort-Object -Unique)
$declaredSources = @($manifest.documents.source | Sort-Object -Unique)
$coverageDifference = @(Compare-Object -ReferenceObject $expectedSources -DifferenceObject $declaredSources -CaseSensitive)
if ($coverageDifference.Count -ne 0) {
    $missing = @($expectedSources | Where-Object { $_ -notin $declaredSources })
    $unexpected = @($declaredSources | Where-Object { $_ -notin $expectedSources })
    throw "Translation manifest coverage mismatch. Missing: $($missing -join ', '). Unexpected: $($unexpected -join ', ')."
}

$seenTargets = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($document in $manifest.documents) {
    $source = Test-RepositoryRelativePath $document.source
    $translation = Test-RepositoryRelativePath $document.translation
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Canonical documentation is missing: $($document.source)"
    }
    if (-not (Test-Path -LiteralPath $translation -PathType Leaf)) {
        throw "Portuguese translation is missing: $($document.translation)"
    }
    if (-not $seenTargets.Add([string]$document.translation)) {
        throw "Duplicate translation target: $($document.translation)"
    }

    $actualHash = Get-NormalizedTextSha256Upper $source
    if ($actualHash -ne [string]$document.sourceSha256) {
        throw "Portuguese translation is stale for $($document.source). Expected source SHA-256 $($document.sourceSha256), actual $actualHash."
    }

    $sourceToTranslation = [System.IO.Path]::GetRelativePath(
        (Split-Path -Parent $source),
        $translation).Replace('\', '/')
    $translationToSource = [System.IO.Path]::GetRelativePath(
        (Split-Path -Parent $translation),
        $source).Replace('\', '/')
    $sourceText = Get-Content -LiteralPath $source -Raw -Encoding UTF8
    $translationText = Get-Content -LiteralPath $translation -Raw -Encoding UTF8
    if (-not $sourceText.Contains("]($sourceToTranslation)", [System.StringComparison]::Ordinal)) {
        throw "Canonical documentation has no direct link to its Portuguese translation: $($document.source)"
    }
    if (-not $translationText.Contains("]($translationToSource)", [System.StringComparison]::Ordinal)) {
        throw "Portuguese documentation has no direct link to its canonical source: $($document.translation)"
    }
}

$documentsToCheck = @($manifest.documents.source) + @($manifest.documents.translation)
$linkPattern = '(?<!!)\[[^\]]*\]\((?<target><[^>]+>|[^)\s]+)(?:\s+"[^"]*")?\)'
foreach ($relativeDocument in $documentsToCheck | Sort-Object -Unique) {
    $documentPath = Test-RepositoryRelativePath $relativeDocument
    $text = Get-Content -LiteralPath $documentPath -Raw -Encoding UTF8
    foreach ($match in [regex]::Matches($text, $linkPattern)) {
        $target = $match.Groups['target'].Value.Trim('<', '>')
        if ($target.StartsWith('#', [System.StringComparison]::Ordinal) -or
            $target -match '^[a-z][a-z0-9+.-]*:' -or
            $target.StartsWith('//', [System.StringComparison]::Ordinal)) {
            continue
        }

        $pathPart = [System.Uri]::UnescapeDataString(($target -split '[?#]', 2)[0])
        if ([string]::IsNullOrWhiteSpace($pathPart)) {
            continue
        }

        $linkedPath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $documentPath) $pathPart))
        if (-not (Test-Path -LiteralPath $linkedPath)) {
            throw "Broken local documentation link in ${relativeDocument}: $target"
        }
    }
}

Write-Output "Documentation translation and local-link checks passed for $($manifest.documents.Count) source/translation pairs."
