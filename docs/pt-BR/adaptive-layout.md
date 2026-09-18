# Layout adaptativo 0.1

[English](../adaptive-layout.md) | Português (Brasil)

O layout transforma um `FlowDocument` imutável em `LayoutDocument` sem produzir HTML, CSS, coordenadas ou páginas. O documento original nunca é alterado.

## Modo de leitura

Somente `ReadingMode.Flow` está implementado. `Paged` e `Print` são contratos reservados e lançam `NotSupportedException`.

## Perfis de viewport

- pequeno, abaixo de 600 px: uma coluna, margem de 16 px e largura fluida;
- médio, de 600 a 1199 px: uma coluna, margem de 32 px e conteúdo limitado a 800 px;
- grande, a partir de 1200 px: margem de 64 px e conteúdo limitado a 1120 px.

Duas colunas só aparecem em viewport grande quando o host permite explicitamente. Margens do utilizador e limites de segurança do renderer participam do perfil resolvido.

## Intenções dos nós

Headings usam `KeepWithNext`; figuras usam `KeepWithCaption` e largura máxima; blocos de código usam `AvoidSplit` e `PreserveWhitespace`; notas carregam uma apresentação preferida. São intenções, não posições físicas.

## Validação e identidade

Cada `LayoutNode` preserva o `NodeId` semântico. O motor recusa documentos inválidos e não cria identidade baseada em página, linha, viewport ou coordenada.
