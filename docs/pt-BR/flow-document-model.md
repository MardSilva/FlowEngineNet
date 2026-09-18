# Modelo de documento Flow

[English](../flow-document-model.md) | Português (Brasil)

## Estado

O modelo é experimental, imutável e independente de HTML, CSS bruto, viewport, páginas, coordenadas e renderer. Records e coleções imutáveis representam o documento; nullable está habilitado.

## Identidade

`DocumentIdentity` contém a identidade estável do documento. `DocumentMetadata` guarda apenas metadados canônicos suportados, como título, idioma principal, autores, subtítulo e descrição. Relatórios completos da fonte ficam fora do documento.

## IDs estáveis

`NodeId`, `DocumentId` e `AssetId` são tipos explícitos. IDs diferenciam maiúsculas de minúsculas, têm comprimento limitado e aceitam o conjunto documentado de caracteres ASCII. Não podem depender de página, posição visual, viewport ou coordenada. Produtores devem derivá-los de identidade semântica persistente.

## Âncoras e índice

`DocumentAnchor` referencia um `NodeId` no próprio documento. `Parse` rejeita sintaxe inválida; `TryParse` não lança para entrada comum inválida. `FlowDocument.Index` permite resolução eficiente e detecta IDs duplicados.

## Estrutura

`FlowDocument` reúne identidade, metadados, `DocumentContent`, assets e apresentação opcional. A árvore usa `DocumentNode` para blocos e `InlineNode` para conteúdo inline.

Blocos incluem `Chapter`, `Section`, `Heading`, `Paragraph`, `BlockQuote`, listas, `Figure`, `Caption`, `Footnote`, `HorizontalRule`, `CodeBlock`, `TableOfContents` e tabelas semânticas. Tabelas preservam caption, head/body/foot, linhas, células, spans positivos, `scope` e `headers`, sem grade calculada ou coordenadas.

Inline inclui `Text`, `Strong`, `Emphasis`, `Underline`, `Strikethrough`, `InlineCode`, `Link`, `FootnoteReference`, `LineBreak`, idioma, direção, isolamento/override bidirecional, ruby e MathML restrito.

## Validação

`DocumentValidator` retorna `ValidationResult` com diagnósticos estáveis. A validação cobre IDs, âncoras, hierarquia, níveis de heading, assets de figuras, notas, TOC, tabelas e os contratos atuais de internacionalização. Ela não substitui validação editorial ou de acessibilidade completa.

## Apresentação e tipografia

`DocumentPresentation` é opcional e não canônico. `TypographySet` define estilos por papel: corpo, título de capítulo, heading 1–6, subtítulo, níveis de TOC, legenda, nota, citação e código. Valores usam tipos como `Length`, `FontWeight`, `FontStyle`, `TextAlignment` e `TextTransform`; strings CSS não entram no domínio.

## Preferências e estilos resolvidos

`UserReadingPreferences` controla fontes, escalas, altura de linha, espaçamento, margens e tema. O serviço puro de resolução aplica:

```text
defaults do Flow
< apresentação do documento
< preferências do utilizador
< limites de segurança do renderer
```

O documento não é alterado, e preferências nunca integram identidade ou conteúdo canônico.

## Fronteira de integridade

Identidade, metadados canônicos, semântica e assets participam da canonicalização versionada. Apresentação, preferências, layout, tema, source map, relatórios e métricas ficam de fora.

## Comportamento adiado

Não há edição colaborativa, paginação física, Reader, PDF, extensões arbitrárias, Content MathML, áudio/vídeo, scripts ou modelo completo de acessibilidade. Consulte [limitações atuais](known-limitations.md).
