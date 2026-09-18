# Política de tradução da documentação

[English](../translation-policy.md) | Português (Brasil)

O inglês (`en-US`) é o idioma canônico da documentação pública, do código-fonte, das APIs, dos comandos, das opções, dos códigos diagnósticos, dos campos serializados, dos identificadores de formato e dos perfis criptográficos do Flow. As edições em português brasileiro (`pt-BR`) existem para facilitar a leitura; elas não criam um contrato diferente.

Cada documento Markdown canônico tem uma versão declarada em português. O arquivo `docs/translation-manifest.json` registra o caminho da fonte, o caminho da tradução e o SHA-256 do texto em inglês revisado, normalizado como UTF-8 sem BOM e LF. Essa normalização mantém a verificação estável entre checkouts Windows e Linux. Quando a fonte muda, a verificação falha até que alguém revise o texto em português e atualize o hash. Trocar apenas o hash, sem conferir a tradução, contraria esta política.

As traduções preservam blocos de código e termos invariáveis. A prosa pode seguir a ordem natural do português brasileiro, mas não pode ampliar garantias de compatibilidade, conformidade, segurança, acessibilidade, desempenho ou publicação. Se houver divergência, o documento em inglês prevalece e a diferença deve ser corrigida como defeito de documentação.

Execute na raiz do repositório a mesma verificação usada pelo CI:

```powershell
pwsh -NoProfile -File ./eng/test-documentation.ps1
```

A verificação cobre o manifesto, os hashes das fontes, a existência das traduções, alvos duplicados, caminhos relativos seguros e links Markdown locais. Qualidade linguística e equivalência semântica ainda dependem de revisão humana.
