# Windows application

English | [Português (Brasil)](pt-BR/windows-app.md)

`Flow.Windows` is the first native Windows host for Flow Engine .NET. It is an experimental WinUI 3 application, not a Reader. The `0.2.0-alpha.4` work connects the explanatory shell to the same typed application boundary used by the CLI, without invoking a terminal command or subprocess.

The home page opens a visual workflow for three common operations:

- **Inspect EPUB** reads the local package and shows its title, authors, language, declared EPUB version, manifest and spine counts. It neither converts nor modifies the source file.
- **Import EPUB** creates a `.flow.json` document and can also create a diagnostics report and a localized, script-free HTML book. The initial destination is beside the EPUB; after inspection, its portable name is derived from the publication title.
- **Validate** accepts a local EPUB or `.flow.json`, runs the applicable semantic checks in memory, and writes no output.

The user may choose the generated HTML interface language independently from authored content. Generated controls support automatic selection, English, Brazilian Portuguese and European Portuguese. Book text, titles and navigation labels are never translated silently.

Real application phases feed the progress indicator. An operation can be cancelled cooperatively, and temporary output is removed after cancellation or failure. Existing final output is rejected until the user confirms replacement. A recognized interrupted output can be discarded and rebuilt only after a separate confirmation. The result presents a short human-readable status first; stable diagnostic codes, locations, counts and technical messages remain available in an expandable section.

Publication paths, metadata and cover bytes stay in process and on the local device. The workflow does not access the network, execute publication scripts, load external resources or bypass DRM. Closing the application does not leave a background operation running.

Settings remain small, local and reconstructible. They are written atomically to `%LocalAppData%\FlowEngineNet\settings.json`; an absent, malformed or oversized file falls back to safe defaults. The settings file contains no book path, content, canonical identity or document preference.

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

The host is a graphical `WinExe`, so opening it does not create a terminal. It does not reference `Flow.Cli`, start subprocesses, use WebView2 or access the network. `NavigationView` adapts to the available width, primary destinations are keyboard reachable, headings expose accessibility levels, and controls have accessible names or help text. Theme resources include high-contrast values, and the logo changes between light and dark variants.

The project uses the Microsoft Windows App SDK WinUI component under the [Microsoft Windows App SDK license](https://github.com/microsoft/WindowsAppSDK/blob/main/LICENSE). The dependency is pinned centrally and limited to the Windows host; it does not enter the document model or cross-platform CLI.

## Build and local run

The WinUI project is built explicitly on Windows because the ordinary solution build remains cross-platform:

```powershell
dotnet restore Flow.sln
dotnet build .\src\Flow.Windows\Flow.Windows.csproj --no-restore -p:Platform=x64
dotnet run --project .\src\Flow.Windows\Flow.Windows.csproj -p:Platform=x64
```

The application currently runs from its build output. The existing alpha.3 MSI still installs only the CLI. Start-menu integration, packaging the graphical host, upgrade from alpha.3 and uninstalling both executables belong to the final Windows installer integration increment.

Automated tests cover settings recovery and atomic persistence, personal-folder discovery, noncanonical index behavior, navigation, both localization catalogs, theme selection, operation contracts, source preservation, output policies, cancellation cleanup, preview disposal and hash parity, command escaping, EPUB and Flow validation, XAML security structure, accessibility, high-contrast resources and project dependency boundaries. Local launch and UI Automation smoke tests confirm that the process creates a responsive native window and exposes the document-processing page. Visual review at Windows display scales of 100%, 150% and 200% remains a manual release check because unit tests cannot prove text clipping or physical readability.

The application still has no persistent library database, reading progress, annotations, search, production pagination or installer integration. The personal folder and preview are operational conveniences, not the Flow Reader.
