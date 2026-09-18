# Renderer HTML standalone 0.1

[English](../html-renderer.md) | Português (Brasil)

## Perfil de saída

O renderer recebe `FlowDocument`, `LayoutDocument` e `UserReadingPreferences` e produz HTML5 standalone com CSS incorporado. Usa elementos semânticos como `article`, `section`, `h1`–`h6`, `p`, `figure`, `figcaption`, `blockquote`, `nav`, listas, `code`, `pre` e tabelas.

IDs estáveis viram atributos `id`; âncoras e sumário continuam navegáveis. Estilos vêm da cascata tipada. Figuras são responsivas e a saída funciona em larguras mobile e desktop.

## Segurança

Texto, atributos e URLs são escapados. O renderer não executa conteúdo EPUB, não carrega recursos externos durante a conversão e não expõe nomes internos de classes C#. SVG só entra como asset passivo sanitizado.

## Determinismo

As mesmas entradas produzem os mesmos bytes. Cultura, ordem de atributos, CSS e assets são estáveis. Viewport e preferências podem mudar a saída renderizada, mas não o documento ou seu hash canônico.
