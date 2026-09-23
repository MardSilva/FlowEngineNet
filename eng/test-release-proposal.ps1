[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'get-release-proposal.ps1'

function Invoke-Proposal {
    param(
        [Parameter(Mandatory)][string]$Branch,
        [string]$RequestedVersion
    )

    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $scriptPath, '-BranchName', $Branch)
    if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
        $arguments += @('-RequestedVersion', $RequestedVersion)
    }
    $json = & pwsh @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Release proposal failed for branch '$Branch'."
    }
    return ($json -join "`n") | ConvertFrom-Json
}

$current = Invoke-Proposal -Branch 'main'
$currentVersion = [string]$current.projectVersion
$feature = Invoke-Proposal -Branch "feature/$currentVersion-cli-experience"
if ($feature.status -ne 'candidate' -or
    -not $feature.versionMatch -or
    $feature.draftEligible -or
    $feature.proposedVersion -ne $currentVersion) {
    throw 'A synchronized feature branch did not produce the expected candidate proposal.'
}

$main = Invoke-Proposal -Branch 'main' -RequestedVersion $currentVersion
if ($main.status -ne 'ready-for-draft' -or -not $main.draftEligible) {
    throw 'A synchronized main branch did not become eligible for a draft release.'
}

$mismatchVersion = if ($currentVersion -eq '9.9.9-beta.999') { '9.9.9-rc.999' } else { '9.9.9-beta.999' }
$mismatch = Invoke-Proposal -Branch "feature/$mismatchVersion-next-cycle"
if ($mismatch.status -ne 'version-update-required' -or $mismatch.versionMatch) {
    throw 'A mismatched feature branch was not reported as requiring a version update.'
}

$unversioned = Invoke-Proposal -Branch 'docs/readme-review'
if ($unversioned.status -ne 'no-version-proposal' -or $null -ne $unversioned.proposedVersion) {
    throw 'An unversioned branch unexpectedly proposed a release.'
}

Write-Output 'Release proposal tests passed for candidate, draft-ready, mismatch and unversioned branches.'
