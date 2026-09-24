# Limitações resolvidas ou reduzidas

[English](../resolved-limitations.md) | Português (Brasil)

Este histórico registra fronteiras removidas ou reduzidas por implementação e testes. Restrições que ainda existem ficam em [limitações conhecidas](known-limitations.md).

## Flow 0.2 alpha

### Inventário privado de EPUB

A CLI descobre, calcula hashes, deduplica e classifica diretórios privados sem converter os originais nem guardar identidade editorial, caminho ou conteúdo. Detecta corrupção, estrutura inadequada, XML Encryption e obfuscação de fontes. O catálogo continua privado porque hashes podem identificar bytes.

### Qualificação privada em lote

`epub-inventory-qualify` associa fontes por SHA-256 e executa duas vezes todo o pipeline para cada candidato, sem interromper o lote após uma falha. O relatório neutro registra fases, contagens, fidelidade e determinismo. Material privado permanece fora do Git.

### Doctypes XHTML legados

O leitor aceita por allowlist declarações públicas HTML/XHTML e NCX encontradas em livros antigos, ignorando o DTD sem resolver rede ou entidades. Declarações desconhecidas, internal subsets e entidades continuam proibidas.

### Imagens em headings e marcadores anchor

Imagens dentro de headings conservam asset, ordem e texto alternativo por meio de `Figure`; headings só com imagem recebem label semântico. `<a>` sem `href`, mas com `id` ou `name`, é tratado como marcador transparente em vez de link quebrado. Âncoras realmente inválidas ainda recebem diagnóstico.

### Fidelidade de containers neutros

`div` e `span` neutros achatados agora são transformação informativa `EPUB075`, não aproximação ou perda. Semântica especializada ainda não representada permanece aproximação. A execução privada registrou 22.598 transformações sem mudar documento ou hash.

### MIME legado de TrueType

`application/x-font-truetype` passou a usar a mesma política do MIME atual de TTF. Fontes conhecidas geram `EPUB074` como substituição tipográfica; bytes e licença da fonte não entram no Flow.

### Page map legado do EPUB 2

`application/oebps-page-map+xml` é reconhecido e gera `EPUB076` em vez de `EPUB009`. O XML não é aberto e seus labels/destinos não viram paginação ou TOC.

### Quatro perdas finais de fidelidade privada

Duas eram parágrafos somente com imagem já preservada, corrigidos na medição. Uma imagem com link dentro de containers transparentes passou a conservar `FigureLink`. Outra diferia do manifest apenas por caixa e agora usa o caminho único com `EPUB077`. As 14 publicações elegíveis ficaram sem unidade perdida.

### Reparo de nível de heading

Saltos inválidos no XHTML podem receber o menor ajuste determinístico dentro de cada recurso, com `EPUB071`. Isso permite produzir um `FlowDocument` válido sem fingir reconstrução editorial. Documentos Flow criados diretamente continuam sob validação estrita.

### Reconciliação de notas, mail e links de figuras

Referência de nota sem fragmento pode apontar para a única nota semântica do recurso e gera `EPUB072`. `mailto` seguro percent-encoded é preservado. Links seguros de imagens usam o modelo tipado `FigureLink`; associações não representáveis deixaram de ser contadas como perda.

### Classificação privada das diferenças

`epub-inventory-matrix` verifica o hash do relatório e separa aproximação, não suportado, perda, defeito da fonte, erro do Flow e revisão humana. Contagens agregadas permanecem tipadas. A matriz não substitui revisão editorial ou visual.

### Revisão visual do corpus

`epub-inventory-review` gera pacotes mobile/desktop e amostras orientadas para todos os candidatos qualificados. Uma falha não bloqueia os demais; decisões humanas continuam pendentes. Duas árvores privadas repetidas tiveram os mesmos 718 arquivos.

### Triagem de fontes incorporadas

Recursos OTF/TTF antes genéricos `EPUB009` foram reconhecidos como fontes referenciadas por CSS. Eles agora usam `EPUB074` e causa tipada de substituição, sem alegar incorporação ou permissão de redistribuição.

### Links em figuras

`FigureLink` preserva destino interno por `DocumentAnchor` e HTTP/HTTPS/`mailto` absolutos seguros. O recurso participa de `flow-json-0.2` e `flow-c14n-0.2`; leitores 0.1 e canonicalizador legado preservam a fronteira anterior.

### Gate real de publicação grande

Uma publicação privada, legal e sem DRM concluiu duas execuções automáticas idênticas e revisão assistida mobile/desktop. Isso fechou a ausência de execução real para aquele arquivo, sem criar garantia universal de tamanho ou conformidade.

### Preservação de imagens inline

Imagens seguras dentro de conteúdo misto viram `Figure` na ordem correta, com asset deduplicado. A publicação grande passou de 41 imagens medidas como perdidas para zero. A distinção inline/bloco permanece aproximação do modelo.

### Links internos e backlinks

Localização usa o ancestral semântico mapeado mais próximo, e a auditoria verifica destinos estruturais. Na publicação verificada, 146 links internos e 120 backlinks de notas resolveram sem aproximação. Spine repetido e ausência de nó específico de backlink continuam limitados.

### Responsividade do pacote HTML

Capas, figuras, headings longos e tabelas deixaram de causar overflow global no viewport testado. Tabelas largas têm rolagem local e alvos de nota recebem destaque. Não houve auditoria completa de browsers ou tecnologias assistivas.

### CLI operacional de corpus e gate

Corpus, gate e revisão assistida saíram de APIs internas para comandos localizados. Escrita transacional, `--force`, `--resume`, lock por destino, consulta e limpeza por UUID impedem que saída parcial ou diretório arbitrário seja tratado como resultado válido.

### Evidência local de distribuição

A CLI pode ser empacotada como `FlowEngineNet.Tool`, instalada isoladamente e exercitada em Windows e Linux. Um builder Ubuntu cria o candidato normalizado canônico, checksums, SBOM e manifesto; ambos os sistemas validam o mesmo pacote. Publicação, assinatura, macOS e builders independentes continuam fora.

### Experiência rica opcional da CLI

A ajuda antes linear agora usa o mesmo catálogo tipado da ajuda específica e do menu opcional por teclado. Os assistentes chamam o parser e as operações existentes para EPUB, documentos Flow, renderização, corpus, gate e manutenção. Terminais compatíveis recebem ajuda responsiva, fases de progresso explícitas e painéis de resultado; comandos diretos e saída redirecionada continuam em texto Plain estável.

A apresentação detecta redirecionamento e capacidades do terminal, respeita `--no-color` e `NO_COLOR` e oferece `--plain` como fallback explícito. Cancelamento, fim inesperado da entrada, falha de desenho e conclusão normal restauram o estado do terminal. Essa cobertura automatizada não substitui uma auditoria externa de acessibilidade.

### Proposta de versão e draft protegido

O CI agora deriva um candidato de branches versionadas e compara a versão com MSBuild e o plano registrado. A sugestão é somente leitura. Um workflow separado cria o draft no GitHub apenas depois de confirmação manual e nova validação em Windows e Ubuntu, a partir de `main` ou `release/<versão>` sincronizada.

Publicação no NuGet e release pública automática continuam desabilitadas. Revisão do draft, proteção do environment e publicação posterior são decisões explícitas do responsável pelo repositório.

### Localização da interface gerada

CLI e material de revisão usam recursos `en-US`/`pt-BR`; o livro HTML também resolve `en`, `pt-PT` ou `pt-BR`. Comandos, JSON e diagnósticos permanecem invariáveis. O texto do livro nunca é traduzido.

### Imagem usada apenas por CSS

Uma imagem JPEG local usada em background deixou de ser recurso genérico desconhecido. `EPUB078` registra a aproximação de apresentação, sem promover a imagem a asset canônico ou carregar URL externa.

### Recuperação de alternativa de imagem

Sem `alt`, o importador procura `aria-label`, `aria-labelledby` local não ambíguo, `title` e `figcaption`. A alternativa autoral entra em `Figure.AlternativeText` e gera `EPUB079`. `alt=""` continua decorativo; ausência total continua `EPUB041` para revisão humana, sem OCR ou texto inventado.

### Triagem de grupos de colunas XHTML

As 24 ocorrências `EPUB058` em quatro candidatos eram `colgroup` válidos. Agora geram `EPUB080`, sem linha/célula de recuperação e sem mudança de hash. `colgroup` com conteúdo visível inválido permanece em `EPUB058` para não perder texto.

Duas qualificações completas foram byte a byte idênticas, com SHA-256 `29EFF347B578C901594692B08F8F592D9CB991A5C3241205DEF2547EB4B00C11`. As 14 publicações elegíveis passaram; a matriz não registrou perda, conteúdo não suportado ou erro do Flow.

## Base do Flow 0.1

O ciclo 0.1 encerrou a ausência de modelo semântico, IDs e âncoras tipados, validação, apresentação opcional, preferências, `.flow.json`, canonicalização, SHA-256, layout independente, HTML, livro de exemplo, CLI, assinatura local e importador EPUB inicial. As limitações de interoperabilidade, confiança, renderização e publicação que restam estão no documento atual de limitações.
