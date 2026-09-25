[CmdletBinding()]
param(
    [string]$ArtifactsDirectory,
    [string]$PayloadDirectory
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') {
    throw 'The MSI installation test must run on Windows.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$allowedArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $allowedArtifactsRoot 'windows-installer'
}
$artifactsRoot = [System.IO.Path]::GetFullPath($ArtifactsDirectory)
if (-not $artifactsRoot.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The installer artifacts directory must be below '$allowedArtifactsRoot'."
}

function Get-MsiProperty {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Name
    )
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $null
    $view = $null
    try {
        $database = $installer.GetType().InvokeMember('OpenDatabase', 'InvokeMethod', $null, $installer, @($Path, 0))
        $query = "SELECT ``Value`` FROM ``Property`` WHERE ``Property``='$Name'"
        $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database, @($query))
        $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null) | Out-Null
        $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
        if ($null -eq $record) {
            return $null
        }
        return $record.GetType().InvokeMember('StringData', 'GetProperty', $null, $record, 1)
    }
    finally {
        if ($null -ne $view) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) | Out-Null }
        if ($null -ne $database) { [Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null }
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
    }
}

function Invoke-MsiExec {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$LogPath,
        [int[]]$ExpectedExitCodes = @(0, 3010)
    )
    $allArguments = @($Arguments) + @('/qn', '/norestart', '/l*v', $LogPath) | ForEach-Object {
        if ($_ -match '\s') { '"' + $_.Replace('"', '\"') + '"' } else { $_ }
    }
    $process = Start-Process -FilePath "$env:SystemRoot\System32\msiexec.exe" -ArgumentList $allArguments -Wait -PassThru
    if ($process.ExitCode -notin $ExpectedExitCodes) {
        $log = if (Test-Path -LiteralPath $LogPath) { Get-Content -LiteralPath $LogPath -Raw } else { '<no log>' }
        throw "msiexec returned $($process.ExitCode), expected $($ExpectedExitCodes -join ', ').`n$log"
    }
    return $process.ExitCode
}

function Get-MsiProductState {
    param([Parameter(Mandatory)][string]$ProductCode)
    $installer = New-Object -ComObject WindowsInstaller.Installer
    try {
        return [int]$installer.GetType().InvokeMember('ProductState', 'GetProperty', $null, $installer, $ProductCode)
    }
    finally {
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
    }
}

function Invoke-InstalledFlow {
    param([Parameter(Mandatory)][string]$Executable, [Parameter(Mandatory)][string]$WorkingDirectory)
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $Executable
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.Environment['PATH'] = [Environment]::GetFolderPath([Environment+SpecialFolder]::System)
    $startInfo.Environment.Remove('DOTNET_ROOT') | Out-Null
    $startInfo.Environment.Remove('DOTNET_ROOT_X64') | Out-Null
    $startInfo.ArgumentList.Add('--language')
    $startInfo.ArgumentList.Add('pt-BR')
    $startInfo.ArgumentList.Add('--plain')
    $startInfo.ArgumentList.Add('help')
    $process = [System.Diagnostics.Process]::Start($startInfo)
    $output = $process.StandardOutput.ReadToEnd() + $process.StandardError.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0 -or
        $output.IndexOf('Comandos:', [System.StringComparison]::Ordinal) -lt 0 -or
        $output.Contains([char]27)) {
        throw "The installed self-contained CLI failed its localized smoke check.`n$output"
    }
}

function Get-UserPath {
    $item = Get-ItemProperty -LiteralPath 'HKCU:\Environment' -Name Path -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $null
    }
    return [string]$item.Path
}

function Test-PathEntry {
    param([AllowNull()][string]$PathValue, [Parameter(Mandatory)][string]$ExpectedEntry)
    if ($null -eq $PathValue) { return $false }
    return @($PathValue.Split(';', [StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { $_.Trim().TrimEnd('\') }) -contains $ExpectedEntry.TrimEnd('\')
}

$manifestPath = Join-Path $artifactsRoot 'installer-manifest.json'
$checksumsPath = Join-Path $artifactsRoot 'SHA256SUMS'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or -not (Test-Path -LiteralPath $checksumsPath -PathType Leaf)) {
    throw 'Installer manifest or SHA256SUMS is missing.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.format -ne 'flow-windows-installer-0.1' -or $manifest.scope -ne 'perUser' -or $manifest.signed) {
    throw 'Installer manifest identity or signing claim is invalid.'
}
foreach ($line in Get-Content -LiteralPath $checksumsPath) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Invalid SHA256SUMS line: $line" }
    $path = Join-Path $artifactsRoot $Matches[2]
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Matches[1]) {
        throw "Installer checksum validation failed: $($Matches[2])"
    }
}
$productionMsis = @(Get-ChildItem -LiteralPath $artifactsRoot -Filter '*.msi' -File)
if ($productionMsis.Count -ne 2) { throw 'Expected one en-US and one pt-BR production MSI.' }
foreach ($msi in $productionMsis) {
    if ((Get-MsiProperty -Path $msi.FullName -Name 'ProductVersion') -ne $manifest.installerVersion -or
        (Get-MsiProperty -Path $msi.FullName -Name 'ProductCode') -ne $manifest.productCode -or
        (Get-MsiProperty -Path $msi.FullName -Name 'UpgradeCode') -ne $manifest.upgradeCode -or
        (Get-MsiProperty -Path $msi.FullName -Name 'ARPPRODUCTICON') -ne 'FlowProductIcon.ico') {
        throw "Production MSI metadata is inconsistent: $($msi.Name)"
    }
}

$runId = [Guid]::NewGuid().ToString('N')
$testRoot = Join-Path $allowedArtifactsRoot "windows-installer-test-$runId"
$portableRoot = Join-Path $testRoot 'portable'
$payloadRoot = Join-Path $testRoot 'payload'
$oldArtifacts = Join-Path $testRoot 'Pacote antigo com espaço Árvore'
$newArtifacts = Join-Path $testRoot 'Pacote novo com espaço Árvore'
$logRoot = Join-Path $testRoot 'logs'
$installDirectoryName = "Flow Teste Árvore $runId"
$installDirectory = Join-Path $env:LOCALAPPDATA "Programs/$installDirectoryName"
$registryKey = "Software\FlowEngineNet\Tests\$runId"
$upgradeCode = "{$([Guid]::NewGuid().ToString().ToUpperInvariant())}"
$oldAuthoredProductCode = "{$([Guid]::NewGuid().ToString().ToUpperInvariant())}"
$newAuthoredProductCode = "{$([Guid]::NewGuid().ToString().ToUpperInvariant())}"
$componentGuid = "{$([Guid]::NewGuid().ToString().ToUpperInvariant())}"
$productName = "Flow Engine .NET Test $runId"
$oldProductCode = $null
$newProductCode = $null
$pathBefore = Get-UserPath

try {
    New-Item -ItemType Directory -Path $testRoot, $logRoot -Force | Out-Null
    if ([string]::IsNullOrWhiteSpace($PayloadDirectory)) {
        & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build-windows-portable.ps1') -OutputDirectory $portableRoot
        if ($LASTEXITCODE -ne 0) { throw 'Could not build the isolated portable test payload.' }
        $zip = @(Get-ChildItem -LiteralPath $portableRoot -Filter '*.zip' -File)
        [System.IO.Compression.ZipFile]::ExtractToDirectory($zip[0].FullName, $payloadRoot)
    }
    else {
        $payloadRoot = [System.IO.Path]::GetFullPath($PayloadDirectory)
        if (-not $payloadRoot.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'The supplied test payload must stay below artifacts.'
        }
    }

    $builder = Join-Path $PSScriptRoot 'build-windows-installer.ps1'
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $builder -PayloadDirectory $payloadRoot -OutputDirectory $oldArtifacts -InstallerVersion '0.2.2' -UpgradeCode $upgradeCode -ProductCode $oldAuthoredProductCode -ExecutableComponentGuid $componentGuid -InstallDirectoryName $installDirectoryName -ProductRegistryKey $registryKey -ProductName $productName -Cultures en-US
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the isolated older MSI.' }
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $builder -PayloadDirectory $payloadRoot -OutputDirectory $newArtifacts -InstallerVersion '0.2.3' -UpgradeCode $upgradeCode -ProductCode $newAuthoredProductCode -ExecutableComponentGuid $componentGuid -InstallDirectoryName $installDirectoryName -ProductRegistryKey $registryKey -ProductName $productName -Cultures en-US
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the isolated newer MSI.' }

    $oldMsi = @(Get-ChildItem -LiteralPath $oldArtifacts -Filter '*.msi' -File)[0].FullName
    $newMsi = @(Get-ChildItem -LiteralPath $newArtifacts -Filter '*.msi' -File)[0].FullName
    $oldProductCode = Get-MsiProperty -Path $oldMsi -Name 'ProductCode'
    $newProductCode = Get-MsiProperty -Path $newMsi -Name 'ProductCode'
    if ($oldProductCode -ne $oldAuthoredProductCode -or
        $newProductCode -ne $newAuthoredProductCode -or
        $oldProductCode -eq $newProductCode -or
        (Get-MsiProperty -Path $oldMsi -Name 'UpgradeCode') -ne $upgradeCode -or
        (Get-MsiProperty -Path $newMsi -Name 'UpgradeCode') -ne $upgradeCode) {
        throw 'Isolated upgrade packages do not have the expected MSI identities.'
    }

    Invoke-MsiExec -Arguments @('/i', $oldMsi) -LogPath (Join-Path $logRoot 'install-old.log') | Out-Null
    $executable = Join-Path $installDirectory 'flow.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Clean installation did not create flow.exe.' }
    Invoke-InstalledFlow -Executable $executable -WorkingDirectory $installDirectory
    if (-not (Test-PathEntry -PathValue (Get-UserPath) -ExpectedEntry $installDirectory)) { throw 'Clean installation did not add its own user PATH entry.' }

    Remove-Item -LiteralPath $executable -Force
    Invoke-MsiExec -Arguments @('/fa', $oldProductCode) -LogPath (Join-Path $logRoot 'repair-old.log') | Out-Null
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'MSI repair did not restore flow.exe.' }

    Invoke-MsiExec -Arguments @('/i', $newMsi) -LogPath (Join-Path $logRoot 'upgrade.log') | Out-Null
    if ((Get-MsiProductState -ProductCode $oldProductCode) -ne -1) { throw 'Major upgrade left the older product registered.' }
    if ((Get-MsiProductState -ProductCode $newProductCode) -ne 5) { throw 'Major upgrade did not register the newer product.' }

    Invoke-MsiExec -Arguments @('/i', $oldMsi) -LogPath (Join-Path $logRoot 'downgrade.log') -ExpectedExitCodes @(1603, 1638) | Out-Null
    if ((Get-MsiProductState -ProductCode $newProductCode) -ne 5 -or -not (Test-Path -LiteralPath $executable)) { throw 'Rejected downgrade changed the current installation.' }

    Invoke-MsiExec -Arguments @('/x', $newProductCode) -LogPath (Join-Path $logRoot 'uninstall.log') | Out-Null
    if ((Get-MsiProductState -ProductCode $newProductCode) -ne -1) { throw 'Uninstallation left the product registered with Windows Installer.' }
    if ((Test-Path -LiteralPath $installDirectory) -or
        (Test-Path -LiteralPath "HKCU:\$registryKey") -or
        (Test-PathEntry -PathValue (Get-UserPath) -ExpectedEntry $installDirectory)) {
        throw 'Uninstallation left owned files, registry data or PATH entry behind.'
    }
    if ((Get-UserPath) -ne $pathBefore) { throw 'Uninstallation changed an unrelated user PATH entry.' }

    $result = [ordered]@{
        format = 'flow-windows-installer-smoke-0.1'; productionVersion = $manifest.publicVersion; productionInstallerVersion = $manifest.installerVersion
        isolatedUpgradeCode = $upgradeCode; installDirectoryIncludedSpacesAndUnicode = $true; dotnetRemovedFromPath = $true
        operations = @('clean-install', 'installed-cli', 'repair', 'major-upgrade', 'downgrade-refused', 'uninstall', 'path-preserved'); status = 'passed'
    }
    [System.IO.File]::WriteAllText((Join-Path $artifactsRoot 'installer-smoke-result.json'), (($result | ConvertTo-Json -Depth 5).Replace("`r`n", "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))
    Write-Output "Windows installer smoke test passed: $($manifest.publicVersion)"
}
finally {
    foreach ($productCode in @($newProductCode, $oldProductCode)) {
        if (-not [string]::IsNullOrWhiteSpace($productCode)) {
            $cleanupLog = Join-Path $logRoot ("cleanup-" + $productCode.Trim('{}') + '.log')
            $quotedCleanupLog = '"' + $cleanupLog.Replace('"', '\"') + '"'
            $process = Start-Process -FilePath "$env:SystemRoot\System32\msiexec.exe" -ArgumentList @('/x', $productCode, '/qn', '/norestart', '/l*v', $quotedCleanupLog) -Wait -PassThru
            if ($process.ExitCode -notin @(0, 1605, 1614, 3010)) {
                Write-Warning "Cleanup for isolated product $productCode returned $($process.ExitCode)."
            }
        }
    }
    if (Test-PathEntry -PathValue (Get-UserPath) -ExpectedEntry $installDirectory) {
        throw "The isolated MSI PATH entry remains after cleanup: $installDirectory"
    }
}
