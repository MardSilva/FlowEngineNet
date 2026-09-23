[CmdletBinding()]
param(
    [string]$BranchName,

    [string]$RequestedVersion,

    [string]$OutputPath,

    [switch]$RequireVersionMatch
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$versionPattern = '^[0-9]+\.[0-9]+\.[0-9]+(?:-(?:alpha|beta|rc)\.[0-9]+)?$'
$branchPattern = '^(?<kind>feature|release)/v?(?<version>[0-9]+\.[0-9]+\.[0-9]+(?:-(?:alpha|beta|rc)\.[0-9]+)?)(?:-|$)'

if ([string]::IsNullOrWhiteSpace($BranchName)) {
    $BranchName = (& git -C $repositoryRoot branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($BranchName)) {
        throw 'Could not resolve the current Git branch.'
    }
}

$projectPath = Join-Path $repositoryRoot 'src/Flow.Cli/Flow.Cli.csproj'
$packageId = (& dotnet msbuild $projectPath -nologo -getProperty:PackageId).Trim()
$projectVersion = (& dotnet msbuild $projectPath -nologo -getProperty:PackageVersion).Trim()
if ($LASTEXITCODE -ne 0 -or
    [string]::IsNullOrWhiteSpace($packageId) -or
    $projectVersion -notmatch $versionPattern) {
    throw 'Could not resolve a supported package identity from MSBuild.'
}

$planPath = Join-Path $PSScriptRoot 'release-plan.json'
$plan = Get-Content -LiteralPath $planPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($plan.format -ne 'flow-cli-release-plan-0.1' -or
    $plan.packageId -ne $packageId -or
    $plan.packageVersion -notmatch $versionPattern -or
    $plan.tag -ne ('v' + $plan.packageVersion) -or
    $plan.channel -notin @('prerelease', 'stable') -or
    $plan.publication -ne 'disabled' -or
    $plan.githubRelease -ne 'draft-only') {
    throw 'The checked-in release plan is invalid or enables unsupported publication.'
}

$branchKind = 'other'
$branchVersion = $null
if ($BranchName -eq 'main') {
    $branchKind = 'main'
    $branchVersion = $projectVersion
}
elseif ($BranchName -match $branchPattern) {
    $branchKind = $Matches.kind
    $branchVersion = $Matches.version
}

if (-not [string]::IsNullOrWhiteSpace($RequestedVersion)) {
    if ($RequestedVersion -notmatch $versionPattern) {
        throw "Requested release version is invalid: $RequestedVersion"
    }
    if ($null -ne $branchVersion -and $branchVersion -ne $RequestedVersion) {
        throw "Requested version '$RequestedVersion' does not match branch proposal '$branchVersion'."
    }
    $branchVersion = $RequestedVersion
}

$versionMatch = $null -ne $branchVersion -and
    $branchVersion -eq $projectVersion -and
    $branchVersion -eq $plan.packageVersion
$draftEligible = $versionMatch -and $branchKind -in @('main', 'release')
$status = if ($null -eq $branchVersion) {
    'no-version-proposal'
}
elseif (-not $versionMatch) {
    'version-update-required'
}
elseif ($draftEligible) {
    'ready-for-draft'
}
else {
    'candidate'
}

$proposal = [ordered]@{
    format = 'flow-cli-release-proposal-0.1'
    branch = $BranchName
    branchKind = $branchKind
    proposedVersion = $branchVersion
    packageId = $packageId
    projectVersion = $projectVersion
    planVersion = $plan.packageVersion
    expectedTag = $plan.tag
    channel = $plan.channel
    packagePublication = $plan.publication
    githubRelease = $plan.githubRelease
    versionMatch = $versionMatch
    draftEligible = $draftEligible
    status = $status
}

if ($RequireVersionMatch -and -not $versionMatch) {
    throw "Release version is not synchronized: branch=$branchVersion, project=$projectVersion, plan=$($plan.packageVersion)."
}
if ($RequireVersionMatch -and -not $draftEligible) {
    throw "Branch '$BranchName' may suggest a version, but only main or release/<version> can create a draft release."
}

$json = $proposal | ConvertTo-Json -Depth 6
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath)
    $pathComparison = if ($env:OS -eq 'Windows_NT') {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }
    if (-not $resolvedOutput.StartsWith(
            $artifactsRoot + [System.IO.Path]::DirectorySeparatorChar,
            $pathComparison)) {
        throw "The proposal output must be under '$artifactsRoot'."
    }

    $parent = Split-Path -Parent $resolvedOutput
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $temporary = $resolvedOutput + '.tmp-' + [Guid]::NewGuid().ToString('N')
    try {
        [System.IO.File]::WriteAllText(
            $temporary,
            ($json.Replace("`r`n", "`n") + "`n"),
            [System.Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $resolvedOutput -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

Write-Output $json
