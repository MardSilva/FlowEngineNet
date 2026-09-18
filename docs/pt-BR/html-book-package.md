# Pacote de livro HTML

[English](../html-book-package.md) | Português (Brasil)

O pacote HTML transforma um documento validado em um diretório navegável e sem scripts. Ele serve para demonstração, revisão e publicação estática; não é EPUB, Web Publication padronizada nem armazenamento interno definitivo do Reader.

## Política de distribuição do conteúdo

O pacote tem `index.html`, sumário separado, um ou mais arquivos de capítulos, notas locais e assets deduplicados. A divisão segue a estrutura semântica e preserva IDs e links. Referências de nota aparecem no capítulo de leitura; um arquivo de notas pode existir como apoio, sem obrigar o leitor a sair do contexto.

## Links, assets e CSS

Links internos são reescritos entre arquivos sem perder o fragmento. Imagens validadas são gravadas uma vez e usadas por referência. CSS compartilhado contém temas claro, escuro e sépia, escolhas de fonte e escala, regras responsivas e impressão básica. Nenhuma URL externa é carregada durante a geração.

## Leitura e acessibilidade estrutural

A saída usa landmarks, headings, listas de TOC aninhadas, foco visível e navegação anterior/próximo. Tabelas largas recebem rolagem local para não ampliar a página. Capas, figuras e headings longos ficam contidos na superfície de leitura. A interface gerada pode usar `en`, `pt-PT` ou `pt-BR`; o texto autoral nunca é traduzido.

Sem JavaScript, preferências são controles CSS e não persistem entre arquivos. Navegadores sem `:has()` mantêm a leitura e o estilo padrão, mas podem ignorar os seletores opcionais de aparência.

## Manifesto e integridade

O manifesto do pacote registra arquivos e SHA-256 de cada payload. Ele exclui o próprio hash recursivo. Geração e verificação são determinísticas para as mesmas entradas.

## CLI e substituição segura

```powershell
flow render livro.flow.json --html-book livro-book --width 390 --height 844 --ui-language pt-BR
```

Operações de revisão usam diretórios temporários, validação antes da promoção e `--force` apenas para destinos reconhecidos. Saída parcial não vira pacote concluído.

## Limites atuais

Não há pesquisa, progresso persistente, service worker, alternância de idioma em execução, JavaScript opcional, paginação física, matriz completa de navegadores, auditoria WCAG 2.2 ou certificação por leitor de tela. O diretório é descompactado e pode ficar grande.
