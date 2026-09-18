# Catálogo experimental de corpus EPUB

[English](../epub-corpus.md) | Português (Brasil)

O corpus é um conjunto controlado de fixtures públicas e publicações privadas usadas com autorização para medir regressões. Não é amostra representativa de todo o ecossistema EPUB nem certificado de conformidade.

## Inventário de diretório privado

`epub-inventory` descobre `.epub` recursivamente sem modificar os originais. Calcula SHA-256, deduplica bytes idênticos, identifica família EPUB 2/3, idiomas, tamanhos, recursos, spine e sinais de proteção. O catálogo usa IDs neutros e exclui caminhos, nomes, títulos, autores, publisher IDs, texto e assets.

O diretório-fonte e a saída devem ficar fora do repositório; a saída também não pode ficar dentro da fonte. `--force` só substitui um catálogo Flow reconhecido.

## Qualificação privada

`epub-inventory-qualify` repete o inventário, associa cada arquivo pelo hash verificado e executa inspeção, importação, validação, fidelidade, round-trip JSON, hash canônico, layout mobile/desktop e verificação dos pacotes HTML duas vezes. Falhar um livro não interrompe os demais.

O relatório `flow-epub-private-qualification-0.1` mantém IDs neutros, hashes, fases, contagens e diagnósticos agregados. Entradas com criptografia desconhecida, corrompidas ou inadequadas ficam explicitamente ignoradas. `--legal-use` e `--drm-free` são declarações do utilizador.

## Matriz privada de diferenças

`epub-inventory-matrix` verifica o SHA-256 do relatório e classifica evidência já produzida como aprovado, aprovado com aproximações, conteúdo não suportado, perda, referência quebrada na fonte, erro do Flow ou revisão humana. Não reabre livros e nunca conclui decisões humanas.

Hashes exatos podem identificar bytes privados. Catálogo, qualificação, matriz e revisão devem permanecer fora do Git mesmo sem identidade editorial explícita.

## Revisão visual assistida

`epub-inventory-review` redescobre fontes pelos hashes e gera pacotes neutros para cada candidato qualificado. O índice oferece mobile/desktop, começo/meio/fim e atalhos para TOC, imagens, links de figura, notas, tabelas, ruby, bidi, SVG, MathML e regiões com mais diagnósticos. O checklist começa pendente.

Revisão assistida reduz o trabalho, mas não substitui leitura completa, auditoria de acessibilidade ou aceite editorial.

## Significado de "corpus"

O corpus público contém somente fixtures redistribuíveis do projeto. O corpus privado contém arquivos locais que nunca são copiados para o repositório. Manifestos registram licença e proveniência declaradas, mas não concedem direitos.

## Contrato JSON e descoberta offline

`flow-epub-corpus-0.1` descreve candidatos, expectativas e condições de licença. Caminhos externos são resolvidos somente dentro das raízes permitidas. Descoberta é offline, limitada e não segue links simbólicos ou caminhos inseguros.

Relatórios usam UTF-8 sem BOM, LF, cultura invariável e ordenação determinística. Evidência de ambiente opcional, como tempo e memória, é marcada como não determinística.

## Execução e baselines

O executor processa cada entrada independentemente e preserva diagnósticos por fase. Um baseline revisado pode ser comparado, mas a CLI não cria nem aceita automaticamente sua substituição. Três fixtures pequenas do projeto têm baseline público.

EPUBCheck é integração local opcional. O executável ou JAR deve ser informado explicitamente; nada é baixado. Seu resultado permanece separado da decisão do Flow.

## Gate de publicação grande

O preflight usa spine, XHTML, variedade de recursos e tamanhos de arquivo como evidência de seleção. Esses sinais não equivalem a páginas físicas. O gate exige ID neutro, SHA-256 esperado, raiz absoluta do repositório e declarações de uso legal/sem DRM.

Cada repetição cobre inspeção, importação, fidelidade, validação, round-trip, integridade, layouts, pacotes HTML e auditoria estrutural de ordem, IDs, âncoras, links, imagens, notas e tabelas. O status automático permanece `inconclusive` até a revisão humana separada, ainda que todas as verificações técnicas passem.

Uma publicação refluível privada concluiu esse caminho com 39 capítulos, 3.911 nós, 18 assets e mais de 1,1 milhão de caracteres. A revisão aprovou os pontos selecionados em 390 × 844 e 1600 × 1000. Isso vale para aquele arquivo verificado, não para qualquer EPUB de tamanho semelhante.

## Evidência privada atual

A execução mais recente encontrou 15 entradas. As 14 elegíveis passaram duas qualificações determinísticas sem unidade medida perdida; uma entrada corrompida foi ignorada. A matriz contém 13 aproximações, um caso de acessibilidade para revisão humana e uma origem corrompida, sem conteúdo não suportado ou erro do Flow.

Containers neutros produziram 22.598 transformações informativas `EPUB075`. Fontes incorporadas reconhecidas usam `EPUB074`; page map legado usa `EPUB076`; uma diferença única de caixa em caminho de imagem usa `EPUB077`; imagem local usada só por CSS usa `EPUB078`; alternativa de imagem recuperada usa `EPUB079`. As 24 estruturas `colgroup` válidas em quatro candidatos usam `EPUB080`, sem `EPUB058` falso e sem mudança de hash canônico.

Duas execuções depois da correção de tabelas produziram relatório idêntico com SHA-256 `29EFF347B578C901594692B08F8F592D9CB991A5C3241205DEF2547EB4B00C11`. A matriz correspondente tem SHA-256 `783E4D7877B0234C4C4D3C71A055EA4997E9BB93503C86581E58DA6E9F6F8CEF`.

## Fluxo da CLI

```powershell
flow epub-inventory C:\livros --output C:\flow-local\inventory.json --repository-root C:\src\FlowEngineNet

flow epub-inventory-qualify C:\livros `
  --report C:\flow-local\qualification.json `
  --repository-root C:\src\FlowEngineNet `
  --legal-use --drm-free

$hash = (Get-FileHash C:\flow-local\qualification.json -Algorithm SHA256).Hash
flow epub-inventory-matrix C:\flow-local\qualification.json `
  --qualification-sha256 $hash `
  --output C:\flow-local\matrix.json `
  --repository-root C:\src\FlowEngineNet

flow epub-inventory-review C:\livros `
  --qualification C:\flow-local\qualification.json `
  --qualification-sha256 $hash `
  --output C:\flow-local\review `
  --repository-root C:\src\FlowEngineNet `
  --legal-use --drm-free --ui-language pt-BR
```

## Limite atual

O executor ainda mantém o ZIP limitado em memória; um pacote HTML pode coexistir com documento, HTML standalone e árvores XML. Métricas são aproximadas. Não há certificação EPUB, garantia universal de tamanho, publicação dos livros privados, baseline automático ou conclusão automática da revisão humana.
