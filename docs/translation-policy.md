# Documentation translation policy

English | [Português (Brasil)](pt-BR/translation-policy.md)

English (`en-US`) is the canonical language for Flow's public documentation, source code, APIs, command names, options, diagnostic codes, serialized field names, format identifiers, and cryptographic profiles. Brazilian Portuguese (`pt-BR`) editions are maintained for readers; they do not define a separate contract.

Every canonical Markdown document has one declared Portuguese counterpart. `docs/translation-manifest.json` records the source path, translation path, and SHA-256 of the reviewed English text normalized to UTF-8 without BOM and LF. This normalization keeps the check stable across Windows and Linux checkouts. A source edit makes the translation check fail until a reviewer updates the Portuguese text and records the new hash. Changing only the hash without reviewing the translation defeats the policy.

Translations preserve code blocks and invariant tokens. Explanatory prose may use natural Brazilian Portuguese instead of following English sentence order, but it must not strengthen compatibility, conformance, security, accessibility, performance, or release claims. When the two editions disagree, the English document governs and the discrepancy should be corrected as a documentation defect.

Run the same check used by CI from the repository root:

```powershell
pwsh -NoProfile -File ./eng/test-documentation.ps1
```

The check verifies manifest coverage, source hashes, translation files, duplicate targets, safe repository-relative paths, and local Markdown link targets. It does not judge linguistic quality or semantic equivalence; those remain review responsibilities.
