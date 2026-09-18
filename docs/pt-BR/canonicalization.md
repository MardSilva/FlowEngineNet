# Canonicalização e hash do documento

[English](../canonicalization.md) | Português (Brasil)

## Estado

`flow-c14n-0.2` define a projeção canônica atual. O leitor preserva compatibilidade com 0.1 por meio de implementação versionada; o perfil não muda silenciosamente.

## Regras dos bytes

A saída usa UTF-8 sem BOM, LF, cultura invariável, ordem fixa de propriedades e coleções determinísticas. O algoritmo escreve uma projeção Flow própria; não é RFC 8785 JCS e não normaliza Unicode.

## Campos canônicos

Participam identidade, metadados canônicos, árvore semântica, conteúdo inline, destinos semânticos, referências de assets e bytes ou hashes dos assets conforme o perfil. IDs e ordem de leitura fazem parte dessa projeção.

Não participam apresentação, tipografia, preferências, tema, viewport, layout, renderer, paginação, source map, relatórios EPUB, métricas nem evidência operacional.

## Perfil de hash

`DocumentHash` usa SHA-256 sobre os bytes canônicos. Alterar conteúdo, estrutura, identidade ou asset altera o hash. Alterar fonte, tema ou viewport não altera. O hash prova igualdade de bytes no perfil; não prova autoria, legalidade, confiança ou adequação arquivística.

## Relação com JSON

`.flow.json` é uma representação legível de intercâmbio e desenvolvimento. Seus bytes não são o hash do documento: o serviço primeiro produz a projeção canônica versionada. Round-trip deve preservar o documento e seu hash.
