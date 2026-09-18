# Limitações conhecidas do Flow 0.2 alpha

[English](../known-limitations.md) | Português (Brasil)

O Flow 0.2 alpha é uma implementação de referência para pesquisa. Este arquivo contém apenas restrições atuais. Itens eliminados ou reduzidos por trabalho testado ficam em [limitações resolvidas](resolved-limitations.md). A lista define a fronteira da versão; não promete que cada item será tratado no marco seguinte.

Mudanças de comportamento devem classificar a fronteira como resolvida, reduzida, inalterada ou nova. Um item resolvido sai daqui; um item reduzido permanece apenas com a restrição restante.

## Compatibilidade e distribuição

- APIs, campos `.flow.json`, canonicalização, HTML e diagnósticos continuam experimentais durante `0.x`.
- `.flow.json` é intercâmbio de desenvolvimento, não media type registrado, padrão, formato arquivístico ou container final.
- Não há pacote NuGet, release pública, registry de schemas, garantia de migração ou compatibilidade. O candidato local inclui checksums, SBOM e manifesto apenas para desenvolvimento e CI.
- O projeto fixa .NET SDK 10.0.401. A CLI depende de runtime .NET 10 compatível; não há instalador assinado nem pacote self-contained.
- Reprodutibilidade cobre duas builds normalizadas no builder Ubuntu canônico e a instalação do mesmo candidato em Ubuntu/Windows. Não prova igualdade entre compiladores independentes, macOS ou outros SDKs. Assinatura futura terá de ocorrer depois da normalização.
- O SBOM não cobre SDK, host, Actions, dependências só de teste ou bibliotecas do sistema. A proveniência local não é assinada e não alega nível SLSA, transparência ou identidade confiável.
- O dry-run não cria tag, GitHub Release ou publicação NuGet. Upgrade, rollback e revogação ainda não foram definidos.

## Modelo semântico

- MathML limita-se a Presentation MathML permitido. Não há Content MathML/OpenMath, avaliação, vocabulário arbitrário, semântica vetorial SVG, áudio, vídeo, citações, bibliografia, anotações, controle de alterações, formulários ou scripts.
- Tags de idioma recebem validação estrutural, não consulta completa ao registro IANA. Ruby complexo, múltiplas trilhas, tipografia por idioma, quebra específica por script, escrita vertical e testes completos do algoritmo bidi permanecem fora.
- Tabelas preservam grupos, células, spans positivos, scope e headers. Não modelam colunas, grades calculadas, cabeçalhos inferidos, largura, altura ou coordenadas. `colgroup`/`col` válido gera `EPUB080`; `rowspan="0"` vira um com diagnóstico.
- TOC é explícito; não há geração automática pelos headings.
- A sintaxe dos IDs é validada, mas o motor não consegue provar que um produtor não usou página ou posição visual.
- A validação cobre o modelo atual, não todas as regras editoriais ou de acessibilidade.

## Serialização, integridade e assinaturas

- `flow-json-0.2` e `flow-c14n-0.2` têm apenas esta implementação. Os caminhos 0.1 no mesmo código não são interoperabilidade independente.
- A canonicalização usa projeção e ordem próprias, não RFC 8785 JCS, e não normaliza Unicode.
- Bytes completos de assets ficam em memória e entram na canonicalização, o que não serve para publicações enormes.
- SHA-256 prova igualdade de bytes no perfil, não autoria, proveniência, legalidade, confiança ou preservação arquivística.
- RSA-PSS-SHA256 é prova local destacada. Não há serialização de assinatura, certificados, PKI, descoberta de chaves, revogação, timestamp ou trust store.

## Layout e renderização

- Somente `ReadingMode.Flow` funciona. `Paged` e `Print` lançam `NotSupportedException`.
- `LayoutDocument` carrega restrições e intenções; não mede glifos, caixas, linhas, coordenadas, páginas, viúvas/órfãs, floats ou impressão.
- HTML é o único renderer. Não há UI nativa, Reader, PDF, paginação de produção ou impressão.
- O HTML standalone não passou por matriz completa de navegadores, WCAG 2.2, leitores de tela, localização ou escrita RTL/vertical.
- O livro HTML é pacote lógico `file://`, não exportação EPUB ou Web Publication. Não tem pesquisa, progresso/preferências persistentes, service worker, JavaScript opcional, alternância de idioma em execução ou certificação. O texto autoral não é traduzido. Browsers sem `:has()` podem ignorar os controles opcionais de aparência.
- Fontes são sugestões. OTF, TTF, WOFF e WOFF2 são reconhecidas e geram `EPUB074`, mas os bytes não entram no Flow/HTML. A fonte instalada pode ser usada; caso contrário há fallback. O importador não consulta fontes do sistema para manter determinismo.

## Interoperabilidade EPUB

- O inventário detecta obfuscação de fontes e XML Encryption conhecido, sem descriptografar, identificar contas de fornecedores ou comprovar situação jurídica de DRM.
- Corpus e gates são evidência de regressão, não certificado. EPUBCheck é opcional, externo e separado. Declaração de licença não concede direitos.
- Preflight usa spine, XHTML, recursos e tamanhos; não estima páginas físicas, duração ou complexidade.
- Um livro real verificado concluiu gate e revisão mobile/desktop. Isso não garante outros livros, editores ou tamanhos.
- ZIP limitado continua em memória. Durante uma fase podem coexistir XML, documento, HTML standalone e um pacote. Heap e working set são amostras aproximadas.
- Tabelas muito complexas podem exigir rolagem local. Não houve auditoria completa de browsers, tecnologias assistivas ou writing modes.
- O importador é subconjunto limitado, não checker EPUB 3.3 ou reading system.
- Inspeção reconhece estrutura EPUB 2/3; importação usa Navigation Document ou NCX, mas não valida a especificação inteira.
- Só título, subtítulo, idioma principal, autores e descrição entram nos metadados canônicos. Outras propriedades ficam em sidecar.
- Metadados de acessibilidade são retidos, não certificados. Links OPF, prefixos completos, alternate scripts, collections e rendition permanecem incompletos.
- TOC limita-se a seis níveis. Landmarks e page-list não são representados. Page map EPUB 2 gera `EPUB076` sem importar labels/destinos.
- Fallback pode escolher XHTML; conteúdo alternativo não XHTML não é convertido. Media overlays não são reproduzidos.
- Imagens inline viram figuras de bloco em ordem; imagem-only heading usa alternativa como label. Vários papéis de container/inline mantêm texto, mas não possuem nó canônico próprio.
- Spine repetido recebe IDs de ocorrência, porém link comum não distingue qual repetição pretendia.
- Notas resolvem referências e backlinks, mas backlink não tem nó próprio e link não marcado não é inferido.
- Source map permanece fora do `.flow.json` e do hash; não existe container arquivístico combinado nem política de migração dessa proveniência.
- Fidelidade é perfil estrutural/textual, não nota visual, conformidade ou comparação editorial.
- Imagens JPEG/PNG/GIF estático/WebP e SVG passivo podem virar assets. Sem texto autoral explícito, `EPUB041` exige revisão. Imagem só em background CSS gera `EPUB078` e não vira asset semântico. SVG sanitizado pode mudar de aparência.
- CSS limita-se a seletores e tipografia tipados. Não há combinators, pseudo-seletores, mídia, variáveis, generated content, posicionamento, floats, colunas ou rede.
- Não há Content MathML, `annotation-xml`, image maps, AVIF, GIF animado, fixed layout, algoritmos de coluna de tabela, áudio/vídeo, DRM, bytes de fontes, EPUB CFI, scripts ou metadados completos de acessibilidade.
- Uma capa declarada que não aparece no conteúdo continua apenas como asset + associação de relatório; o importador não inventa capítulo ou figura.
- Texto recuperável de semântica não suportada é achatado com diagnóstico; algumas distinções não cabem no modelo atual.
- O corpus privado atual tem 15 entradas: 14 elegíveis determinísticas sem unidade perdida e uma corrompida. A matriz tem 13 aproximações, uma revisão humana e uma origem quebrada, sem erro do Flow. Isso ainda é cobertura estreita.
- A matriz agrega evidência e não contém localização quando o relatório de qualificação não a guardou. A revisão assistida começa sempre pendente.
- A CLI não exporta EPUB; sucesso de importação não significa conformidade completa.

## CLI e operações

- A CLI cobre documentos, EPUB, corpus, gate e revisão, mas não assinatura/verificação, PDF, paginação, Reader, switches completos de preferência ou batch interativo.
- Textos humanos têm `en-US` e `pt-BR`; detalhes técnicos originais continuam em inglês depois do resumo. JSON e códigos são invariáveis.
- Operações transacionais usam `--force`, `--resume` e lock por destino. Filesystems remotos que ignoram exclusão do .NET não são suportados.
- `execution-clean` remove somente artefatos reconhecidos com UUID exato; não repara sidecars inválidos nem escolhe backups ambíguos.
- O UUID é correlação local, não autenticação. Quem altera o diretório pode alterar seus artefatos.
- CI cobre Windows e Linux, não macOS ou runtimes alternativos.
- O smoke test usa feed local e pacote framework-dependent; não testa publicação, assinatura, upgrade, instalação global ou todos os comandos.

## Fora do escopo

Flow 0.2 alpha não implementa Reader, editor, colaboração, nuvem, DRM, marketplace, contas, PDF, paginação de produção ou fluxo jurídico de assinatura.
