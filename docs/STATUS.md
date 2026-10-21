# Estado do projeto

Versão atual: **0.9.1**, com barras integradas ao tema e idioma na interface da biblioteca e dos jogos.

Base inicial deste repositório: **NandoHost 0.8.9**. A organização em `src`, `tests`, `docs` e `scripts` não altera o formato dos perfis nem o comportamento dos jogos.

## Implementado

- Português e inglês com troca imediata e preferência salva.

- Biblioteca com busca, capas e atalhos dos perfis existentes.
- Temas próprios de Minecraft, Terraria e Calamity.
- Janela adaptável, maximização e restauração.
- Minecraft Java e Bedrock, incluindo mesmo mundo com Geyser/Floodgate.
- Terraria dedicado e tModLoader com importação de `.tmod`.
- Instalação, console, diagnósticos, métricas e backups.
- Protótipo do agente playit para acesso externo.

## Limitações atuais

- Projeto Windows e dependente do .NET Desktop Runtime 10.
- Vanilla/Fabric têm suporte no fluxo de pacotes Minecraft; Forge, NeoForge e Quilt ainda são bloqueados quando não suportados.
- A importação CurseForge exige um pacote de servidor compatível; não representa download automático irrestrito de todo modpack do catálogo.
- Calamity exige os arquivos de Calamity Mod e Calamity Mod Music e suas dependências compatíveis. O programa não importa a assinatura da Steam Workshop automaticamente.
- A compatibilidade do servidor e dos clientes depende das versões e dos mods selecionados.
- O protótipo de túnel depende de configuração e disponibilidade do provedor. Não há assinatura paga contratada pelo aplicativo.
- O histórico de validação inclui Terraria e tModLoader com ExampleMod reais. Uma sessão completa com Calamity ainda precisa ser validada.

## Antes de uma versão pública

- Validar instalação e atualização em um Windows limpo.
- Ampliar os testes com jogos reais, incluindo Calamity e pacotes maiores.
- Revisar consumo de recursos e acessibilidade em monitores com diferentes escalas.
- Revisar segurança dos downloads, mods e ciclo de vida dos processos.
- Revisar procedência de todos os recursos e definir a licença do projeto.
- Definir distribuição, assinatura do executável e canal de relato de problemas.

Amigos, chat social e contas online permanecem fora do escopo atual. Não há mudança automática da visibilidade do repositório: torná-lo público depende de uma decisão explícita do responsável pelo projeto.
