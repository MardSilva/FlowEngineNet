# Resultados da pesquisa

[English](../research-findings.md) | Português (Brasil)

HTML é adequado como primeira saída porque oferece estrutura semântica, links, acessibilidade e adaptação a diferentes telas. CSS deve permanecer no renderer ou virar apresentação tipada; strings CSS, viewport e coordenadas não pertencem ao conteúdo canônico.

EPUB é um contêiner ZIP com package document, manifest, spine, navegação e recursos. A importação segura exige limites de arquivo, XML sem entidades externas, normalização de caminhos, ausência de rede e diagnóstico explícito para tudo que não puder ser representado. O Flow não deve fingir equivalência visual quando só preserva semântica.

Padrões W3C relevantes incluem HTML, CSS, EPUB, WCAG e vocabulários de acessibilidade. O projeto usa esses padrões como referência, mas não alega conformidade completa. `.flow.json` e a canonicalização são experimentais e não registrados.

## Conclusão para 0.1

A separação entre documento, layout e renderização funciona como base testável. O passo seguinte foi ampliar a evidência EPUB real. PDF requer pesquisa própria, pois sua ordem de leitura e semântica muitas vezes precisam ser reconstruídas a partir de uma representação física.
