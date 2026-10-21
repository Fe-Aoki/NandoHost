# Testes

## Testes padrão

```powershell
.\scripts\Test.ps1
```

O script publica o executável em `artifacts/tests` e o executa. A suíte cria dados em uma pasta temporária exclusiva. Verifica perfis, caminhos, arquivos compactados, downloads simulados, propriedades, senhas, controle de processos, logs, backups, agente e mods. Os processos simulados fazem parte dos testes; o comando padrão não inicia jogos reais nem um túnel público.

## Verificação da interface

Compile o aplicativo com `scripts/Build.ps1`. O modo de prévia cria perfis temporários e renderiza a interface sem usar o `Data` da distribuição:

```powershell
New-Item -ItemType Directory -Path artifacts/previews -Force
$image = Join-Path $PWD 'artifacts/previews/biblioteca.png'
.\artifacts\app\NandoHost.exe --smoke $image 4 --library-check --size 1280 920
```

O modo `--smoke` grava PNG, arquivos de confirmação e um `.error` em caso de erro. Páginas: `0` visão geral, `1` mods, `2` conexão, `3` configurações e `4` biblioteca. `--terraria` e `--calamity` preparam perfis de teste dos respectivos jogos. `--compact` usa a janela mínima; `--maximized` confere a maximização.

Verificações adicionais: `--home-check`, `--library-check`, `--resize-check`, `--game-entry` e `--review`. Os testes de UI anteriores à organização do repositório não substituem uma execução da nova estrutura; veja os resultados registrados em `docs/validation`.

## Integrações opcionais

O executável de testes reconhece `--online`, `--editions-online`, `--terraria-live`, `--tml-live`, `--example-mod` e `--agent-path`. Esses modos podem baixar arquivos, executar servidores reais, consumir rede e ocupar portas locais. Não fazem parte do teste padrão.

Para testar um jogo real, use uma pasta temporária nova como primeiro argumento do executável de testes, nunca uma pasta de servidores em uso. Consulte os blocos correspondentes de `tests/NandoHost.Tests/Program.cs` antes de executar. Não envie os dados gerados para o Git.
