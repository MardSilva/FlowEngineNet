# Microsoft Store package

English | [Português (Brasil)](pt-BR/windows-store.md)

The Store channel is being prepared for `0.2.0-alpha.4`. Packaging does not promote the engine to a stable release. The public version remains in `Directory.Build.props`; the initial numeric MSIX version is `0.2.4.0`. Increase the MSIX version for every subsequent Store update and keep its fourth component zero.

The reserved identity is `ESSoftwares.FlowEngine`, publisher `CN=21D9EB03-6223-4C3C-91C6-B132CCE87A14`, display publisher `ES Softwares`, family `ESSoftwares.FlowEngine_nxa5g9xs5gbp2`, Store ID `9NMJW9XHMJ0F`. These are public identifiers, not signing credentials.

## Build

Use Windows, PowerShell 7, the repository's .NET SDK and the x64 Windows SDK MakeAppx tool. Restoring Flow.Windows also supplies SDK build tools through NuGet. From a clean checkout:

```powershell
./eng/build-windows-combined-payload.ps1
./eng/test-windows-combined-payload.ps1 -SkipApplicationLaunch
./eng/build-windows-store.ps1 -CombinedPayloadDirectory artifacts/windows-combined-payload
```

The last command reuses the combined GUI/CLI payload without compiling again. It checks the revision, source state, complete file inventory and SHA-256 values. An existing output directory is never replaced. Use `-OutputDirectory artifacts/windows-store-next` for another attempt. `-MakeAppxPath` can select an installed SDK tool explicitly. `-AllowDevelopmentPayload` permits local dirty-tree rehearsals, labels them development-only, and must not be used for a submission candidate.

Output includes an unsigned MSIX, `SHA256SUMS`, `store-package.json` and staging files. MakeAppx performs package validation; this is not an installed-app test or Store certification. The manual `store-package.yml` workflow rehearses the same steps without publishing anything or adding checks to each PR. Its combined build is fresh; cross-run reuse of CI artifacts is not implemented in this workflow.

## Distribution behavior

The MSIX targets Windows desktop x64, declares English and Brazilian Portuguese, and includes the self-contained GUI and CLI. It creates one GUI application entry, with no CLI execution alias or PATH changes, avoiding collisions with an existing MSI. The CLI remains inside the package but is not advertised as a Store command-line installation.

The reserved package identity selects Store-managed update guidance instead of the GUI's GitHub update check. MSI and portable behavior remain unchanged. WebView2 uses a writable local application-data folder, not the package installation directory. The WebView2 Runtime remains a prerequisite; self-contained .NET/Windows App SDK does not bundle it.

The `runFullTrust` capability is required for this desktop application to process user-selected local files. Explain this in certification notes. The package does not install a service, driver or automatic-start task.

## Before submission

- Test a signed test package or development registration on a disposable Windows account/VM. Do not trust a test certificate on customers' machines.
- Exercise startup, settings, About, file selection, EPUB inspection/import/validation and WebView2 previews in both languages, including DPI and keyboard navigation.
- Test update and removal, document MSIX data virtualization and what happens to preferences/cache. Never assume MSI data-retention behavior applies to MSIX. Preserve original books and user exports.
- Check coexistence with MSI, and test a machine without a .NET SDK. Verify behavior when WebView2 Runtime is missing.
- Run the Windows App Certification Kit and review capability declarations, dependency licenses, privacy text and screenshots against the actual package.
- Upload only a clean, tested candidate. Keep publication manual until the Store review and final checks are complete.

Microsoft signs Store-distributed MSIX packages after certification. The unsigned build is not a double-click installer for customers. Building it does not buy, create or install a certificate. See [package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements).

A stable release needs a defined supported scope and evidence for that scope, including installation, upgrades, data preservation and known limitations. The first Store submission can remain an alpha; `1.0` is a separate product decision.
