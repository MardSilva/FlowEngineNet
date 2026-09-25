# Windows distribution foundation

English | [Português (Brasil)](pt-BR/windows-distribution.md)

Flow's Windows distribution has a stable product identity before the first installer is introduced. The semantic version remains defined in `Directory.Build.props`; `eng/Flow.WindowsProduct.props` adds only Windows-specific identity and installation policy. This avoids a second public version source.

The initial target is `win-x64`, installed per user under the local application-data programs directory. The permanent upgrade code is `{C412C622-FA2F-400C-88EE-BA5D4A573F7D}`. Windows Installer uses the separate numeric version `0.2.3` for ordering the `0.2.0-alpha.3` package. Future installer versions must increase that numeric value even when the public SemVer changes between alpha, beta, release-candidate and stable channels.

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

## Per-user MSI

The repository can also build localized `en-US` and `pt-BR` MSI packages. They are language alternatives for the same release, not packages to install side by side. Both install the same reviewed self-contained payload under `%LocalAppData%\Programs\FlowEngineNet`, add that directory to the current user's `PATH`, and register Flow in Installed Apps and Programs and Features. No administrator elevation is intended. Open a new terminal after installation before running `flow` by name.

Build both variants with:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/build-windows-installer.ps1
```

The output under `artifacts/windows-installer/` contains both MSI files, SHA-256 checksums, an installer manifest and a CycloneDX document. Existing output is rejected unless `-Force` is supplied. The MSI is unsigned and experimental; Windows may display an unknown-publisher or SmartScreen warning.

For an unattended installation:

```powershell
msiexec.exe /i ".\FlowEngineNet.Setup.0.2.0-alpha.3.en-US.win-x64.msi" /qn /norestart
```

Windows Installer owns repair, upgrade and removal. A repair can be requested with `msiexec.exe /fa <product-code> /qn /norestart`; normal removal should use Windows Installed Apps or Programs and Features. Both localized variants of this release share one fixed `ProductCode`. A future public MSI release must use a new `ProductCode`, keep the permanent `UpgradeCode`, and increase the numeric installer version. This allows a major upgrade to replace the older release and blocks installation of a lower version over a newer one.

The uninstaller removes `flow.exe`, its license and version document, the product registration and only the `PATH` segment created by this MSI. It does not know about or remove books, configured directories, `.flow.json` documents, reports, exports or preferences. There are no Start menu shortcuts and no `.epub` or `.flow.json` associations in this release.

Run the destructive installation test only on Windows:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File ./eng/test-windows-installer.ps1
```

The test uses random product, upgrade, component, registry and directory identities. It never addresses the production product code or an existing personal installation. Within that isolated identity it verifies clean installation, execution without .NET on `PATH`, repair, major upgrade, downgrade rejection and uninstallation. The test also uses a directory containing spaces and Unicode and requires the original user `PATH` to be restored exactly.

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

The portable ZIP and MSI remain unsigned experimental artifacts, although the manually confirmed workflow can now attach them to a draft release. Windows may therefore show an unknown-publisher or SmartScreen warning. There is still no MSIX, graphical application or file association. The update command reports an available package and its checksum but never downloads or executes it. Code signing and automated installation remain separate increments.
