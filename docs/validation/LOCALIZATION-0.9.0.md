# Validação de idiomas — 0.9.0

Data: 21 de outubro de 2026.

- Build Release sem avisos ou erros na compilação final.
- 60 testes do núcleo, 0 falhas.
- Catálogo de inglês incorporado; parâmetros de formatação verificados.
- Preferência salva e relida; arquivo inválido usa idioma inicial do sistema.
- Troca imediata nos dois sentidos com preservação de campos, senha, versão, comando, cursor e histórico.
- Troca durante tarefa simulada mantém os bloqueios.
- Perfis não foram gravados durante a troca de idioma.
- Biblioteca, Terraria, Calamity, mods, conexão e configurações conferidos visualmente.
- Janela compacta e maximizada conferidas.
- Nenhum jogo real ou túnel iniciado nesta validação.

Os testes de processo do núcleo usam processos simulados do executável de testes. Logs já recebidos, nomes e conteúdos do usuário mantêm o idioma original. O teste não comprova uma sessão completa com Calamity.

## Verificações da interface

### library-en
- PASS language switch persists en-US
- PASS draft fields and password preserved en-US
- PASS command, caret and history preserved en-US
- PASS edition, engine and version preserved en-US
- PASS profiles unchanged en-US
- PASS library action translated en-US
- PASS dynamic action translated en-US
- PASS language switch persists pt-BR
- PASS draft fields and password preserved pt-BR
- PASS command, caret and history preserved pt-BR
- PASS edition, engine and version preserved pt-BR
- PASS profiles unchanged pt-BR
- PASS library action translated pt-BR
- PASS dynamic action translated pt-BR
- PASS language switch persists en-US
- PASS draft fields and password preserved en-US
- PASS command, caret and history preserved en-US
- PASS edition, engine and version preserved en-US
- PASS profiles unchanged en-US
- PASS library action translated en-US
- PASS dynamic action translated en-US
- PASS language switch persists pt-BR
- PASS draft fields and password preserved pt-BR
- PASS command, caret and history preserved pt-BR
- PASS edition, engine and version preserved pt-BR
- PASS profiles unchanged pt-BR
- PASS library action translated pt-BR
- PASS dynamic action translated pt-BR
- PASS switch during task preserves lock and draft
- PASS language changes never start server or tunnel
- PASS suggestion description translated without changing command
- PASS game covers fit minimum window
- PASS cover selection updates game details
- PASS search filters covers and sidebar
- PASS empty search is explained
- PASS cover counts reflect real profiles
- PASS mouse parallax is bounded
- PASS mouse leave resets artwork position
- PASS game entry respects task lock
- PASS library does not start server or tunnel

### library-pt
- PASS game covers fit minimum window
- PASS cover selection updates game details
- PASS search filters covers and sidebar
- PASS empty search is explained
- PASS cover counts reflect real profiles
- PASS mouse parallax is bounded
- PASS mouse leave resets artwork position
- PASS game entry respects task lock
- PASS library does not start server or tunnel

### terraria-en
- PASS language switch persists en-US
- PASS draft fields and password preserved en-US
- PASS command, caret and history preserved en-US
- PASS edition, engine and version preserved en-US
- PASS profiles unchanged en-US
- PASS library action translated en-US
- PASS dynamic action translated en-US
- PASS language switch persists pt-BR
- PASS draft fields and password preserved pt-BR
- PASS command, caret and history preserved pt-BR
- PASS edition, engine and version preserved pt-BR
- PASS profiles unchanged pt-BR
- PASS library action translated pt-BR
- PASS dynamic action translated pt-BR
- PASS language switch persists en-US
- PASS draft fields and password preserved en-US
- PASS command, caret and history preserved en-US
- PASS edition, engine and version preserved en-US
- PASS profiles unchanged en-US
- PASS library action translated en-US
- PASS dynamic action translated en-US
- PASS language switch persists pt-BR
- PASS draft fields and password preserved pt-BR
- PASS command, caret and history preserved pt-BR
- PASS edition, engine and version preserved pt-BR
- PASS profiles unchanged pt-BR
- PASS library action translated pt-BR
- PASS dynamic action translated pt-BR
- PASS switch during task preserves lock and draft
- PASS language changes never start server or tunnel
- PASS suggestion description translated without changing command
- PASS console remains usable at 960;640: 233,6
- PASS actions fit at 960;640
- PASS command reachable by scrolling at 960;640
- PASS settings actions visible at 960;640
- PASS console remains usable at 1100;800: 252,8
- PASS actions fit at 1100;800
- PASS command reachable by scrolling at 1100;800
- PASS settings actions visible at 1100;800
- PASS console remains usable at 1280;920: 264,8
- PASS actions fit at 1280;920
- PASS command reachable by scrolling at 1280;920
- PASS settings actions visible at 1280;920
- PASS console remains usable at 1600;900: 264,8
- PASS actions fit at 1600;900
- PASS command reachable by scrolling at 1600;900
- PASS settings actions visible at 1600;900
- PASS maximized window has restore action
- PASS maximized window stays within monitor work area and taskbar
- PASS resize, minimize and restore never start server or tunnel

### settings-en

### connection-en

### mods-en

### minecraft-pt
- PASS new profile primary action opens setup
- PASS failed property write does not commit the profile or replace the selected item
- PASS settings actions remain visible within window
- PASS settings actions remain visible within window
- PASS edition selector has accessible name
- PASS modpack empty state explains next step
- PASS local addresses match edition and crossplay
- PASS tunnel setup does not promise free access for every transport
- PASS Java diagnostic detects runtime without starting Minecraft
- PASS review does not start game servers or tunnels

### library-en-maximized
- PASS game covers fit minimum window
- PASS cover selection updates game details
- PASS search filters covers and sidebar
- PASS empty search is explained
- PASS cover counts reflect real profiles
- PASS mouse parallax is bounded
- PASS mouse leave resets artwork position
- PASS game entry respects task lock
- PASS library does not start server or tunnel
