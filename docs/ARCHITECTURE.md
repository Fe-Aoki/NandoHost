# Arquitetura

## Aplicativo

`src/NandoHost.App` é a interface Windows em WPF, com integração de bandeja e processos locais. Os arquivos parciais de `MainWindow` dividem biblioteca, jogos, mods, sugestões, verificações e adaptação da janela. O assembly continua se chamando `NandoHost`, preservando os endereços dos recursos incorporados.

Os temas e recursos estáticos ficam em `App.xaml`, `ThemeArt.cs` e `Assets`. O menu é construído com controles nativos, com animações curtas e sem ciclo contínuo de desenho. As preferências de animações do Windows são respeitadas.

## Núcleo

`src/NandoHost.Core` não depende da interface. Os módulos principais são:

| Área | Arquivos |
| --- | --- |
| Perfis e dados | `Models.cs`, `Storage.cs` |
| Catálogos e instalação | `ReleaseCatalog.cs`, `Downloads.cs`, `Installer.cs`, `BedrockInstaller.cs` |
| Execução e console | `ServerProcess.cs`, `WindowsGameConsole.cs`, `CommandAssist.cs` |
| Pacotes e jogos | `Modpacks.cs`, `TerrariaModule.cs`, `TModLoaderModule.cs` |
| Acesso externo | `PlayitAgent.cs` |

Cada perfil mantém arquivos e mundos próprios. A interface gerencia um servidor ligado por vez. O console do Terraria utiliza o pseudoconsole nativo do Windows; os testes simulam os processos de jogos sem incluir executáveis dos jogos no repositório.

## Testes e saídas

`tests/NandoHost.Tests` é um executável de testes sem framework externo. As simulações reutilizam o próprio executável de testes com nomes e argumentos controlados. Testes online e com jogos reais são optativos.

O build gera `artifacts`; o empacotamento gera `dist`. Ambos são ignorados pelo Git. Downloads de jogos, runtimes privados, credenciais, logs, mundos e backups ficam fora do versionamento.
