# Dados e credenciais

Não envie para o Git arquivos da pasta `Data`, mundos, backups, credenciais do playit, senhas, tokens, configurações de servidores em uso ou pacotes de terceiros baixados. O `.gitignore` exclui esses caminhos; confira os arquivos preparados com `git diff --cached --name-only` antes de cada commit.

Os exemplos de senhas, URLs de autenticação e saídas do agente no projeto de testes são dados fictícios usados para verificar bloqueios e ocultação de segredos.

O núcleo contém validações de caminhos e arquivos compactados, restrição de origens de download, verificação de hashes e proteção de arquivos locais no Windows. Isso não substitui a revisão antes de distribuir o programa. Mods e servidores executam código local: importe arquivos apenas de fontes confiáveis.

O acesso externo usa um agente separado. Mantenha o painel e o console locais; não exponha interfaces de administração na internet. O programa não aceita a EULA do Minecraft por conta própria.

Para vulnerabilidades, use a opção privada "Report a vulnerability" na guia Security, se estiver disponível. Caso contrário, combine um canal privado com o mantenedor antes de enviar detalhes sensíveis. Não publique credenciais reais, dados de jogadores ou instruções de exploração em issues públicas.
