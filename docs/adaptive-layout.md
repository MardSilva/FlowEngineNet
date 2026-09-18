# Adaptive layout 0.1

English | [Português (Brasil)](pt-BR/adaptive-layout.md)

`AdaptiveLayoutEngine` is a pure, renderer-independent transformation from an immutable `FlowDocument` and a runtime `LayoutContext` to an immutable `LayoutDocument`. The result contains semantic-node references, resolved reading styles, responsive constraints, and layout intentions. It contains no markup, style-sheet strings, coordinates, measured boxes, pages, or renderer output.

## Reading mode

Flow 0.1 implements `ReadingMode.Flow`. `Paged` and `Print` are reserved contract values and fail explicitly with `NotSupportedException`; the engine does not approximate either mode or claim production pagination.

## Viewport profiles

Viewport classes are selected from the logical viewport width supplied by the host:

| Category | Width | Columns | Content margin | Maximum content width |
| --- | ---: | ---: | ---: | ---: |
| Small | less than 600 px | 1 | 16 px | 100% of the available content area |
| Medium | 600 px to less than 1200 px | 1 | 32 px | 800 px |
| Large | 1200 px or greater | 1 by default, 2 with explicit permission | 64 px | 1120 px |

Two columns require both a large viewport and `LayoutContext.AllowTwoColumns`. The two-column gap is 48 px. Device class is descriptive host context and does not override viewport evidence.

These values are constraints for a later renderer, not measured output. The margin in the table is the adaptive default. An explicit reader margin replaces it, and a compatible renderer safety minimum is applied last; `LayoutProfile.ContentMargin` and `ResolvedReadingStyle.ContentMargin` expose the same effective value. A renderer remains responsible for fitting content within the actual available area.

## Resolved node intentions

The layout tree preserves every semantic `NodeId` and its parent-child order. The first profile resolves these minimum intentions:

- headings keep with the following content and avoid a break immediately after by default;
- figures keep with captions, scale down to fit, and default to a maximum width of 100%;
- small viewports resolve figures to block placement even when an author prefers floating placement;
- code blocks avoid splitting and preserve whitespace by default;
- footnotes carry their preferred presentation as a typed value.

Optional author intentions override these defaults except when the small-screen figure rule is required for safe flow. The engine resolves typography through the existing cascade and does not mutate the source `FlowDocument`, its presentation, or user preferences.

## Validation and identity

The engine validates the semantic document before producing layout. Invalid hierarchy, references, assets, or IDs prevent layout and expose the validation diagnostic codes in the exception message. Layout identity and version are copied from `DocumentIdentity`; node identity is preserved from each semantic node. None of this runtime layout state participates in a Flow canonical profile or `DocumentHash`.
