# Atualização do site: Flow Engine 1.0

[English](../website-release-1.0.md) | Português (Brasil)

Documento preparado em 28/09/2026 para o projeto separado do site na Vercel. Não executa deploy nem submete a aplicação à Store. Não foi possível inspecionar as páginas públicas `/en` e `/pt-BR` nesta preparação; confira os componentes atuais antes de aplicar as mudanças.

## Estado da versão e regras de publicação

A versão pública passa de `0.2.0-alpha.4` para `1.0.0`; o pacote da Store usa `1.0.0.0`. Estável se refere ao fluxo local de EPUB suportado, não a um formato Flow padronizado nem a um leitor completo. Store ID: `9NMJW9XHMJ0F`.

O estado da distribuição é separado do número da versão:

| Evidência | Estado em inglês | Estado em português | Ação de download |
| --- | --- | --- | --- |
| Candidato preparado, sem confirmação de envio | Preparing for Microsoft Store submission | Preparando o envio à Microsoft Store | Apenas releases já publicadas e conferidas no GitHub |
| Partner Center confirma o envio | Submitted to the Microsoft Store for review | Enviado à Microsoft Store para análise | Manter os downloads existentes; não anunciar disponibilidade na Store |
| Aprovado, mas ainda não publicado | Approved; publication pending | Aprovado; publicação pendente | Ainda não ativar o download pela Store |
| Página abre e permite adquirir o aplicativo | Available in the Microsoft Store | Disponível na Microsoft Store | Ativar o link verificado da Store |

Gerar o MSIX não significa submetê-lo; passar no WACK não significa aprovação. O responsável informou que enviou o pedido de suporte por email e aguarda resposta. Isso não confirma a abertura de um caso de certificação nem a aprovação da aplicação. Os detalhes do diagnóstico ficam no repositório e no suporte, não no destaque da página.

## Alterações nas páginas

- Trocar os destaques atuais de alpha pelo estado real da preparação de `1.0.0`. Preservar o histórico das alphas como histórico.
- Apresentar a aplicação Windows antes da CLI. Mostrar o fluxo existente de EPUB: inspecionar, validar, importar e visualizar o conteúdo suportado.
- Informar Windows desktop x64. Não anunciar ARM64 nativo, Xbox, celular ou interface Linux.
- Separar os downloads da Store, do MSI e da CLI portátil. Cada arquivo disponível deve mostrar versão, arquitetura, estado e SHA256. Não criar links para arquivos `v1.0.0` ainda não publicados.
- Manter links para código-fonte e documentação. A edição Store orienta atualizações pela Store; MSI e portable são canais separados. Checksum não é assinatura de um publicador confiável.
- Informar que as prévias HTML precisam do WebView2 Runtime. O .NET autocontido não inclui esse componente.
- Manter privacidade e suporte acessíveis nos dois idiomas. Usar o contato confirmado pelo responsável: `eym_silva@outlook.com`. Descrever somente práticas de dados verificadas na aplicação; a hospedagem do site exige uma revisão própria.

## Textos prontos em português

Título principal: **Flow Engine para Windows**

Descrição: **Inspecione, valide e importe arquivos EPUB no seu computador. Explore o conteúdo suportado em uma prévia local, com interface em português brasileiro e inglês.**

Identificação da versão: **Versão 1.0.0 — preparando o envio à Microsoft Store.** Trocar apenas o estado quando houver a confirmação correspondente no portal.

Botão principal antes da disponibilidade na Store: **Ver versões publicadas** → `https://github.com/MardSilva/FlowEngineNet/releases`.

Botão secundário: **Consultar a documentação** → `https://github.com/MardSilva/FlowEngineNet`.

Descrições dos recursos:

- **Inspecione EPUBs:** Consulte a estrutura e os metadados da publicação sem converter o livro original.
- **Valide e importe:** Confira o conteúdo suportado e crie um documento Flow separado, com diagnósticos para recursos não suportados.
- **Visualize localmente:** Explore a saída HTML suportada dentro da aplicação Windows.
- **Escolha seu idioma:** Alterne entre português brasileiro e inglês.
- **Use o motor:** A CLI, distribuída separadamente, oferece processamento de documentos e relatórios reproduzíveis.

Limitações: **O Flow Engine não é um leitor completo de ebooks. Importação de PDF, OCR, remoção de DRM e paginação de produção não estão incluídos. O formato de documento Flow e as assinaturas criptográficas continuam experimentais. Preserve seus arquivos originais.**

Depois de verificar a disponibilidade na Store: usar **Versão 1.0.0 — disponível na Microsoft Store** e o botão **Obter na Microsoft Store**. Copiar o link do produto no Partner Center e conferir se corresponde ao Store ID `9NMJW9XHMJ0F` antes do deploy.

Os textos da rota `/en` estão na [versão inglesa deste documento](../website-release-1.0.md).

## Capturas da aplicação

Usar capturas reais da versão final instalada, com arquivos separados em inglês e português brasileiro. As capturas anteriores da alpha servem como referência, não como comprovação da interface 1.0. Não usar mockups gerados como se fossem telas da aplicação.

Arquivos sugeridos; este documento não os gera:

| Padrão de nome | Tela | Texto alternativo em português |
| --- | --- | --- |
| `flow-1.0-home-{en,pt-br}.png` | Início | Tela inicial do Flow Engine com ferramentas locais de EPUB |
| `flow-1.0-epub-{en,pt-br}.png` | Inspeção ou validação | Resultado da inspeção de um EPUB no Flow Engine |
| `flow-1.0-preview-{en,pt-br}.png` | Prévia HTML suportada | Prévia local de um EPUB de demonstração no Flow Engine |
| `flow-1.0-settings-{en,pt-br}.png` | Idioma e configurações | Configurações de idioma e da aplicação Flow Engine |

Usar livros sintéticos de demonstração ou conteúdo autorizado para publicação. Remover caminhos pessoais, nomes de usuário, títulos privados e outras janelas. Manter texto legível, tema consistente e enquadramento semelhante. Não esticar imagens. Informar dimensões para evitar saltos no layout e carregar imagens abaixo da primeira tela sob demanda. Usar os arquivos originais, não os anexos redimensionados do chat.

## Conferência antes do deploy

- As duas rotas mostram a mesma versão e o mesmo estado verificado, com textos e descrições de imagem traduzidos.
- O selo da Store só fica ativo quando for possível adquirir o aplicativo. Não oferecer o MSIX sem assinatura da submissão nem o certificado de teste como download público.
- Não há promessas de PDF, leitor completo, sincronização na nuvem, IA ou plataformas não suportadas.
- Links e hashes correspondem aos arquivos publicados, não apenas ao candidato local.
- Foco por teclado, contraste, layout móvel e legibilidade das capturas foram conferidos.
- Metadados, cards Open Graph e dados estruturados usam o mesmo estado da página visível.
- Links de privacidade e suporte funcionam; afirmações sobre telemetria ou cookies foram conferidas no código do site.
- Revisar um deploy de preview na Vercel antes de promovê-lo para produção. Registrar o commit e a URL do site separadamente da release da aplicação.
