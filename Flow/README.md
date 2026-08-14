# Flow

> **Warning:** Flow is an experimental adaptive document model. Flow 0.x is not a standardized file format and should not be used for archival or legally binding documents.

Flow is a research-grade .NET reference implementation exploring whether a digital document can preserve canonical identity, semantic structure, stable references, integrity, and provenance independently from its visual rendition.

Flow is not currently a replacement for PDF or EPUB. Version 0.1 focuses on proving the engine foundations; it deliberately excludes readers, editors, cloud services, DRM, and publishing infrastructure.

## Current status

Milestone 1 establishes the repository and dependency structure. Semantic document behavior will be introduced in the following milestones.

## Build

The repository pins .NET SDK 10.0.302. From this directory, run:

```powershell
dotnet build
dotnet test
```

## Architecture

The central invariant is `Document != Layout`. Core semantic projects cannot reference layout or renderer projects. The planned pipeline is:

```text
Input -> FlowDocument -> LayoutDocument -> Renderer
```

See [docs/architecture.md](docs/architecture.md) for project boundaries and dependency rules.

## Sample

The sample book and CLI commands described for Flow 0.1 will be implemented after the semantic model, layout, and renderer milestones exist.
