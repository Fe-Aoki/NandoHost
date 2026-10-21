# NandoHost

Gerenciador nativo para hospedar servidores de jogos no seu computador Windows. Versão atual: **0.8.9**, em desenvolvimento.

![Biblioteca do NandoHost](docs/images/biblioteca.png)

## O que já existe

- Biblioteca de jogos com busca, atalhos de servidores e interação com o mouse.
- Minecraft Java, Bedrock e Java + Bedrock com Geyser/Floodgate.
- Minecraft Vanilla/Fabric e importação de pacotes compatíveis do Modrinth e CurseForge.
- Terraria para PC e perfis tModLoader, incluindo Calamity e mods personalizados.
- Console com sugestões de comandos, métricas do processo e backups locais.
- Protótipo de gerenciamento do agente playit para acesso externo.

Os mundos e os dados dos servidores ficam no computador do usuário. O painel não é uma hospedagem na nuvem nem uma plataforma social. Os downloads dos servidores dependem das fontes oficiais e da conexão com a internet. Forge, NeoForge, Quilt e alguns formatos de pacotes ainda têm limitações; veja [o estado do projeto](docs/STATUS.md).

## Abrir no desenvolvimento

Requisitos: Windows, PowerShell e SDK .NET 10 estável. O SDK foi definido em `global.json`, com atualização permitida dentro da versão principal 10. Para executar uma distribuição compilada, instale o .NET Desktop Runtime 10.

```powershell
dotnet restore NandoHost.slnx --configfile NuGet.Config -m:1
dotnet run --project src/NandoHost.App
```

O aplicativo cria `Data` ao lado do executável. Não coloque arquivos pessoais dentro do código-fonte e não versione essa pasta. O Minecraft Java exige uma versão de Java compatível com o servidor escolhido.

## Compilar e testar

```powershell
.\scripts\Build.ps1
.\scripts\Test.ps1
```

O aplicativo publicado fica em `artifacts/app`. Os testes padrão usam dados temporários e processos simulados; não baixam servidores de jogos. Testes de integração com jogos reais são opcionais e estão descritos em [TESTING.md](docs/TESTING.md).

Para gerar um ZIP limpo de distribuição:

```powershell
.\scripts\Package.ps1
```

O pacote fica em `dist`. A montagem usa uma pasta temporária nova e copia somente executáveis do aplicativo, recursos necessários e avisos de terceiros. Não empacota `Data`, mundos ou credenciais.

## Organização

| Pasta | Conteúdo |
| --- | --- |
| `src/NandoHost.App` | Interface WPF, temas, biblioteca e recursos visuais |
| `src/NandoHost.Core` | Instalação, processos, rede, mods, armazenamento e backups |
| `tests/NandoHost.Tests` | Testes executáveis do núcleo e processos simulados |
| `scripts` | Compilação, testes e criação de pacote |
| `docs` | Arquitetura, estado atual, verificações e imagens |
| `licenses` | Licenças e procedência de fontes de terceiros |
| `artifacts`, `dist` | Saídas locais ignoradas pelo Git |

## Documentação

- [Arquitetura](docs/ARCHITECTURE.md)
- [Estado atual e próximos passos](docs/STATUS.md)
- [Testes e verificação](docs/TESTING.md)
- [Cuidados com dados e credenciais](SECURITY.md)
- [Recursos e licenças de terceiros](THIRD_PARTY_NOTICES.md)

O repositório deve permanecer **privado** nesta fase. A licença de distribuição do código do projeto ainda não foi definida. As licenças dos componentes de terceiros permanecem em vigor. Não há workflows de publicação ou serviços pagos configurados neste repositório.
