[CmdletBinding()]
param(
    [string]$ArtifactsDirectory,
    [string]$PayloadDirectory,
    [string]$BaselinePortableArtifactsDirectory,
    [ValidateSet('en-US', 'pt-BR')][string[]]$ExpectedCultures = @('en-US', 'pt-BR')
)

$ErrorActionPreference = 'Stop'
if ($env:OS -ne 'Windows_NT') {
    throw 'The MSI installation test must run on Windows.'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
. (Join-Path $PSScriptRoot 'windows-installer-test-support.ps1')
$phaseResults = [Collections.Generic.List[object]]::new()
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
    $process = Start-Process -FilePath "$env:SystemRoot\System32\msiexec.exe" -ArgumentList $allArguments -WindowStyle Hidden -Wait -PassThru
    $phaseResults.Add([ordered]@{ log = [IO.Path]::GetFileName($LogPath); exitCode = $process.ExitCode; passed = $process.ExitCode -in $ExpectedExitCodes })
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

function Invoke-InstalledApplicationShortcut {
    param([Parameter(Mandatory)][string]$ShortcutPath, [Parameter(Mandatory)][string]$ExpectedExecutable)

    $shell = New-Object -ComObject WScript.Shell
    try {
        $shortcut = $shell.CreateShortcut($ShortcutPath)
        if (-not [string]::Equals($shortcut.TargetPath, $ExpectedExecutable, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "The Start-menu shortcut targets an unexpected executable: $($shortcut.TargetPath)"
        }
    }
    finally {
        [Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) | Out-Null
    }

    $bytes = [IO.File]::ReadAllBytes($ExpectedExecutable)
    $pe = [BitConverter]::ToInt32($bytes, 0x3c)
    if ([BitConverter]::ToUInt16($bytes, $pe + 24 + 68) -ne 2) { throw 'The application is not a Windows GUI executable.' }
    $existingIds = @((Get-Process -Name 'Flow.Windows' -ErrorAction SilentlyContinue).Id)
    Start-Process -FilePath $ShortcutPath -WindowStyle Hidden
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $process = $null
    do {
        $candidates = @(Get-Process -Name 'Flow.Windows' -ErrorAction SilentlyContinue | Where-Object { $_.Id -notin $existingIds -and $_.Path -eq $ExpectedExecutable } | Select-Object -First 1)
        if ($candidates.Count -ne 0) { $process = $candidates[0]; break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $process) { throw 'The Start-menu shortcut did not start Flow.Windows.' }
    try {
        do {
            $process.Refresh()
            if ($process.HasExited) { throw "The installed application exited during launch with code $($process.ExitCode)." }
            if ($process.MainWindowHandle -ne [IntPtr]::Zero) { break }
            Start-Sleep -Milliseconds 200
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'The installed application did not create a main window.' }
        if (-not $process.CloseMainWindow() -or -not $process.WaitForExit(5000)) { throw 'The installed application did not close normally.' }
    }
    finally {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
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

function ConvertTo-ComparableUserPath {
    param([AllowNull()][string]$PathValue)
    if ([string]::IsNullOrWhiteSpace($PathValue)) { return '' }

    $entries = @($PathValue.Split(';', [StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object {
            $entry = $_.Trim()
            if ($entry.Length -gt 3) { $entry = $entry.TrimEnd('\') }
            $entry
        } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    return [string]::Join(';', $entries)
}

function Test-UserPathPreserved {
    param([AllowNull()][string]$Before, [AllowNull()][string]$After)
    return [string]::Equals(
        (ConvertTo-ComparableUserPath -PathValue $Before),
        (ConvertTo-ComparableUserPath -PathValue $After),
        [System.StringComparison]::OrdinalIgnoreCase)
}

$manifestPath = Join-Path $artifactsRoot 'installer-manifest.json'
$checksumsPath = Join-Path $artifactsRoot 'SHA256SUMS'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf) -or -not (Test-Path -LiteralPath $checksumsPath -PathType Leaf)) {
    throw 'Installer manifest or SHA256SUMS is missing.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$resultPath = Join-Path $artifactsRoot 'installer-smoke-result.json'
[IO.File]::WriteAllText($resultPath, '{"format":"flow-windows-installer-smoke-0.2","status":"running"}', [Text.UTF8Encoding]::new($false))
if ($manifest.format -ne 'flow-windows-installer-0.2' -or $manifest.scope -ne 'perUser' -or $manifest.signed -or
    $manifest.entryPoints.application -ne 'app/Flow.Windows.exe' -or $manifest.entryPoints.cli -ne 'flow.exe' -or
    $manifest.startMenuShortcut.cliShortcut) {
    throw 'Installer manifest identity or signing claim is invalid.'
}
foreach ($line in Get-Content -LiteralPath $checksumsPath) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Invalid SHA256SUMS line: $line" }
    $path = Join-Path $artifactsRoot $Matches[2]
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Matches[1]) {
        throw "Installer checksum validation failed: $($Matches[2])"
    }
}
$productionMsis = @(Get-TestMsiPackages -Directory $artifactsRoot -Manifest $manifest -ExpectedCultures $ExpectedCultures)
foreach ($msi in $productionMsis) {
    $packageEntry = $manifest.files | Where-Object name -CEQ $msi.Name
    $expectedLanguage = if ($packageEntry.culture -eq 'pt-BR') { '1046' } else { '1033' }
    if ((Get-MsiProperty -Path $msi.FullName -Name 'ProductVersion') -ne $manifest.installerVersion -or
        (Get-MsiProperty -Path $msi.FullName -Name 'ProductLanguage') -ne $expectedLanguage -or
        (Get-MsiProperty -Path $msi.FullName -Name 'ProductCode') -ne $manifest.productCode -or
        (Get-MsiProperty -Path $msi.FullName -Name 'UpgradeCode') -ne $manifest.upgradeCode -or
        (Get-MsiProperty -Path $msi.FullName -Name 'ARPPRODUCTICON') -ne 'FlowProductIcon.ico') {
        throw "Production MSI metadata is inconsistent: $($msi.Name)"
    }
}

$runId = [Guid]::NewGuid().ToString('N')
$testRoot = Join-Path $allowedArtifactsRoot "windows-installer-test-$runId"
$combinedRoot = Join-Path $testRoot 'combined'
$payloadRoot = Join-Path $testRoot 'payload'
$newArtifacts = Join-Path $testRoot 'Pacote novo com espaço Árvore'
$logRoot = Join-Path $testRoot 'logs'
$installDirectoryName = "Flow Teste Árvore $runId"
$installDirectory = Join-Path $env:LOCALAPPDATA "Programs/$installDirectoryName"
$registryKey = "Software\FlowEngineNet\Tests\$runId"
$upgradeCode = "{$([Guid]::NewGuid().ToString().ToUpperInvariant())}"
$oldAuthoredProductCode = "{$([Guid]::NewGuid().ToString().ToUpperInvariant())}"
$newAuthoredProductCode = "{$([Guid]::NewGuid().ToString().ToUpperInvariant())}"
$componentGuid = "{$([Guid]::NewGuid().ToString().ToUpperInvariant())}"
$registrationComponentGuid = "{$([Guid]::NewGuid().ToString().ToUpperInvariant())}"
$metadataComponentGuid = "{$([Guid]::NewGuid().ToString().ToUpperInvariant())}"
$startMenuComponentGuid = "{$([Guid]::NewGuid().ToString().ToUpperInvariant())}"
$payloadComponentNamespace = "{$([Guid]::NewGuid().ToString().ToUpperInvariant())}"
$productName = "Flow Engine .NET Test $runId"
$oldProductCode = $null
$newProductCode = $null
$pathBefore = Get-UserPath
$shortcutDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) $productName
$shortcutPath = Join-Path $shortcutDirectory "$productName.lnk"
$sentinelDirectory = Join-Path $testRoot 'user-data'
$sentinelPath = Join-Path $sentinelDirectory 'user-settings-sentinel.json'
$personalSettings = Join-Path $env:LOCALAPPDATA 'FlowEngineNet/settings.json'
$settingsBefore = if (Test-Path -LiteralPath $personalSettings) { (Get-FileHash -LiteralPath $personalSettings).Hash } else { $null }
if ((Test-Path -LiteralPath $installDirectory) -or (Test-Path -LiteralPath $shortcutDirectory) -or (Test-Path -LiteralPath "HKCU:\$registryKey")) {
    throw 'The random test identity already exists; no installation was attempted.'
}

try {
    New-Item -ItemType Directory -Path $testRoot, $logRoot -Force | Out-Null
    if ([string]::IsNullOrWhiteSpace($PayloadDirectory)) {
        & pwsh -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build-windows-combined-payload.ps1') -OutputDirectory $combinedRoot
        if ($LASTEXITCODE -ne 0) { throw 'Could not build the isolated combined test payload.' }
        $payloadRoot = $combinedRoot
    }
    else {
        $payloadRoot = [System.IO.Path]::GetFullPath($PayloadDirectory)
        if (-not $payloadRoot.StartsWith($allowedArtifactsRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'The supplied test payload must stay below artifacts.'
        }
    }

    $builder = Join-Path $PSScriptRoot 'build-windows-installer.ps1'
    $componentArguments = @('-RegistrationComponentGuid', $registrationComponentGuid, '-MetadataComponentGuid', $metadataComponentGuid,
        '-StartMenuComponentGuid', $startMenuComponentGuid, '-PayloadComponentNamespace', $payloadComponentNamespace,
        '-CompressionLevel', 'none')
    $baseline = New-Alpha3Baseline -RepositoryRoot $repositoryRoot -TestRoot $testRoot -PortableArtifactsDirectory $BaselinePortableArtifactsDirectory `
        -UpgradeCode $upgradeCode -ProductCode $oldAuthoredProductCode -ComponentGuid $componentGuid `
        -InstallDirectoryName $installDirectoryName -RegistryKey $registryKey -ProductName $productName
    & pwsh -NoProfile -ExecutionPolicy Bypass -File $builder -PayloadDirectory $payloadRoot -OutputDirectory $newArtifacts -InstallerVersion '0.2.4' -UpgradeCode $upgradeCode -ProductCode $newAuthoredProductCode -ExecutableComponentGuid $componentGuid -InstallDirectoryName $installDirectoryName -ProductRegistryKey $registryKey -ProductName $productName -Cultures en-US @componentArguments
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the isolated alpha.4 MSI.' }
    $testedManifest = Get-Content -LiteralPath (Join-Path $newArtifacts 'installer-manifest.json') -Raw | ConvertFrom-Json
    if ($testedManifest.sourceRevision -ne $manifest.sourceRevision -or
        ($testedManifest.payloadFiles | ConvertTo-Json -Depth 6 -Compress) -cne ($manifest.payloadFiles | ConvertTo-Json -Depth 6 -Compress)) {
        throw 'The isolated MSI does not use the production candidate payload.'
    }

    $oldMsi = $baseline.Msi
    $newMsi = @(Get-ChildItem -LiteralPath $newArtifacts -Filter '*.msi' -File)[0].FullName
    # Never make a ProductCode eligible for cleanup until its random authored identity is verified.
    if ((Get-MsiProperty -Path $oldMsi -Name 'ProductCode') -ne $oldAuthoredProductCode -or
        (Get-MsiProperty -Path $newMsi -Name 'ProductCode') -ne $newAuthoredProductCode -or
        (Get-MsiProperty -Path $oldMsi -Name 'UpgradeCode') -ne $upgradeCode -or
        (Get-MsiProperty -Path $newMsi -Name 'UpgradeCode') -ne $upgradeCode -or
        $oldAuthoredProductCode -eq $manifest.productCode -or $newAuthoredProductCode -eq $manifest.productCode -or
        $upgradeCode -eq $manifest.upgradeCode -or
        (Get-MsiProductState $oldAuthoredProductCode) -ne -1 -or (Get-MsiProductState $newAuthoredProductCode) -ne -1) {
        throw 'Isolated upgrade packages do not have safe, unused MSI identities.'
    }
    $oldProductCode = $oldAuthoredProductCode
    $newProductCode = $newAuthoredProductCode
    $executable = Join-Path $installDirectory 'flow.exe'
    $applicationExecutable = Join-Path $installDirectory 'app/Flow.Windows.exe'
    New-Item -ItemType Directory -Path $sentinelDirectory -Force | Out-Null
    [IO.File]::WriteAllText($sentinelPath, '{"ownedBy":"user"}', [Text.UTF8Encoding]::new($false))
    $sentinelHash = (Get-FileHash -LiteralPath $sentinelPath).Hash

    # First prove a clean alpha.4 install and repair, then start the historical upgrade independently.
    Invoke-MsiExec -Arguments @('/i', $newMsi) -LogPath (Join-Path $logRoot 'clean-alpha4.log') | Out-Null
    if (-not (Test-Path -LiteralPath $applicationExecutable) -or -not (Test-Path -LiteralPath $shortcutPath)) { throw 'Clean alpha.4 installation is incomplete.' }
    Invoke-InstalledApplicationShortcut -ShortcutPath $shortcutPath -ExpectedExecutable $applicationExecutable
    if (-not (Test-PathEntry -PathValue (Get-UserPath) -ExpectedEntry $installDirectory)) { throw 'Installation did not add its PATH entry.' }
    Test-InstalledWorkflows -InstallDirectory $installDirectory -TestRoot $testRoot -ExpectedVersion $manifest.publicVersion
    $bookHash = (Get-FileHash -LiteralPath (Join-Path $testRoot 'public-smoke.epub')).Hash

    # Exact owned files inside this run's random installation directory only.
    Remove-Item -LiteralPath $executable, $applicationExecutable, $shortcutPath -Force
    Invoke-MsiExec -Arguments @('/famus', $newProductCode) -LogPath (Join-Path $logRoot 'repair-alpha4.log') | Out-Null
    if (-not (Test-Path -LiteralPath $executable) -or -not (Test-Path -LiteralPath $applicationExecutable) -or
        -not (Test-Path -LiteralPath $shortcutPath)) { throw 'MSI repair did not restore the application, CLI and shortcut.' }
    Invoke-InstalledFlow -Executable $executable -WorkingDirectory $installDirectory
    Invoke-InstalledApplicationShortcut -ShortcutPath $shortcutPath -ExpectedExecutable $applicationExecutable
    Invoke-MsiExec -Arguments @('/x', $newProductCode) -LogPath (Join-Path $logRoot 'remove-clean-alpha4.log') | Out-Null
    if ((Test-Path -LiteralPath $installDirectory) -or (Test-Path -LiteralPath $shortcutDirectory) -or
        (Test-PathEntry -PathValue (Get-UserPath) -ExpectedEntry $installDirectory)) { throw 'Clean uninstall left owned resources.' }

    Invoke-MsiExec -Arguments @('/i', $oldMsi) -LogPath (Join-Path $logRoot 'install-alpha3.log') | Out-Null
    if ((Test-Path -LiteralPath $applicationExecutable) -or (Test-Path -LiteralPath $shortcutPath)) { throw 'Historical alpha.3 is not CLI-only.' }
    $baselineHelp = Invoke-InstalledCommand $executable @('--plain', 'help') $testRoot
    if (-not $baselineHelp.Contains('0.2.0-alpha.3')) { throw 'Baseline CLI is not alpha.3.' }
    Invoke-MsiExec -Arguments @('/i', $newMsi) -LogPath (Join-Path $logRoot 'upgrade-alpha3-alpha4.log') | Out-Null
    if ((Get-MsiProductState -ProductCode $oldProductCode) -ne -1) { throw 'Major upgrade left the older product registered.' }
    if ((Get-MsiProductState -ProductCode $newProductCode) -ne 5) { throw 'Major upgrade did not register the newer product.' }
    Invoke-InstalledApplicationShortcut -ShortcutPath $shortcutPath -ExpectedExecutable $applicationExecutable
    Test-InstalledWorkflows -InstallDirectory $installDirectory -TestRoot $testRoot -ExpectedVersion $manifest.publicVersion
    if ((Get-Content -LiteralPath (Join-Path $installDirectory 'VERSION.json') -Raw | ConvertFrom-Json).version -ne $manifest.publicVersion) { throw 'Upgrade left stale version metadata.' }

    Invoke-MsiExec -Arguments @('/i', $oldMsi) -LogPath (Join-Path $logRoot 'downgrade.log') -ExpectedExitCodes @(1603, 1638) | Out-Null
    if ((Get-MsiProductState -ProductCode $newProductCode) -ne 5 -or -not (Test-Path -LiteralPath $applicationExecutable)) { throw 'Rejected downgrade changed the current installation.' }
    Invoke-MsiExec -Arguments @('/x', $newProductCode) -LogPath (Join-Path $logRoot 'uninstall.log') | Out-Null
    if ((Get-MsiProductState -ProductCode $newProductCode) -ne -1) { throw 'Uninstallation left the product registered with Windows Installer.' }
    if ((Test-Path -LiteralPath $installDirectory) -or (Test-Path -LiteralPath $shortcutDirectory) -or
        (Test-Path -LiteralPath "HKCU:\$registryKey") -or
        (Test-PathEntry -PathValue (Get-UserPath) -ExpectedEntry $installDirectory)) {
        throw 'Uninstallation left owned files, registry data or PATH entry behind.'
    }
    if ((Get-FileHash -LiteralPath $sentinelPath).Hash -ne $sentinelHash) { throw 'Uninstallation changed user-owned data.' }
    if ((Get-FileHash -LiteralPath (Join-Path $testRoot 'public-smoke.epub')).Hash -ne $bookHash -or
        @(Get-ChildItem -LiteralPath $testRoot -Filter '*.flow.json' -File).Count -lt 2) { throw 'Uninstallation changed the test book or generated documents.' }
    $settingsAfter = if (Test-Path -LiteralPath $personalSettings) { (Get-FileHash -LiteralPath $personalSettings).Hash } else { $null }
    if ($settingsBefore -ne $settingsAfter) { throw 'Personal settings were changed.' }
    if (-not (Test-UserPathPreserved -Before $pathBefore -After (Get-UserPath))) { throw 'Uninstallation changed an unrelated user PATH entry.' }

    $result = [ordered]@{
        format = 'flow-windows-installer-smoke-0.2'; productionVersion = $manifest.publicVersion; productionInstallerVersion = $manifest.installerVersion
        sourceRevision = $manifest.sourceRevision; installerManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
        baselineRevision = $baseline.Revision; baselineVersion = '0.2.0-alpha.3'; testedVersion = $manifest.publicVersion
        phases = @($phaseResults.ToArray()); testedCultures = @($ExpectedCultures)
        isolatedUpgradeCode = $upgradeCode; installDirectoryIncludedSpacesAndUnicode = $true; dotnetRemovedFromPath = $true
        operations = @('clean-install', 'installed-application', 'start-menu-launch', 'installed-cli', 'repair', 'major-upgrade', 'alpha3-to-alpha4', 'powershell-cli', 'cmd-cli', 'public-epub', 'downgrade-refused', 'uninstall', 'user-data-preserved', 'path-preserved', 'settings-preserved'); status = 'passed'
    }
}
catch {
    [IO.File]::WriteAllText($resultPath, (@{ format='flow-windows-installer-smoke-0.2'; status='failed'; phases=@($phaseResults.ToArray()) } | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    throw
}
finally {
    foreach ($productCode in @($newProductCode, $oldProductCode)) {
        if (-not [string]::IsNullOrWhiteSpace($productCode) -and (Get-MsiProductState -ProductCode $productCode) -eq 5) {
            $cleanupLog = Join-Path $logRoot ("cleanup-" + $productCode.Trim('{}') + '.log')
            $quotedCleanupLog = '"' + $cleanupLog.Replace('"', '\"') + '"'
            $process = Start-Process -FilePath "$env:SystemRoot\System32\msiexec.exe" -ArgumentList @('/x', $productCode, '/qn', '/norestart', '/l*v', $quotedCleanupLog) -WindowStyle Hidden -Wait -PassThru
            if ($process.ExitCode -notin @(0, 1605, 1614, 3010)) {
                throw "Cleanup for isolated product $productCode returned $($process.ExitCode)."
            }
        }
    }
    if (Test-PathEntry -PathValue (Get-UserPath) -ExpectedEntry $installDirectory) {
        throw "The isolated MSI PATH entry remains after cleanup: $installDirectory"
    }
}
[IO.File]::WriteAllText($resultPath, (($result | ConvertTo-Json -Depth 6).Replace("`r`n", "`n") + "`n"), [Text.UTF8Encoding]::new($false))
Write-Output "Windows installer smoke test passed: $($manifest.publicVersion)"
