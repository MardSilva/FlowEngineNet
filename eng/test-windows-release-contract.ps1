$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'windows-release-contract.ps1')
function Expect-Rejection([scriptblock]$Action) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (-not $rejected) { throw 'Invalid release evidence was accepted.' }
}
$manifest = [pscustomobject]@{ version='0.2.0-alpha.4'; sourceRevision=('a' * 40) }
$smoke = [pscustomobject]@{
    format='flow-windows-combined-payload-smoke-0.1'; status='passed'; applicationLaunch='passed'
    cliExecution='passed'; manifestCoverage='passed'; installedStateChanged=$false
    version=$manifest.version; sourceRevision=$manifest.sourceRevision; runtimeIdentifier='win-x64'; manifestSha256=('b' * 64)
}
Test-CombinedSmokeEvidence $smoke $manifest ('b' * 64)
$smoke.applicationLaunch = 'skipped'
Expect-Rejection { Test-CombinedSmokeEvidence $smoke $manifest ('b' * 64) }
$smoke.applicationLaunch = 'passed'
Expect-Rejection { Test-CombinedSmokeEvidence $smoke $manifest ('c' * 64) }
$smoke.sourceRevision = 'wrong'
Expect-Rejection { Test-CombinedSmokeEvidence $smoke $manifest ('b' * 64) }
$installer = [pscustomobject]@{
    publicVersion=$manifest.version; sourceRevision=$manifest.sourceRevision; installerVersion='0.2.4'
    productCode='isolated-fixture'; runtimeIdentifier='win-x64'
    payloadFiles=@(@{path='app/Flow.Windows.exe';sha256=('c' * 64)}, @{path='cli/flow.exe';sha256=('d' * 64)})
}
$sbom = [pscustomobject]@{
    bomFormat='CycloneDX'; specVersion='1.5'
    metadata=@{component=@{name='FlowEngineNet.Setup';version=$manifest.version;properties=@(
        @{name='flow:sourceRevision';value=$installer.sourceRevision}, @{name='flow:installerVersion';value='0.2.4'},
        @{name='flow:productCode';value=$installer.productCode}, @{name='flow:runtimeIdentifier';value='win-x64'})}}
    components=@(
        @{name='FlowEngineNet.Windows';version=$manifest.version;hashes=@(@{alg='SHA-256';content=('c' * 64)})},
        @{name='FlowEngineNet.Cli';version=$manifest.version;hashes=@(@{alg='SHA-256';content=('d' * 64)})})
}
Test-InstallerSbom $sbom $installer
$sbom.components[0].hashes[0].content = 'wrong'
Expect-Rejection { Test-InstallerSbom $sbom $installer }
$sbom.components[0].hashes[0].content = 'c' * 64
$sbom.metadata.component.properties[0].value = 'wrong'
Expect-Rejection { Test-InstallerSbom $sbom $installer }
$sbom.metadata.component.properties[0].value = $installer.sourceRevision
$sbom.components += $sbom.components[0]
Expect-Rejection { Test-InstallerSbom $sbom $installer }
Write-Output 'Windows release contract checks passed (8 cases; no installation or publication).'
