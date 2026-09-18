# Arquitetura

[English](../architecture.md) | Português (Brasil)

## Invariante principal

```text
Documento != Layout != Renderização
```

`FlowDocument` contém identidade e semântica canônicas. `LayoutDocument` contém decisões adaptativas de execução. Um renderer produz uma saída específica. Viewport, preferências, tema e paginação não entram no conteúdo canônico.

## Limites dos projetos

- `Flow.Core`: tipos fundamentais sem dependência das camadas superiores;
- `Flow.Documents`: modelo semântico, apresentação tipada, validação, JSON e canonicalização;
- `Flow.Layout`: contexto e layout adaptativo independente de renderer;
- `Flow.Rendering` e `Flow.Rendering.Html`: contratos e saída HTML;
- `Flow.Security`: hash e assinatura local sobre bytes canônicos;
- `Flow.Epub`: adaptação segura e limitada de EPUB;
- `Flow.Epub.Corpus`: evidência de corpus, gates e revisão assistida;
- `Flow.Cli`: composição e operações de linha de comando.

As referências entre projetos apontam para dentro. O modelo de documentos não depende de EPUB, HTML, CLI ou sistema operacional.

## Pipeline de renderização

O documento é validado, estilos são resolvidos pela cascata, o layout produz intenções e o renderer gera a saída. A cascata segue `defaults do Flow < apresentação do documento < preferências do utilizador < limites de segurança do renderer`.

## Segurança

Entradas EPUB usam limites de ZIP, recursos, bytes e dimensões; XML não resolve entidades externas; scripts e URLs externas não são executados; caminhos são normalizados; DRM não é contornado. Diagnósticos registram toda aproximação, conteúdo não suportado ou perda observada.

## Interoperabilidade EPUB

O adaptador preserva ordem do spine, IDs, âncoras e conteúdo suportado. Relatórios de origem, source map, apresentação importada e métricas são não canônicos. O importador não é um validador EPUB 3.3 nem um sistema de leitura completo.
