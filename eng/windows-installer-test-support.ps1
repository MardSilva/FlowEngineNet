function Get-TestMsiPackages {
    param([string]$Directory, $Manifest, [string[]]$ExpectedCultures)
    $expected = @($ExpectedCultures | Sort-Object -Unique)
    $declared = @($Manifest.files | ForEach-Object { [string]$_.culture } | Sort-Object)
    if (($expected -join ',') -cne ($declared -join ',') -or
        (@($Manifest.cultures | Sort-Object) -join ',') -cne ($expected -join ',')) {
        throw 'Installer cultures do not match the requested gate.'
    }
    $files = @(Get-ChildItem -LiteralPath $Directory -Filter '*.msi' -File)
    if ($files.Count -ne $expected.Count) { throw 'Unexpected MSI file count.' }
    foreach ($entry in $Manifest.files) {
        if ([IO.Path]::GetFileName($entry.name) -cne $entry.name -or $entry.name -match '[/\\:]') { throw 'Unsafe MSI name.' }
        $file = $files | Where-Object Name -CEQ $entry.name
        if ($null -eq $file -or (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.sha256) {
            throw 'MSI files do not match the manifest.'
        }
    }
    return $files
}

function New-Alpha3Baseline {
    param([string]$RepositoryRoot, [string]$TestRoot, [string]$PortableArtifactsDirectory,
        [string]$UpgradeCode, [string]$ProductCode, [string]$ComponentGuid,
        [string]$InstallDirectoryName, [string]$RegistryKey, [string]$ProductName)
    # Fixed CLI-only source, not a new executable relabelled as an older release.
    $revision = '1a2fbc50a7a79bc1f9fdccbb7347b39949743bbe'
    $snapshot = Join-Path $TestRoot 'alpha3-source'
    $archive = Join-Path $TestRoot 'alpha3-source.zip'
    & git -C $RepositoryRoot archive --format=zip "--output=$archive" $revision
    if ($LASTEXITCODE -ne 0) { throw 'The pinned alpha.3 source is unavailable; fetch the full repository history.' }
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $snapshot)
    $payload = Join-Path $snapshot 'artifacts/baseline-payload'
    if ($PortableArtifactsDirectory) {
        $cache = [IO.Path]::GetFullPath($PortableArtifactsDirectory)
        $allowed = [IO.Path]::GetFullPath((Join-Path $RepositoryRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
        if (-not $cache.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Baseline cache must be under artifacts.' }
        $manifest = Get-Content -LiteralPath (Join-Path $cache 'portable-manifest.json') -Raw | ConvertFrom-Json
        if ($manifest.version -ne '0.2.0-alpha.3' -or $manifest.sourceRevision -ne $revision) { throw 'Baseline cache identity mismatch.' }
        $zip = @($manifest.files | Where-Object { $_.name -like '*.zip' })
        if ($zip.Count -ne 1 -or [IO.Path]::GetFileName($zip[0].name) -cne $zip[0].name) { throw 'Invalid baseline archive.' }
        $zipPath = Join-Path $cache $zip[0].name
        if ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $zip[0].sha256) { throw 'Baseline archive hash mismatch.' }
        # Extract only the three expected files; reject paths and additional payload.
        $bundle = [IO.Compression.ZipFile]::OpenRead($zipPath)
        try {
            if ((@($bundle.Entries.FullName | Sort-Object) -join ',') -cne (@('LICENSE.txt','VERSION.json','flow.exe' | Sort-Object) -join ',')) { throw 'Unexpected baseline ZIP entries.' }
            [IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $payload)
        } finally { $bundle.Dispose() }
    }
    else {
        & dotnet publish (Join-Path $snapshot 'src/Flow.Cli/Flow.Cli.csproj') -c Release -r win-x64 --self-contained true `
            -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:DebugType=None -p:DebugSymbols=false -p:GenerateDocumentationFile=false `
            "-p:SourceRevisionId=$revision" -p:EnableSourceControlManagerQueries=false -o $payload | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Could not publish the pinned alpha.3 CLI.' }
        Copy-Item -LiteralPath (Join-Path $snapshot 'LICENSE') -Destination (Join-Path $payload 'LICENSE.txt')
        [IO.File]::WriteAllText((Join-Path $payload 'VERSION.json'), (@{
            version='0.2.0-alpha.3'; sourceRevision=$revision; runtimeIdentifier='win-x64'
        } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    }
    $version = Get-Content -LiteralPath (Join-Path $payload 'VERSION.json') -Raw | ConvertFrom-Json
    $binaryVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $payload 'flow.exe')).ProductVersion.Split('+')[0]
    if ($version.version -ne '0.2.0-alpha.3' -or $version.sourceRevision -ne $revision -or $binaryVersion -ne $version.version) { throw 'Baseline executable/version mismatch.' }
    $output = Join-Path $snapshot 'artifacts/isolated-msi'
    & pwsh -NoProfile -File (Join-Path $snapshot 'eng/build-windows-installer.ps1') -PayloadDirectory $payload -OutputDirectory $output `
        -InstallerVersion '0.2.3' -UpgradeCode $UpgradeCode -ProductCode $ProductCode -ExecutableComponentGuid $ComponentGuid `
        -InstallDirectoryName $InstallDirectoryName -ProductRegistryKey $RegistryKey -ProductName $ProductName -Cultures en-US | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the original alpha.3 MSI structure.' }
    return @{ Msi = @(Get-ChildItem -LiteralPath $output -Filter '*.msi')[0].FullName; Revision = $revision }
}

function Invoke-InstalledCommand {
    param([string]$Executable, [string[]]$Arguments, [string]$WorkingDirectory, [string]$PathValue)
    $info = [Diagnostics.ProcessStartInfo]::new($Executable)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.WorkingDirectory = $WorkingDirectory
    if ($PathValue) { $info.Environment['PATH'] = $PathValue }
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($info)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { $process.Kill($true); throw 'Installed command timed out.' }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw "Installed command failed ($($process.ExitCode)): $output" }
        return $output
    } finally { $process.Dispose() }
}

function Test-InstalledWorkflows {
    param([string]$InstallDirectory, [string]$TestRoot, [string]$ExpectedVersion)
    $system = [Environment]::GetFolderPath([Environment+SpecialFolder]::System)
    $path = "$InstallDirectory;$system;$system/WindowsPowerShell/v1.0"
    $powershell = Join-Path $system 'WindowsPowerShell/v1.0/powershell.exe'
    foreach ($command in @(
        @{ Exe=$powershell; Args=@('-NoProfile', '-NonInteractive', '-Command', 'flow --plain help; exit $LASTEXITCODE') },
        @{ Exe=(Join-Path $system 'cmd.exe'); Args=@('/d', '/c', 'flow --plain help') })) {
        $output = Invoke-InstalledCommand -Executable $command.Exe -Arguments $command.Args -WorkingDirectory $TestRoot -PathValue $path
        if (-not $output.Contains("Flow Engine .NET $ExpectedVersion")) { throw 'Installed shell resolved an unexpected Flow version.' }
    }
    $epub = Join-Path $TestRoot 'public-smoke.epub'
    if (-not (Test-Path -LiteralPath $epub)) {
        $zip = [IO.Compression.ZipFile]::Open($epub, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $parts = [ordered]@{
                'mimetype' = 'application/epub+zip'
                'META-INF/container.xml' = '<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0"><rootfiles><rootfile full-path="book.opf" media-type="application/oebps-package+xml"/></rootfiles></container>'
                'book.opf' = '<package xmlns="http://www.idpf.org/2007/opf" version="2.0" unique-identifier="id"><metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:identifier id="id">urn:flow:installer-test</dc:identifier><dc:title>Installer fixture</dc:title><dc:language>en</dc:language></metadata><manifest><item id="chapter" href="chapter.xhtml" media-type="application/xhtml+xml"/><item id="toc" href="toc.ncx" media-type="application/x-dtbncx+xml"/></manifest><spine toc="toc"><itemref idref="chapter"/></spine></package>'
                'chapter.xhtml' = '<html xmlns="http://www.w3.org/1999/xhtml"><head><title>Fixture</title></head><body><h1 id="start">Installer fixture</h1><p>Public test content.</p></body></html>'
                'toc.ncx' = '<ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1"><head/><docTitle><text>Fixture</text></docTitle><navMap><navPoint id="start" playOrder="1"><navLabel><text>Fixture</text></navLabel><content src="chapter.xhtml#start"/></navPoint></navMap></ncx>'
            }
            foreach ($part in $parts.GetEnumerator()) {
                $entry = $zip.CreateEntry($part.Key, [IO.Compression.CompressionLevel]::NoCompression)
                $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
                try { $writer.Write($part.Value) } finally { $writer.Dispose() }
            }
        } finally { $zip.Dispose() }
    }
    $before = (Get-FileHash -LiteralPath $epub).Hash
    $output = Join-Path $TestRoot ("smoke-$([Guid]::NewGuid().ToString('N')).flow.json")
    $cli = Join-Path $InstallDirectory 'flow.exe'
    Invoke-InstalledCommand $cli @('--plain', 'epub-inspect', $epub) $TestRoot $path | Out-Null
    Invoke-InstalledCommand $cli @('--plain', 'import', $epub, '--output', $output) $TestRoot $path | Out-Null
    Invoke-InstalledCommand $cli @('--plain', 'validate', $output) $TestRoot $path | Out-Null
    if ((Get-FileHash -LiteralPath $epub).Hash -ne $before) { throw 'Installed import changed the EPUB source.' }
}
