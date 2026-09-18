# Desempenho, progresso e cancelamento de EPUB

[English](../epub-performance.md) | Português (Brasil)

As observações de execução são evidência não canônica. Elas não entram em `FlowDocument`, `.flow.json`, bytes canônicos, hashes, assinaturas ou HTML determinístico.

## Observações tipadas

O importador informa fase, unidades concluídas, total conhecido, recurso atual, tempo e amostras aproximadas de memória. Heap gerenciado e working set não são medições exatas nem limites universais.

## Cancelamento

APIs aceitam `CancellationToken`. O cancelamento interrompe operações cooperativamente e a CLI usa escrita atômica, sem promover saída parcial a resultado completo. Reinício operacional volta à fonte verificada; não continua de um documento parcial.

## Memória

O ZIP limitado continua em memória para fornecer um `ZipArchive` seguro e pesquisável. Durante uma fase podem coexistir árvores XML, documento semântico, HTML standalone e um pacote HTML. Pacotes mobile e desktop são processados sequencialmente para evitar manter os dois juntos.

## Limites do host e testes

O host deve escolher limites de bytes, recursos, dimensões e tempo compatíveis com seu ambiente. Testes funcionais pequenos rodam sempre; cargas reais e benchmarks são evidência opcional e privada. Um livro grande aprovado não define tamanho máximo suportado.
