# Windows application

English | [Português (Brasil)](pt-BR/windows-app.md)

`Flow.Windows` is the first native Windows host for Flow Engine .NET. It is an experimental WinUI 3 application, not a Reader. The `0.2.0-alpha.4` work connects the explanatory shell to the same typed application boundary used by the CLI, without invoking a terminal command or subprocess.

The home page opens a visual workflow for three common operations:

- **Inspect EPUB** reads the local package and shows its title, authors, language, declared EPUB version, manifest and spine counts. It neither converts nor modifies the source file.
- **Import EPUB** creates a `.flow.json` document and can also create a diagnostics report and a localized, script-free HTML book. The initial destination is beside the EPUB; after inspection, its portable name is derived from the publication title.
- **Validate EPUB** imports a local EPUB in memory, applies the structural and semantic checks, and writes no output. Direct `.flow.json` validation remains available in the CLI; the graphical picker is restricted to EPUB while this host is focused on the EPUB workflow.

The user may choose the generated HTML interface language independently from authored content. Generated controls support automatic selection, English, Brazilian Portuguese and European Portuguese. Book text, titles and navigation labels are never translated silently.

Real application phases feed the progress indicator. An operation can be cancelled cooperatively, and temporary output is removed after cancellation or failure. Existing final output is rejected until the user confirms replacement. A recognized interrupted output can be discarded and rebuilt only after a separate confirmation. The result presents a short human-readable status first; stable diagnostic codes, locations, counts and technical messages remain available in an expandable section.

Publication paths, metadata and cover bytes stay in process and on the local device. The workflow does not access the network, execute publication scripts, load external resources or bypass DRM. Closing the application does not leave a background operation running.

Settings remain small, local and reconstructible. They are written atomically to `%LocalAppData%\FlowEngineNet\settings.json`; an absent, malformed or oversized file falls back to safe defaults. The file may contain the chosen personal-folder path, but it contains no individual book path, publication content, canonical identity or document preference.

## Output safety

The graphical host uses the typed `Flow.Application` cases directly. `Flow.Windows.Shell` owns only the local file policy needed by a desktop host: absolute paths, read-only source streams, title-based suggestions, confirmations and atomic staging. It does not parse console output or reference `Flow.Cli`.

The optional diagnostics sidecar currently uses the UI-specific `flow-windows-diagnostics-0.1` envelope over the same application diagnostics. The generated `.flow.json`, semantic validation, canonical hash and HTML package come from the shared engine. The CLI's full metadata, fidelity, processing and source-map report set remains available through the command line until those choices receive dedicated visual controls.

## Personal folder and preview

The **Personal folder** page stores one user-selected absolute directory in local settings. It discovers at most 500 EPUB files recursively, skips reparse points, sorts paths deterministically and processes books sequentially. The in-memory index contains source paths, basic metadata, safe covers and diagnostics only while the application is running; it is never serialized into a `FlowDocument`, canonical hash or evidence report. Refreshing the page rebuilds it from the source files.

Each discovered publication can be sent to inspection, import, validation or preview. The preview generates a temporary HTML-book package through the same renderer and preserves the canonical document hash across phone, tablet and desktop profiles. These profiles change the renderer viewport only. They are not pages, device emulation or a Reader.

WebView2 receives the package through the fixed virtual host `flow-preview.local`. Navigation is restricted to that host. New windows, downloads, permission requests and every external resource request are blocked. The temporary directory is removed when the preview page closes or the profile changes. A forced process termination can leave a recognized preview directory under local application data; it contains generated local output and is listed as a known limitation.

## Advanced mode

Advanced mode remains disabled by default. When enabled in settings, the operation page shows real progress observations, diagnostic codes and a PowerShell or Command Prompt representation built by `FlowCommandDisplayFormatter`. The representation is never used to run the operation. **Copy command** writes it to the clipboard, **Save log** writes only after the user chooses a destination, and **Copy and open PowerShell** opens a clean PowerShell window after copying the text. It does not paste or execute the command.

## Architecture and accessibility

The application is split into two projects:

- `Flow.Windows.Shell` contains platform-neutral settings, text catalogs, navigation state and visual-operation orchestration over `Flow.Application`;
- `Flow.Windows` contains the WinUI 3 composition root and XAML views.

The host is a graphical `WinExe`, so opening it does not create a terminal. It does not reference or invoke `Flow.Cli`. External applications open only through explicit actions, such as opening PowerShell in advanced mode or an official link in the browser. The restricted local preview uses WebView2, as described above. `NavigationView` adapts to the available width, primary destinations are keyboard reachable, headings expose accessibility levels, and controls have accessible names or help text. Theme resources include high-contrast values, and the logo changes between light and dark variants.

The window preserves a minimum size of 1000 × 660 logical units. The host converts that size for the current monitor DPI, so its minimum visual area remains consistent at 100%, 150% and 200% display scaling. On smaller displays, the limit is clamped to the monitor work area so the title bar and controls remain reachable.

The **How it works** page is an in-application guide rather than a copy of the public website. At wider sizes, a section index stays beside the selected explanation; when space is limited, it becomes a selector above the content. The guide covers the document/layout/rendering boundary, canonical identity, the EPUB pipeline, format roles, fidelity and the current experimental scope. Frequently asked questions use expanders so the primary explanation remains visible. Section selection is keyboard accessible, and both navigation forms share the same localized content in English and Brazilian Portuguese.

The project uses the Microsoft Windows App SDK WinUI component under the [Microsoft Windows App SDK license](https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE). The dependency is pinned centrally and limited to the Windows host; it does not enter the document model or cross-platform CLI.

## About and distribution identity

**About**, in the navigation footer, shows the public version, process architecture and experimental scope. The version comes from assembly metadata generated by the same central configuration as the CLI. The platform-neutral `FlowWindowsProductIdentity` contract accepts distribution metadata without referencing the CLI or installer.

The host reads only the adjacent distribution's `VERSION.json`. A matching manifest supplies the source revision and whether the build included uncommitted changes. Missing metadata is shown as a development or standalone build; malformed, oversized or incompatible metadata leaves the revision unavailable. The page does not expose installation paths, machine names or book metadata, and does not infer MSI installation or publisher trust from a manifest.

The expandable license section works offline. `Assets/LICENSES.txt` is assembled during the build from the Flow MIT license and the notices supplied by the resolved WinUI, WebView2 and runtime packages used by the graphical application. Packages that supply only a license reference retain that reference; license texts are not translated. Self-contained publication also includes the notices supplied by its resolved .NET runtime packs. Build-only tools are not listed as application dependencies. Project, issue-tracker and release links open only when selected; displaying the page makes no network request and performs no update check.

## Explicit update check

In **About**, **Check for updates** queries the official GitHub releases. Prereleases are included by default for this experimental application; clear the checkbox to select stable releases only. The result shows the running version, the latest version in the selected channel and a validated official release link. A newer version is never downloaded or installed automatically.

The application and CLI share `Flow.Updates`, which owns release parsing, semantic-version comparison and the bounded HTTP transport. The CLI retains its command syntax, JSON and exit codes. Requests time out after ten seconds, reject redirects and accept at most 2 MiB of response data. Opening the application or About page does not start a request. Cancellation, network failure, timeout, invalid responses and an empty channel produce localized messages without affecting document operations. Leaving the page cancels an active check.

MSI guidance is shown only when the current application directory matches the per-user installer registration, including its `app` subdirectory. This local evidence is not a signature or integrity check. Without a matching registration, the application leaves the installation method unconfirmed. Updating an MSI installation requires the user to open the release page, choose the new MSI and run it. Automated tests use fake transports and never contact GitHub.

## Build and local run

The WinUI project is built explicitly on Windows because the ordinary solution build remains cross-platform:

```powershell
dotnet restore Flow.sln
dotnet restore .\src\Flow.Windows\Flow.Windows.csproj -p:Platform=x64
dotnet build .\src\Flow.Windows\Flow.Windows.csproj --no-restore -p:Platform=x64
dotnet run --project .\src\Flow.Windows\Flow.Windows.csproj -p:Platform=x64
```

The application can run from its build output or from the self-contained combined payload generated by `eng/build-windows-combined-payload.ps1`. The combined artifact also includes the unchanged portable CLI and an auditable manifest. The alpha.4 MSI consumes this artifact, installs both entry points per user and creates one Start menu shortcut for the graphical application. The CLI has no shortcut and remains available through `flow` in a new terminal.

Automated tests cover settings recovery and atomic persistence, personal-folder discovery, noncanonical index behavior, navigation, both localization catalogs, theme selection, operation contracts, source preservation, output policies, cancellation cleanup, preview disposal and hash parity, command escaping, EPUB and Flow validation, XAML security structure, accessibility, high-contrast resources and project dependency boundaries. Local launch and UI Automation smoke tests confirm that the process creates a responsive native window and exposes the document-processing page. Visual review at Windows display scales of 100%, 150% and 200% remains a manual release check because unit tests cannot prove text clipping or physical readability.

The application still has no persistent library database, reading progress, annotations, search or production pagination. The personal folder and preview are operational conveniences, not the Flow Reader.
