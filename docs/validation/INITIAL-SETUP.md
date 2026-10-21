# Verificação da organização inicial

Data: 21 de outubro de 2026. Base: NandoHost 0.8.9.

| Verificação | Resultado |
| --- | --- |
| Compilação com a nova solução e scripts | Passou, SDK .NET 10.0.402 |
| Suíte padrão do núcleo | 56 verificações, 0 falhas |
| Aplicativo e núcleo comparados à versão 0.8.9 | Arquivos idênticos byte a byte; somente a localização mudou |
| Referência do projeto de testes | Atualizada para `../../src/NandoHost.Core` |
| Interface WPF na estrutura nova | Carregou; fontes incorporadas e acentos conferidos |
| Biblioteca | Busca, seleção, contagens, limites do movimento e retorno do mouse passaram |
| Navegação inicial | Testes de entrada, atalhos e preservação de perfis passaram |
| Redimensionamento | 960×640, 1100×800, 1280×920, 1600×900, maximização e restauração passaram |
| Dados usados na verificação | Perfis temporários; nenhum jogo real ou túnel iniciado |
| Script de empacotamento | ZIP gerado e integridade verificada; sem `Data`, credenciais ou PDB |
| Revisão dos arquivos para Git | Sem padrões de tokens, chaves privadas ou caminhos pessoais detectados |

Os testes do núcleo utilizam processos simulados reais do próprio executável de testes. Os modos de integração online e com jogos reais não foram executados nesta organização. Esta verificação não é uma auditoria completa de segurança.
