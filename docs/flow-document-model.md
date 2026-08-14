# Flow document model

Flow 0.1 will model a document as immutable semantic data with a first-class identity. Addressable nodes will have validated stable IDs that do not encode page, coordinate, font, or viewport information.

Typed anchors will resolve stable IDs against the document tree. Block and inline nodes will represent meaning rather than raw HTML. Optional presentation intent will use typed typography and layout values and remain separate from reader preferences.

Canonicalization will serialize only canonical identity, selected metadata, assets, and semantic content into deterministic bytes. Presentation, layout context, renderer constraints, and reader state will not participate in the canonical hash.

The precise model and canonical field set will be documented as those milestones are implemented.
