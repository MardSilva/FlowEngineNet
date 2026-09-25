# Windows distribution foundation

English | [Português (Brasil)](pt-BR/windows-distribution.md)

Flow's Windows distribution has a stable product identity before the first installer is introduced. The semantic version remains defined in `Directory.Build.props`; `eng/Flow.WindowsProduct.props` adds only Windows-specific identity and installation policy. This avoids a second public version source.

The initial target is `win-x64`, installed per user under the local application-data programs directory. The permanent upgrade code is `{C412C622-FA2F-400C-88EE-BA5D4A573F7D}`. Windows Installer uses the separate numeric version `0.2.4` for ordering the `0.2.0-alpha.4` package. Future installer versions must increase that numeric value even when the public SemVer changes between alpha, beta, release-candidate and stable channels.

The installer may own and remove only its application payload, command alias, installer registration and product shortcuts. Books, configured directories, `.flow.json` documents, preferences, reports and exports are user data. Normal upgrades and uninstallation must preserve them.

## Brand assets

The three 512×512 PNG sources under `assets/branding/source/` were supplied by the repository owner for Flow Engine .NET. Their reviewed hashes and intended uses are recorded in `assets/branding/brand-assets.json`. No separate brand license is currently declared. The source files remain unchanged; generated assets must never replace them.

Use the dark symbol with blue accent on light surfaces, the light symbol with light-blue accent on dark surfaces, and the monochrome symbol only where color is unavailable. Windows product icons use the light symbol over an opaque navy tile so the identity remains visible in both light and dark system themes.

Run the Windows-only generator from the repository root:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/New-FlowWindowsBrandAssets.ps1
```

The generator verifies the source hashes, crops only transparent padding, preserves aspect ratio, and writes fixed 16, 32, 48, 256 and 512 pixel assets plus a multi-image `.ico`. It validates alpha policy and a 4.5:1 palette contrast floor. The committed manifest records the dimensions and SHA-256 of every derivative. No installer banners are generated because the current scope does not use them.

GDI+ is the pinned renderer for this engineering step, so regeneration is supported only on Windows. Committed derivatives can be consumed and verified on other systems. Windows tests regenerate them in a temporary directory and require byte-for-byte agreement; no extra runtime graphics package is added to the Flow engine.

## Portable win-x64 distribution

The first runnable Windows artifact is a self-contained `win-x64` ZIP. It does not require a separately installed .NET SDK or runtime and does not modify the registry, `PATH`, Start menu or installed-program list. Extract the archive to a local directory and run `flow.exe` from there.

Build it from the repository root with PowerShell 7:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/build-windows-portable.ps1
```

The output is written atomically to `artifacts/windows-portable/`. An existing destination is rejected unless `-Force` is supplied. The directory contains the versioned ZIP, `portable-manifest.json`, a CycloneDX 1.5 SBOM and `SHA256SUMS`. The ZIP itself contains only `flow.exe`, `LICENSE.txt` and `VERSION.json`.

The builder publishes the application twice and compares every payload file and the two normalized archives. ZIP entries use ordinal ordering, a fixed timestamp and neutral external attributes. The manifest and version document record the public version, exact Git revision, RID, architecture, localization catalogs and deployment mode without timestamps or local paths.

Run the installed-artifact smoke test with:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/test-windows-portable.ps1
```

The test validates checksums and the SBOM, extracts the ZIP to an isolated temporary directory, removes .NET locations from the child process environment, and exercises English and Brazilian Portuguese help, safe redirected-menu refusal, sample creation, inspection, validation and HTML rendering. Redirected output must contain no ANSI escape sequences.

### Single-file decision

`PublishSingleFile` is enabled because the current CLI, Spectre.Console dependency and both localization catalogs work without sidecar assemblies. `IncludeNativeLibrariesForSelfExtract` and `IncludeAllContentForSelfExtract` remain disabled: the package does not request extraction of its full contents to a temporary directory. If a later dependency cannot meet these conditions, the project will prefer a reviewed multi-file payload over hidden extraction.

The CycloneDX document is derived from the RID-specific dependency graph created by publish. It covers Flow assemblies, Spectre.Console and the bundled .NET runtime pack. All currently shipped components use the MIT license. The SBOM does not describe Windows system libraries, the build host, test-only dependencies or GitHub Actions.

## Combined application and CLI payload

The alpha.4 integration starts with a local combined payload, before any MSI change. It places the unpackaged WinUI 3 application and the existing single-file CLI under one versioned contract. Both entry points are self-contained for `win-x64`; the graphical application remains multi-file because the Windows App SDK requires native libraries, compiled XAML and resource indexes beside the executable.

Build it from the repository root:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/build-windows-combined-payload.ps1
```

The output is written atomically to `artifacts/windows-combined-payload/`. The `payload/app/` directory contains `Flow.Windows.exe` and its runtime files. `payload/cli/flow.exe` is copied from the independently reproducible portable CLI build. `LICENSE.txt` and `VERSION.json` apply to both entry points. `combined-payload-manifest.json` records the public version, Git revision, source-tree state, RID, architecture, packaging method, locales, roles, sizes and SHA-256 of every installable file. `SHA256SUMS` also covers the detached manifest.

The builder publishes the graphical application twice and requires the same file set and hashes. It keeps only the `en-US` and `pt-BR` resource directories, rejects debug and cache files, and does not copy build directories, tests or private EPUBs. The CLI still comes from `build-windows-portable.ps1`, so this increment does not create a second CLI publication policy.

Run the combined smoke test with:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/test-windows-combined-payload.ps1
```

The test checks complete manifest coverage, file hashes, entry-point versions and private path leakage. It runs the CLI after removing .NET runtime hints from the child environment, starts the WinUI application, waits for its native main window and closes only that process. The script does not write the Registry, alter `PATH`, create shortcuts or change installed state. This combined payload is the reviewed input consumed by the MSI builder.

## Per-user MSI

The repository can also build localized `en-US` and `pt-BR` MSI packages. They are language alternatives for the same release, not packages to install side by side. Both consume the reviewed combined payload and install the graphical application under `%LocalAppData%\Programs\FlowEngineNet\app`. The CLI remains available as `%LocalAppData%\Programs\FlowEngineNet\flow.exe`, preserving the alpha.3 command location and the current user's `PATH` entry. Flow is registered in Installed Apps and Programs and Features without requesting administrator elevation. Open a new terminal after installation before running `flow` by name.

Build both variants with:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/build-windows-installer.ps1
```

The output under `artifacts/windows-installer/` contains both MSI files, SHA-256 checksums, an installer manifest and a CycloneDX document. Existing output is rejected unless `-Force` is supplied. The MSI is unsigned and experimental; Windows may display an unknown-publisher or SmartScreen warning.

For an unattended installation:

```powershell
msiexec.exe /i ".\FlowEngineNet.Setup.0.2.0-alpha.4.en-US.win-x64.msi" /qn /norestart
```

Windows Installer owns repair, upgrade and removal. A complete repair can be requested with `msiexec.exe /famus <product-code> /qn /norestart`; normal removal should use Windows Installed Apps or Programs and Features. Both localized variants of this release share one fixed `ProductCode`. A future public MSI release must use a new `ProductCode`, keep the permanent `UpgradeCode`, and increase the numeric installer version. This allows a major upgrade to replace the older release and blocks installation of a lower version over a newer one.

The installer creates one Start menu shortcut, and it opens `Flow.Windows.exe` directly without showing a console. It does not create a CLI shortcut, file association, service, scheduled task or automatic-start entry. The uninstaller removes the application and CLI payloads, license, version document, product registration, product shortcut and only the `PATH` segment created by this MSI. It does not know about or remove books, configured directories, `.flow.json` documents, reports, HTML exports or preferences.

Run the destructive installation test only on Windows:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/test-windows-installer.ps1
```

The test uses random product, upgrade, component, registry and directory identities. It never addresses the production product code or an existing personal installation. Within that isolated identity it verifies clean installation, execution without .NET on `PATH`, repair, major upgrade, downgrade rejection and uninstallation. The test also uses a directory containing spaces and Unicode. Its final check compares normalized, ordered `PATH` entries: harmless separator and trailing-slash formatting changes are accepted, but a changed, removed, or reordered unrelated entry still fails the gate.

The installer is built with WiX Toolset 4.0.6, fixed in the project and licensed under MS-RL. WiX 6 and 7 were not selected because their current distribution adds a separate Open Source Maintenance Fee EULA. WiX is a build-time tool and is not installed with Flow. Its version, license and excluded build-tool role are recorded in the installer SBOM. The MSI uses the standard Windows Installer registration and major-upgrade mechanisms described by [Microsoft](https://learn.microsoft.com/en-us/windows/win32/msi/configuring-add-remove-programs-with-windows-installer) and [WiX](https://docs.firegiant.com/wix/schema/wxs/majorupgrade/).

The MSI is built once for each release candidate and then identified by its checksum. Rebuilding the same source is not expected to reproduce identical MSI bytes because Windows Installer packages require package-level identity metadata for each build. The portable ZIP remains the byte-reproducible Windows artifact; the MSI manifest records the fixed product identity, source revision and payload version used for the installer.

## Explicit update check

Run `flow update check` to consult stable releases, or `flow update check --channel prerelease` to include prereleases. This is an explicit read-only network operation. It validates the official GitHub response and reports the release page, expected package and published SHA-256 when available. It never downloads or runs an installer.

When the current executable matches the registered per-user MSI directory, the command identifies the installation as MSI and instructs the user to download and run the listed newer MSI. A major upgrade then uses the permanent `UpgradeCode` described above. The command also recognizes .NET tool and portable installations and presents the appropriate manual command or replacement procedure. If there is not enough evidence, it reports an unknown method instead of guessing.

## Release gate

The manually confirmed **Draft release** workflow now builds the portable ZIP on Windows, smoke-tests it without a .NET runtime on `PATH`, and passes that exact payload to the MSI builder. A separate Windows runner installs an isolated older test package silently, exercises the installed CLI, repairs it, upgrades it, rejects a downgrade, uninstalls it and checks that owned files, registration and the added `PATH` entry are gone. Random test identities prevent this process from addressing the production product code or a personal Flow installation.

The final Windows gate requires the `.nupkg`, ZIP and MSI to name the same public version and Git revision. It also compares the three files extracted from the promoted ZIP with the payload hashes recorded by the MSI manifest. The formats are not expected to have equal bytes. Installer evidence, distribution manifests, separate SBOMs and a consolidated Windows checksum file are attached only after these checks pass.

The workflow can create only a draft GitHub Release after explicit confirmation and the protected `draft-release` environment. It does not publish to NuGet, create a public release or mark a prerelease as `Latest`.

## Current boundary

`Flow.Application` provides the in-process boundary for EPUB inspection and import, Flow validation and hashing, and HTML rendering. It reports typed progress, cancellation, diagnostics and results without depending on the CLI, a terminal or subprocess execution. The CLI owns file paths, persistence, confirmation and exit codes.

The WinUI 3 application runs as a separate graphical `WinExe`, explains Flow offline and performs local EPUB inspection, import and validation through the shared typed application boundary. It also provides a reconstructible personal-folder index, restricted local preview and opt-in advanced command/log details. It never invokes the CLI; opening PowerShell is a separate explicit action and does not execute the displayed command. The alpha.4 MSI installs this host and the CLI from the same combined payload and exposes only the graphical application through the Start menu. See [windows-app.md](windows-app.md).

The portable ZIP and MSI remain unsigned experimental artifacts, although the manually confirmed workflow can attach them to a draft release. Windows may therefore show an unknown-publisher or SmartScreen warning. There is still no MSIX or file association. The update command reports an available package and its checksum but never downloads or executes it. Code signing and automated installation remain separate increments.
