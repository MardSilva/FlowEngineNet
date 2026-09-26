function Test-CombinedSmokeEvidence {
    param($Smoke, $Manifest, [string]$ManifestSha256)
    if ($Smoke.format -ne 'flow-windows-combined-payload-smoke-0.1' -or
        $Smoke.status -ne 'passed' -or $Smoke.applicationLaunch -ne 'passed' -or
        $Smoke.cliExecution -ne 'passed' -or $Smoke.manifestCoverage -ne 'passed' -or
        $Smoke.installedStateChanged -ne $false -or
        $Smoke.version -ne $Manifest.version -or $Smoke.sourceRevision -ne $Manifest.sourceRevision -or
        $Smoke.runtimeIdentifier -ne 'win-x64' -or $Smoke.manifestSha256 -cne $ManifestSha256) {
        throw 'Combined application smoke evidence is missing, skipped or belongs to another payload.'
    }
}

function Test-InstallerSbom {
    param($Sbom, $Manifest)
    if ($Sbom.bomFormat -ne 'CycloneDX' -or $Sbom.specVersion -ne '1.5' -or
        $Sbom.metadata.component.name -ne 'FlowEngineNet.Setup' -or
        $Sbom.metadata.component.version -ne $Manifest.publicVersion) {
        throw 'Installer SBOM identity mismatch.'
    }
    foreach ($property in @{
            'flow:sourceRevision' = $Manifest.sourceRevision
            'flow:installerVersion' = $Manifest.installerVersion
            'flow:productCode' = $Manifest.productCode
            'flow:runtimeIdentifier' = $Manifest.runtimeIdentifier
        }.GetEnumerator()) {
        $entries = @($Sbom.metadata.component.properties | Where-Object name -CEQ $property.Key)
        if ($entries.Count -ne 1 -or $entries[0].value -cne $property.Value) {
            throw "Installer SBOM property mismatch: $($property.Key)"
        }
    }
    foreach ($mapping in @{
            'FlowEngineNet.Windows' = 'app/Flow.Windows.exe'
            'FlowEngineNet.Cli' = 'cli/flow.exe'
        }.GetEnumerator()) {
        $components = @($Sbom.components | Where-Object name -CEQ $mapping.Key)
        $files = @($Manifest.payloadFiles | Where-Object path -CEQ $mapping.Value)
        if ($components.Count -ne 1 -or $files.Count -ne 1 -or
            $components[0].version -ne $Manifest.publicVersion) { throw 'Installer SBOM component mismatch.' }
        $hashes = @($components[0].hashes | Where-Object alg -CEQ 'SHA-256')
        if ($hashes.Count -ne 1 -or $hashes[0].content -cne $files[0].sha256) {
            throw 'Installer SBOM executable hash mismatch.'
        }
    }
}
