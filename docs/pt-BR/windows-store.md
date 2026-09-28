# Pacote da Microsoft Store

[English](../windows-store.md) | Português (Brasil)

A primeira versão estável está sendo preparada como `1.0.0`, com MSIX `1.0.0.0`. Essa decisão de produto cobre o fluxo local de EPUB já implementado; não significa aprovação pela Store nem suporte a importação de PDF, OCR ou um aplicativo completo de leitura. A versão pública fica em `Directory.Build.props`. Aumente a versão do MSIX em cada atualização, mantenha o primeiro componente diferente de zero e o quarto igual a zero.

A identidade reservada é `ESSoftwares.FlowEngine`, com publisher `CN=21D9EB03-6223-4C3C-91C6-B132CCE87A14`, nome de editor `ES Softwares`, família `ESSoftwares.FlowEngine_nxa5g9xs5gbp2` e Store ID `9NMJW9XHMJ0F`. São identificadores públicos, não credenciais de assinatura.

## Compilação

Use Windows, PowerShell 7, o SDK .NET do repositório e o MakeAppx x64 do Windows SDK. O restore de Flow.Windows também fornece ferramentas do SDK pelo NuGet. Em uma cópia limpa do repositório:

```powershell
./eng/build-windows-combined-payload.ps1
./eng/test-windows-combined-payload.ps1 -SkipApplicationLaunch
./eng/build-windows-store.ps1 -CombinedPayloadDirectory artifacts/windows-combined-payload
```

O último comando reaproveita o payload combinado da interface e da CLI sem recompilar. Confere revisão, estado do código, inventário completo e hashes SHA-256. Não substitui uma pasta de saída existente. Use `-OutputDirectory artifacts/windows-store-next` em outra tentativa. `-MakeAppxPath` permite indicar uma ferramenta do SDK instalado. `-AllowDevelopmentPayload` permite ensaios locais com alterações não commitadas, identifica a saída como exclusiva para desenvolvimento e não deve ser usado para uma submissão.

A saída inclui um MSIX sem assinatura, `SHA256SUMS`, `store-package.json` e arquivos de preparação. O MakeAppx valida o pacote; isso não equivale a testar a aplicação instalada nem à certificação da Store. O workflow manual `store-package.yml` executa essas etapas sem publicar nem adicionar checks a cada PR. Ele gera um payload combinado novo; ainda não reaproveita artefatos de outras execuções da CI.

## Comportamento da distribuição

O MSIX destina-se ao Windows desktop x64, declara inglês e português brasileiro e inclui a interface e a CLI autocontidas. Cria uma entrada para a interface, sem alias de execução da CLI ou mudanças no PATH, evitando conflitos com um MSI existente. A CLI fica dentro do pacote, mas não é anunciada como instalação de linha de comando pela Store.

A identidade reservada seleciona orientações de atualização pela Store no lugar da consulta ao GitHub na interface. MSI e portable mantêm seu comportamento. O WebView2 usa uma pasta gravável de dados locais, não o diretório de instalação do pacote. O WebView2 Runtime continua sendo um pré-requisito: .NET e Windows App SDK autocontidos não o incluem.

A capacidade `runFullTrust` permite que esta aplicação desktop processe arquivos locais escolhidos pelo usuário. Explique isso nas notas de certificação. O pacote não instala serviço, driver ou tarefa de início automático.

## Antes da submissão

- Teste um pacote de teste assinado ou um registro de desenvolvimento em uma conta/VM descartável. Não instale certificados de teste nos computadores dos usuários.
- Exercite abertura, configurações, Sobre, escolha de arquivos, inspeção/importação/validação de EPUB e prévias WebView2 nos dois idiomas, incluindo DPI e navegação por teclado.
- Teste atualização e remoção; documente a virtualização de dados do MSIX e o destino das preferências/cache. Não presuma que a preservação de dados do MSI se aplica ao MSIX. Preserve os livros originais e as exportações do usuário.
- Confira a coexistência com MSI e teste sem SDK .NET instalado. Verifique o comportamento sem WebView2 Runtime.
- Execute o Windows App Certification Kit e revise capacidades, licenças de dependências, política de privacidade e capturas com base no pacote real.
- Envie apenas um candidato limpo e testado. Mantenha a publicação manual até concluir a avaliação da Store e a conferência final.

A Microsoft assina os MSIX distribuídos pela Store após a certificação. O arquivo sem assinatura não é um instalador de dois cliques para o público. A compilação não compra, cria ou instala certificados. Consulte os [requisitos de pacotes](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements).

O pacote de teste anterior, `0.2.4.0`, apresentou WARNING de DPI no WACK 26100.7705 e 28000.2705. O analisador recebeu ACCESS DENIED ao abrir o executável instalado para leitura/execução, enquanto uma cópia idêntica passou. Não foram alterados o manifesto DPI nem as permissões de WindowsApps. O FAIL opcional de Blocked executables é separado. O responsável informou que enviou o pedido ao suporte; a resposta está pendente. Preserve esses resultados históricos e teste o novo candidato separadamente. O resultado local do WACK não é uma decisão de certificação da Store.
