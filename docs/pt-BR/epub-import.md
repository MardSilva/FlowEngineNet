# Importação EPUB experimental

[English](../epub-import.md) | Português (Brasil)

`Flow.Epub` adapta um subconjunto deliberadamente limitado de EPUB para `FlowDocument`. Não é validador EPUB 3.3 nem sistema de leitura geral.

## Contrato público

`IEpubImporter.ImportAsync` recebe um stream pesquisável ou copiável dentro dos limites configurados e devolve documento opcional, diagnósticos, source map, relatório de metadados, decisões de processamento, medições e evidência de fidelidade. Conteúdo aproximado, não suportado ou perdido nunca é descartado silenciosamente.

O processamento não executa scripts, não acessa a rede, não resolve entidades externas e não contorna DRM. Ordem de leitura vem do spine, nunca da ordem física ou alfabética do ZIP.

## Metadados OPF

O importador lê identifier/`unique-identifier`, títulos e refinements, subtítulo, creators e contributors com papéis, publisher, idiomas, descrição, subjects, datas, direitos, modified, capa e metadados de acessibilidade. Somente título, idioma principal, autores, subtítulo e descrição entram em `DocumentMetadata`; o restante fica no relatório de origem não canônico.

Valores inválidos, refinements órfãos, conflitos, idiomas malformados e ausência de identificador produzem diagnósticos. Relações não são inventadas.

## Manifest e spine

Manifest items preservam ID, caminho normalizado, MIME, propriedades, fallback e media overlay. O spine mantém posição exata, `linear="yes"`/`"no"`, repetições e decisões tipadas de inclusão, substituição ou exclusão. Cadeias de fallback detectam ciclos e referências quebradas. Conteúdo `linear="no"` permanece explícito.

Navigation Document EPUB 3 tem precedência sobre NCX EPUB 2. TOC preserva hierarquia, labels, destinos entre XHTML e fragmentos percent-encoded. Destinos vazios, ausentes, circulares, ambíguos ou conflitantes são diagnosticados.

## Fidelidade

O snapshot da fonte e o [relatório de fidelidade](epub-fidelity.md) contam estruturas representáveis antes e depois da conversão. A evidência é separada do documento, do hash e da renderização.

## Imagens, capas e assets

JPEG, PNG, GIF estático e WebP são validados por MIME e bytes. Dimensões e tamanho têm limites. Assets iguais são deduplicados por SHA-256. `cover-image`, metadado de capa EPUB 2, `img`, `picture/source`, `figure/figcaption` e capas XHTML/SVG podem produzir `Figure` e intenção de capa não canônica.

`<svg><image href>` e `xlink:href` só resolvem recursos locais seguros. SVG passivo é sanitizado: scripts, handlers, objetos, animação ativa, style perigoso e referências externas são removidos. O Flow não transporta SVG ativo nem converte `viewBox`, width, height ou coordenadas para o domínio.

`alt` tem precedência, inclusive vazio para imagem decorativa. Sem `alt`, o importador pode usar `aria-label`, um `aria-labelledby` local e não ambíguo, `title` ou `figcaption`, registrando `EPUB079`. Não usa OCR, nome do arquivo, metadados ou texto próximo para inventar descrição. Ausência completa permanece `EPUB041` e exige revisão humana.

Imagem inline segura vira `Figure` em ordem entre os segmentos de texto, porque o modelo ainda não tem nó de imagem inline. Link seguro ao redor da imagem vira `FigureLink`. A distinção inline/bloco continua como aproximação.

## SVG e MathML

SVG é asset passivo sanitizado, não DOM confiável. MathML preserva apenas uma allowlist de Presentation MathML. Conteúdo ativo, `annotation-xml`, Content MathML e construções não permitidas são removidos ou achatados com diagnóstico; o importador não interpreta fórmulas.

## CSS seguro e tipado

Stylesheets locais, elementos `style` e atributo `style` participam de uma cascata limitada com herança, ordem, especificidade e seletores simples de elemento, classe e ID. Apenas propriedades representáveis viram tipos Flow: família e tamanho de fonte, peso, estilo, line-height, alinhamento, transformação, decoração, letter-spacing, espaçamento de parágrafo e text-indent.

Posicionamento, coordenadas, colunas, floats, layout físico, media queries, variáveis, conteúdo gerado, URLs remotas e JavaScript não entram no documento. CSS bruto não é preservado. Apresentação importada é opcional, serializável para desenvolvimento e excluída da canonicalização.

## Notas

O importador reconhece `epub:type="noteref"`, `footnote`, `endnote`, papéis ARIA equivalentes, notas em outro XHTML, backlinks e múltiplas referências. Destinos viram `DocumentAnchor`; referências órfãs, notas sem referência, backlinks quebrados, ciclos e ambiguidades recebem diagnósticos. Backlink válido continua um link comum porque não há nó dedicado.

## Tabelas

Tabelas preservam caption, `thead`, múltiplos `tbody`, `tfoot`, linhas, células vazias, `colspan`, `rowspan`, `scope` e `headers`. Associações de cabeçalho só permanecem quando apontam para `th` da mesma tabela.

Recuperação determinística preserva conteúdo visível malformado em linhas/células geradas. `EPUB058` indica hierarquia inválida; `EPUB059`, span inválido; `EPUB060`, scope inválido; `EPUB061`, cabeçalho ausente. `EPUB080` identifica `colgroup`/`col` válido que o modelo não representa: nenhuma linha ou célula fantasma é criada. `colgroup` com texto visível ou filhos indevidos continua na recuperação `EPUB058`.

Não há definição de colunas, grade calculada, largura, altura ou inferência automática de cabeçalhos.

## Rastreabilidade e IDs

`EpubSourceMap` liga caminho normalizado + fragmento decodificado a `NodeId`, com ocorrência para spine repetido. Resolve links relativos, próprio XHTML, outro capítulo, dot segments, percent-encoding e links externos permitidos. Colisões e IDs EPUB inválidos são resolvidos deterministicamente e diagnosticados.

Source map, caminhos e evidência de origem não entram no `.flow.json` canônico nem no hash. IDs permanecem estáveis após importações repetidas, serialização, layout e HTML.

## XHTML e conteúdo misto

Headings, parágrafos, links, listas e estruturas suportadas preservam ordem. Containers como `article`, `main`, `section`, `aside`, `header`, `footer`, `address`, `div`, `details`, `summary` e definition lists mantêm filhos e texto mesmo quando seu papel não tem nó Flow próprio. `abbr`, `cite`, `q`, `sub`, `sup`, `mark` e `time` mantêm conteúdo inline e geram diagnóstico agregado quando a semântica é aproximada.

`lang`, `xml:lang`, `dir`, `bdi`, `bdo`, ruby/`rt`/`rp` e mudanças de idioma dentro do parágrafo têm representação semântica. Whitespace significativo é preservado onde o nó exige, como código.

## Limites de segurança

O importador limita tamanho compactado/descompactado, razão de expansão, quantidade de entradas e recursos, XML, imagens e profundidade estrutural. Caminhos absolutos, traversal e aliases ambíguos são rejeitados. DTDs XHTML/NCX legados entram apenas por allowlist exata e nunca são resolvidos; outros DTDs e entidades permanecem proibidos.

Criptografia e DRM são detectados como evidência. O Flow não descriptografa nem tenta identificar sistemas de conta de fornecedores.

## Fixture de teste

Os testes criam EPUBs mínimos próprios, incluindo entradas válidas, inválidas e maliciosas. Livros privados/licenciados ficam fora do repositório e são associados apenas por hashes em execuções locais.

## Limite atual

Ainda não há exportação EPUB, mídia sincronizada, fixed layout, áudio/vídeo, fontes incorporadas no Flow, EPUB CFI, page-list/landmarks completos, Content MathML, imagem AVIF, GIF animado, scripts ou metadados completos de acessibilidade. Consulte [limitações atuais](known-limitations.md).

```powershell
flow epub-inspect livro.epub --json inspection.json
flow import livro.epub --diagnostics-json diagnostics.json --fidelity-report fidelity.json
```

Sem `--output`, o título gera um nome portátil ao lado do EPUB. A escrita só é promovida depois de uma importação completa e válida.
