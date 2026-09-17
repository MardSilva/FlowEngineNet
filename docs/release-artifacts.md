# Local release artifacts

Flow can assemble and validate a release candidate for the CLI without publishing it. This workflow is intended for development and CI while the package remains experimental.

Run it from the repository root after restore:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\build-release-artifacts.ps1 -Configuration Release
```

Use `pwsh` on PowerShell 7 or Linux. The default destination is `artifacts/release`, which is ignored by Git. An existing destination is preserved unless `-Force` is supplied. With `-Force`, replacement happens only after the new artifact set has passed every check.

The generated directory contains:

- `FlowEngineNet.Tool.<version>.nupkg`, the framework-dependent .NET tool package;
- `FlowEngineNet.Tool.<version>.cdx.json`, a CycloneDX 1.5 SBOM;
- `release-manifest.json`, the package identity and validation result;
- `SHA256SUMS`, lowercase SHA-256 values for the other three files.

## Versioned release dry-run

`eng/release-plan.json` is the checked-in release intent. It fixes the package ID, semantic version, expected `v<version>` tag and prerelease channel. Its publication field is `disabled`; the dry-run refuses any other value.

After building the local artifacts, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\eng\invoke-release-dry-run.ps1
```

Use `-AllowDirty` only while developing the release scripts. A dirty run is recorded as `passed-with-warnings` and `releaseReady: false`. It cannot be used as releasable evidence. `-Force` replaces existing dry-run evidence after validating the plan and core artifact checksums.

The dry-run adds:

- `FlowEngineNet.Tool.<version>.intoto.jsonl`, an unsigned in-toto Statement v1 with a SLSA provenance v1 predicate;
- `release-dry-run.json`, the version, expected tag, source state and validation outcome;
- `release-evidence.json`, hashes binding the core artifacts to the provenance and dry-run report.

No tag or GitHub Release is created. A local run does not upload anything. CI temporarily transfers the canonical candidate between its own jobs so Windows and Linux validate the exact same package; the artifact expires after one day and is never published to a package feed.

## Reproducibility check

The script invokes `dotnet pack` twice with deterministic compiler settings, no restore, and `ContinuousIntegrationBuild=true`, allowing the .NET SDK to normalize source paths consistently. It does not supply a manual `PathMap`, because a map built from an operating-system path can retain different directory separators in compiler inputs on Windows and Linux. NuGet writes current timestamps and host-specific metadata into ZIP entries, while checked-out and generated text can use the host line ending. The script rewrites each unsigned package in ordinal entry order with data compression disabled, normalizes known UTF-8 text entries to LF, applies a fixed ZIP timestamp and clears host-specific creator and file-attribute fields. It then compares the complete package bytes. A mismatch stops the build.

This proves repeatability for the current source, SDK and canonical build environment. It does not claim that independent compiler executions on different operating systems produce identical managed assemblies.

CI builds the canonical candidate once on Ubuntu after the Windows/Linux source validation matrix passes. `eng/verify-release-artifacts.ps1` then checks the canonical manifest and every `SHA256SUMS` entry, installs that supplied `.nupkg` from an isolated local-only source, and exercises the installed CLI independently on Ubuntu and Windows. Each validator writes `flow-cli-release-platform-validation-0.1` evidence containing the source revision, SDK, package identity, exact canonical hash and commands exercised.

`eng/compare-release-evidence.ps1` produces `flow-cli-multiplatform-release-validation-0.1`. It requires clean checkouts, the same revision, SDK, package identity and core-file hashes, and proof that both platforms installed the one canonical package. This is a portability and artifact-promotion gate, not a reproducible-build certification across operating systems. It covers only the GitHub-hosted Ubuntu and Windows environments in the workflow.

## SBOM scope

The SBOM is derived from the packaged `flow.deps.json`, not from a manually maintained component list. It describes the CLI and the Flow runtime assemblies shipped inside the `.nupkg`, including their dependency relationships, package version, target framework, deployment type and MIT license declaration for project components.

The package is framework-dependent. The .NET runtime and SDK are prerequisites rather than bundled components, so they do not appear as shipped SBOM components. Test-only NuGet packages are also absent because they are not distributed with the CLI.

## Validation

Before promoting the staged directory, the script checks:

- package paths, duplicates and required files;
- NuGet identity, version and `DotnetTool` package type;
- the runtime dependency graph used to build the SBOM;
- CycloneDX structure and dependency references;
- every entry in `SHA256SUMS`;
- installation from a local-only NuGet source and execution of `flow help`.

The package is then uninstalled from the isolated tool directory. The script never changes the user's global tool list, contacts a package feed or publishes an artifact. The dry-run also checks that an existing expected tag points to the source revision; an absent tag is valid because this stage never creates it.

## Current boundary

These files are unsigned local release candidates. The provenance is informative and self-consistent, but it is not signed, hosted by a transparency service or a claim of any SLSA build level. There is no public NuGet package, installer, upgrade test or long-term artifact retention policy yet. Do not distribute the contents of `artifacts/release` as a supported release.
