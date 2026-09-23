# Artefatos locais de release

[English](../release-artifacts.md) | Português (Brasil)

O fluxo cria candidatos locais para validar distribuição sem publicar pacotes.

```powershell
pwsh -NoProfile -File ./eng/build-release-artifacts.ps1 -Configuration Release
pwsh -NoProfile -File ./eng/invoke-release-dry-run.ps1
```

## Dry-run versionado

`eng/release-plan.json` fixa versão e política de não publicação. O dry-run não cria tag, GitHub Release nem upload para NuGet. Produz proveniência local in-toto/SLSA não assinada, sem alegar nível SLSA ou identidade confiável do builder.

## Reprodutibilidade

O build canônico gera dois pacotes no mesmo ambiente Ubuntu, normaliza metadados ZIP enquanto o pacote não é assinado e exige bytes idênticos. O pacote aceito é instalado e exercitado em Ubuntu e Windows. Isso prova repetibilidade no builder canônico e portabilidade desse candidato, não igualdade entre builds independentes em sistemas diferentes.

## SBOM e validação

O SBOM CycloneDX 1.5 cobre componentes enviados no pacote e dependências de runtime. Não cobre SDK, host, Actions, dependências só de teste ou bibliotecas do sistema operacional. `SHA256SUMS`, manifesto e validações verificam os arquivos produzidos.

`Flow.Cli` usa `Spectre.Console` 0.57.2 na ajuda rica, menus, assistentes, progresso e painéis de resultado. Nenhum projeto de domínio depende dele. `Spectre.Console` e seu componente de runtime `Spectre.Console.Ansi` usam a licença MIT; o script de release exige uma ocorrência de cada componente no SBOM e registra a licença. O modo Plain e a saída redirecionada não dependem de ANSI, movimento do cursor ou caracteres Unicode de moldura.

O smoke do pacote instalado verifica ajuda Plain geral e específica, catálogo `pt-BR`, saída redirecionada sem ANSI, resolução das dependências do Spectre.Console, recusa segura do menu sem terminal interativo e comandos representativos de documentos. O CI não simula navegação real pelo teclado; os testes usam o console abstrato para cobrir o menu de forma determinística.

Na auditoria final da experiência da CLI, o commit-base da branch (`1dc44e9`) e o estado concluído foram empacotados no mesmo host Windows, com o SDK fixado, configuração Release e `ContinuousIntegrationBuild=true`. O `.nupkg` passou de 870.714 para 1.485.053 bytes: aumento de 614.339 bytes (70,56%). A maior parte vem da implementação opcional de apresentação e das assemblies de runtime do Spectre.Console. É uma medição local de desenvolvimento, não um orçamento permanente de tamanho nem uma alegação de reprodutibilidade entre sistemas.

## Limite atual

Não há assinatura, feed público, instalador, build self-contained, macOS no CI, política de upgrade/rollback ou garantia de builder independente. Os artefatos do CI expiram e não são uma release suportada.
