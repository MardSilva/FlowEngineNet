$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows-installer-test-support.ps1')
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('flow-msi-contract-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $english = Join-Path $temporary 'test.en-US.msi'
    [IO.File]::WriteAllText($english, 'Not an installer: contract fixture only.')
    $manifest = [pscustomobject]@{
        cultures=@('en-US')
        files=@([pscustomobject]@{ name='test.en-US.msi'; culture='en-US'; sha256=(Get-FileHash $english).Hash.ToLowerInvariant() })
    }
    if (@(Get-TestMsiPackages $temporary $manifest @('en-US')).Count -ne 1) { throw 'Single-culture CI gate failed.' }
    function Expect-Rejection([scriptblock]$Action) {
        $rejected = $false
        try { & $Action | Out-Null } catch { $rejected = $true }
        if (-not $rejected) { throw 'Invalid installer candidate was accepted.' }
    }
    Expect-Rejection { Get-TestMsiPackages $temporary $manifest @('en-US','pt-BR') }
    $portuguese = Join-Path $temporary 'test.pt-BR.msi'
    [IO.File]::WriteAllText($portuguese, 'Second contract fixture.')
    Expect-Rejection { Get-TestMsiPackages $temporary $manifest @('en-US') }
    $manifest.cultures += 'pt-BR'
    $manifest.files += [pscustomobject]@{ name='test.pt-BR.msi'; culture='pt-BR'; sha256=(Get-FileHash $portuguese).Hash.ToLowerInvariant() }
    if (@(Get-TestMsiPackages $temporary $manifest @('en-US','pt-BR')).Count -ne 2) { throw 'Bilingual release gate failed.' }
    $manifest.files[1].culture = 'en-US'
    Expect-Rejection { Get-TestMsiPackages $temporary $manifest @('en-US','pt-BR') }
    $manifest.files[1].culture = 'pt-BR'
    [IO.File]::AppendAllText($portuguese, 'modified')
    Expect-Rejection { Get-TestMsiPackages $temporary $manifest @('en-US','pt-BR') }
    $manifest.files[1].name = '../outside.msi'
    Expect-Rejection { Get-TestMsiPackages $temporary $manifest @('en-US','pt-BR') }
    Write-Output 'Installer contract checks passed (7 cases; no installer executed).'
}
finally {
    # Only the freshly created, random fixture directory is eligible for removal.
    $resolved = [IO.Path]::GetFullPath($temporary)
    if ($resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved) -match '^flow-msi-contract-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
