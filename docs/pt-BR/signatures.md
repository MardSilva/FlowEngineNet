# Assinaturas experimentais de documento 0.1

[English](../signatures.md) | Português (Brasil)

## Bytes assinados

Somente os bytes produzidos pelo canonicalizador versionado são assinados. JSON legível, apresentação, preferências, viewport, layout e renderer não entram na assinatura.

## Perfil do algoritmo

A prova de conceito usa APIs padrão do .NET e RSA-PSS com SHA-256. O identificador do algoritmo é explícito; algoritmos desconhecidos não são aceitos. Não existe algoritmo criptográfico próprio do Flow.

## Valor e verificação

`DocumentSignature` transporta algoritmo e bytes da assinatura. A verificação retorna resultado tipado para assinatura válida, documento alterado, chave incorreta, algoritmo desconhecido ou entrada inválida. Comparações e erros não expõem material privado.

## Exemplo local

O chamador cria ou carrega sua própria chave RSA, canonicaliza o documento, assina e verifica localmente. A implementação não cria certificados, PKI, identidade, carimbo de tempo, revogação, armazenamento de chaves ou cadeia de confiança. Uma assinatura válida prova apenas que a chave fornecida assinou aqueles bytes canônicos.
