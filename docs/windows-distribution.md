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

## Current boundary

This foundation does not yet publish a self-contained executable, MSI, MSIX, graphical application or file association. It does not install or remove anything from the developer's machine. Installer creation, installed-package upgrade tests and GitHub Release integration remain separate increments.
