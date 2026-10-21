# Idiomas

Disponíveis a partir da versão **0.9.0**: Português (Brasil) e English.

## Usar

O seletor fica na barra superior, ao lado do nome NandoHost. A troca é imediata e não exige reiniciar. No primeiro uso, o aplicativo usa português quando o idioma do sistema começa com pt; nos demais casos, usa inglês. Depois disso, prevalece a escolha salva.

A preferência fica em Data/ui-settings.json, separada dos perfis. O salvamento usa um arquivo temporário e substituição do arquivo final. Uma preferência ausente, inválida ou ilegível usa o idioma inicial do sistema. Se não for possível salvar uma nova escolha, o aplicativo mantém a anterior e informa o problema.

A troca preserva campos em edição, senhas, edição e versão do jogo, nomes dos perfis, comandos e histórico. É possível trocar o idioma durante uma tarefa; os bloqueios de edição continuam valendo. A sugestão de comandos mostra descrições traduzidas, mas os comandos enviados ao jogo continuam iguais.

## O que mantém o idioma original

- Nomes e conteúdos fornecidos pelo usuário.
- Saídas do console dos jogos e serviços externos.
- Linhas do histórico já registradas antes da troca.
- Informações e erros gerados pelo próprio sistema operacional ou por bibliotecas externas.

Mensagens de progresso já recebidas podem manter o idioma em que foram emitidas; novas mensagens do NandoHost usam a seleção atual. A preferência não altera o idioma dos jogos nem a formatação numérica usada para ler configurações.

## Organização do código

NandoHost.Core/Localizer.cs controla o idioma e carrega o catálogo incorporado Localization/en-US.json. Os textos-fonte em português são as chaves. Tr traduz um texto e TrFormat mantém parâmetros de formatação.

Na interface, TranslateExtension cria bindings para textos fixos do XAML; LocalizationKeys.cs associa identificadores estáveis aos textos-fonte. LocalizedUi.Set atualiza textos dinâmicos com bindings que são notificados ao trocar de idioma. As notificações usam eventos fracos para não reter objetos de bindings descartados.

Ao adicionar um texto:

1. Inclua o texto-fonte e sua tradução no catálogo.
2. Em código, use Tr ou TrFormat. Para um texto dinâmico que deve atualizar imediatamente, use LocalizedUi.Set.
3. Em XAML, adicione um identificador em LocalizationKeys.cs e use loc:Translate.
4. Preserve os parâmetros de formatação numerados.
5. Não traduza tokens de protocolo, comandos, caminhos, nomes de arquivos, identificadores ou dados fornecidos pelo usuário.

Dados de formulários editáveis não devem receber bindings de tradução. Ao usar uma função para texto dinâmico, capture valores estáveis: não capture um recurso descartável que já terá sido fechado na próxima troca de idioma.

## Verificar

scripts/Test.ps1 inclui testes de persistência, preferência inválida, parâmetros, formatação numérica, validações de segurança e preservação de perfis e comandos.

Para a interface, combine --language-check com o modo --smoke. --language en-US e --language pt-BR escolhem o idioma da captura. Os testes usam perfis temporários, sem iniciar jogos ou túneis.
