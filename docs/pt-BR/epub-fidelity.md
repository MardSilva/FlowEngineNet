# Relatório de fidelidade EPUB

[English](../epub-fidelity.md) | Português (Brasil)

`EpubFidelityReport` é um sidecar determinístico e não canônico. Ele compara evidência tipada da fonte com o documento importado sem entrar no `.flow.json`, hash, assinatura, layout ou HTML.

## Estado e impacto

Cada resultado é `preserved`, `transformed`, `approximated`, `unsupported` ou `lost`, acompanhado de impacto informativo, menor, moderado ou maior. Uma transformação mantém a unidade semântica em outra forma; aproximação preserva conteúdo com alguma distinção perdida; não suportado indica representação ausente; perdido significa que uma unidade medida não chegou ao destino.

`EPUB075` registra containers neutros achatados como transformação informativa. `EPUB079` é aproximação menor quando outro texto XHTML explícito fornece a alternativa de imagem. `EPUB041` é moderado porque a imagem existe, mas a publicação não forneceu alternativa textual. `EPUB080` registra `colgroup`/`col` válido como aproximação de tabela, sem inventar célula de destino. `EPUB058` continua reservado à recuperação estrutural malformada.

## Medições

O relatório conta spine, capítulos, headings, parágrafos, links, imagens, notas, células, texto e construções não representáveis. A porcentagem usa unidades heterogêneas e serve para regressão, não como nota editorial ou visual. Sem denominador confiável, o valor é `null`.

Achados diagnósticos podem se sobrepor a medições. Localização pode ficar limitada ao recurso quando a fonte não oferece fragmento estável.

## CLI

```powershell
flow import livro.epub --output livro.flow.json --fidelity-report fidelity.json
```

O JSON usa UTF-8 sem BOM, LF, ordem determinística e escrita atômica. Uma importação parcial ainda pode produzir evidência diagnóstica quando o destino do relatório é válido.

## Limites atuais

O relatório não é validador EPUB, diff visual, auditoria de acessibilidade, EPUBCheck ou comparação com a intenção do editor. Ele mede apenas o perfil tipado conhecido pelo importador.
