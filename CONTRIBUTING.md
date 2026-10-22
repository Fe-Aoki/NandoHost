# Desenvolvimento e relatos

O repositório do NandoHost é público e o projeto continua em desenvolvimento. Use as issues para registrar sugestões e problemas.

## Relatar um problema

Informe a versão do NandoHost, a versão do Windows, o jogo e a edição, a versão do servidor e os passos para reproduzir. Descreva o resultado esperado e o observado. Se usar mods, informe quais e suas versões.

Antes de anexar imagens ou logs, remova nomes de jogadores, endereços pessoais, caminhos privados, senhas, tokens e links de autenticação. Não envie mundos, backups ou arquivos de jogos por padrão. Veja [SECURITY.md](SECURITY.md) para dados sensíveis.

## Alterar o código

1. Crie uma branch com um nome descritivo.
2. Faça uma mudança com escopo claro e preserve a identidade de cada jogo.
3. Execute `scripts/Build.ps1` e os testes adequados à alteração. `scripts/Test.ps1` executa a suíte padrão do núcleo.
4. Para mudanças visuais, confira uma janela compacta e uma maximizada; os modos de prévia estão em [TESTING.md](docs/TESTING.md).
5. Descreva o problema, o comportamento final e a validação no pull request.

Não versione `Data`, credenciais, jogos baixados, mods, runtimes, `artifacts` ou `dist`. Publicação, serviços pagos e mudança da visibilidade do repositório dependem de autorização.
