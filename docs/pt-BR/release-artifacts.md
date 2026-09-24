# Artefatos locais de release

[English](../release-artifacts.md) | Português (Brasil)

O fluxo cria candidatos locais para validar distribuição sem publicar pacotes.

```powershell
pwsh -NoProfile -File ./eng/build-release-artifacts.ps1 -Configuration Release
pwsh -NoProfile -File ./eng/invoke-release-dry-run.ps1
```

## Dry-run versionado

`eng/release-plan.json` fixa a versão e mantém a publicação de pacote desabilitada. A criação no GitHub fica restrita a rascunho. O dry-run não cria tag, GitHub Release nem upload para NuGet. Produz proveniência local in-toto/SLSA não assinada, sem alegar nível SLSA ou identidade confiável do builder.

## Proposta de versão e release draft

`eng/get-release-proposal.ps1` reconhece branches `feature/<versão>-<descrição>` e `release/<versão>` na sequência suportada de prereleases `alpha`, `beta` e `rc`. Compara a proposta com a versão resolvida por MSBuild e com `eng/release-plan.json`. O resumo do CI informa se há candidato, se o draft está pronto, se os arquivos de versão precisam de atualização ou se a branch não propõe release. Esse job tem somente permissão de leitura.

O workflow manual `.github/workflows/release.yml` exige a versão exata do plano, uma origem `main` ou `release/<versão>` e a escolha `CREATE_DRAFT_RELEASE`. Windows e Ubuntu repetem formatação, documentação, build, testes e smoke instalado. O job final usa o environment `draft-release` e recebe escrita no repositório somente para criar o rascunho. Ele coloca uma introdução editorial bilíngue antes das notas automáticas categorizadas, inclui instruções de instalação local e verificação e anexa os artefatos validados. Uma prerelease não recebe a posição `Latest`. O workflow não publica no NuGet nem torna a GitHub Release pública. Administradores podem configurar revisores obrigatórios nesse environment.

`.github/release.yml` define as categorias do changelog automático. `eng/new-release-introduction.ps1` resolve a versão exata do plano, recusa divergências e, quando grava um arquivo, usa UTF-8 sem BOM e LF. `eng/test-release-introduction.ps1` verifica bytes determinísticos, links versionados, instruções de instalação, codificação e recusa de versão divergente. A introdução descreve a versão experimental completa; a seção categorizada abaixo dela apresenta somente as mudanças desde a release anterior.

## Reprodutibilidade

O build canônico gera dois pacotes no mesmo ambiente Ubuntu, normaliza metadados ZIP enquanto o pacote não é assinado e exige bytes idênticos. O pacote aceito é instalado e exercitado em Ubuntu e Windows. Isso prova repetibilidade no builder canônico e portabilidade desse candidato, não igualdade entre builds independentes em sistemas diferentes.

## SBOM e validação

O SBOM CycloneDX 1.5 cobre componentes enviados no pacote e dependências de runtime. Não cobre SDK, host, Actions, dependências só de teste ou bibliotecas do sistema operacional. `SHA256SUMS`, manifesto e validações verificam os arquivos produzidos.

`Flow.Cli` usa `Spectre.Console` 0.57.2 na ajuda rica, menus, assistentes, progresso e painéis de resultado. Nenhum projeto de domínio depende dele. `Spectre.Console` e seu componente de runtime `Spectre.Console.Ansi` usam a licença MIT; o script de release exige uma ocorrência de cada componente no SBOM e registra a licença. O modo Plain e a saída redirecionada não dependem de ANSI, movimento do cursor ou caracteres Unicode de moldura.

O smoke do pacote instalado verifica ajuda Plain geral e específica, catálogo `pt-BR`, saída redirecionada sem ANSI, resolução das dependências do Spectre.Console, recusa segura do menu sem terminal interativo e comandos representativos de documentos. O CI não simula navegação real pelo teclado; os testes usam o console abstrato para cobrir o menu de forma determinística.

Na auditoria final da experiência da CLI, o commit-base da branch (`1dc44e9`) e o estado concluído foram empacotados no mesmo host Windows, com o SDK fixado, configuração Release e `ContinuousIntegrationBuild=true`. O `.nupkg` passou de 870.714 para 1.485.771 bytes: aumento de 615.057 bytes (70,64%). A maior parte vem da implementação opcional de apresentação e das assemblies de runtime do Spectre.Console. É uma medição local de desenvolvimento, não um orçamento permanente de tamanho nem uma alegação de reprodutibilidade entre sistemas.

## Limite atual

Não há assinatura, feed público, instalador, build self-contained, macOS no CI, política de upgrade/rollback ou garantia de builder independente. Um draft criado pelo workflow ainda exige revisão humana antes de ser publicado e não é uma release suportada.
