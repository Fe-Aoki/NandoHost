# Dados e credenciais

Não envie para o Git arquivos da pasta `Data`, mundos, backups, credenciais do playit, senhas, tokens, configurações de servidores em uso ou pacotes de terceiros baixados. O `.gitignore` exclui esses caminhos; confira os arquivos preparados com `git diff --cached --name-only` antes de cada commit.

Os exemplos de senhas, URLs de autenticação e saídas do agente no projeto de testes são dados fictícios usados para verificar bloqueios e ocultação de segredos.

O núcleo contém validações de caminhos e arquivos compactados, restrição de origens de download, verificação de hashes e proteção de arquivos locais no Windows. Isso não substitui a revisão antes de distribuir o programa. Mods e servidores executam código local: importe arquivos apenas de fontes confiáveis.

O acesso externo usa um agente separado. Mantenha o painel e o console locais; não exponha interfaces de administração na internet. O programa não aceita a EULA do Minecraft por conta própria.

Enquanto o projeto estiver privado, registre problemas de segurança no próprio repositório sem anexar credenciais reais ou dados de jogadores. Antes de torná-lo público, defina uma licença e um canal privado para comunicar vulnerabilidades.
