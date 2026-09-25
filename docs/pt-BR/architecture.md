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
- `Flow.Application`: casos de uso neutros, compartilhados pelos hosts oficiais;
- `Flow.Cli`: composição e operações de linha de comando.
- `Flow.Windows.Shell`: estado, localização, configurações e índices reconstruíveis, sessões temporárias de prévia e orquestração de arquivos locais sobre a fronteira compartilhada, sem dependência de WinUI;
- `Flow.Windows`: composição nativa WinUI 3 para os fluxos visuais.

As referências entre projetos apontam para dentro. O modelo de documentos não depende de EPUB, HTML, CLI ou sistema operacional.

## Fronteira da aplicação

`Flow.Application` recebe streams controlados pelo chamador ou documentos `FlowDocument` já carregados. A camada devolve resultados tipados de inspeção, importação, validação, hash, layout e renderização. Progresso e cancelamento podem ser observados sem `Console`, subprocessos ou caminhos de saída.

A CLI continua responsável pelo parsing, pelo Spectre.Console e pelos códigos de saída. A interface WinUI chama diretamente os mesmos casos de uso e mantém sua própria política tipada para origem, destino, confirmação, gravação temporária, descoberta local e prévia. Nenhum projeto Windows referencia ou inicia a CLI. A representação de comando equivalente serve apenas para mostrar ou copiar texto com o escaping do shell selecionado; ela não executa processos. O PowerShell só pode ser aberto por uma ação explícita do modo avançado, com o comando copiado para a área de transferência, sem execução automática.

## Pipeline de renderização

O documento é validado, estilos são resolvidos pela cascata, o layout produz intenções e o renderer gera a saída. A cascata segue `defaults do Flow < apresentação do documento < preferências do utilizador < limites de segurança do renderer`.

## Segurança

Entradas EPUB usam limites de ZIP, recursos, bytes e dimensões; XML não resolve entidades externas; scripts e URLs externas não são executados; caminhos são normalizados; DRM não é contornado. Diagnósticos registram toda aproximação, conteúdo não suportado ou perda observada.

## Interoperabilidade EPUB

O adaptador preserva ordem do spine, IDs, âncoras e conteúdo suportado. Relatórios de origem, source map, apresentação importada e métricas são não canônicos. O importador não é um validador EPUB 3.3 nem um sistema de leitura completo.
