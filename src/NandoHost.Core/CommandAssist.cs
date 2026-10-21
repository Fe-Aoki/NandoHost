using static NandoHost.Core.Localizer;
namespace NandoHost.Core;

public sealed record CommandCompletion(string Text, int Caret, string Label, string Description);

// Local suggestions: never request shell completion or execute a proposed command.
// Keep this small catalog independent of network access and server authentication.
public static class CommandAssist
{
    private sealed record Node(string Token, string Description, string Usage = "", bool Players = false, Node[]? Children = null);
    private static Node N(string token, string description, string usage = "", bool players = false, params Node[] children) =>
        new(token, description, usage, players, children);
    private static readonly Node[] Roots =
    [
        N("help", "Mostra a ajuda do servidor"),
        N("list", "Lista os jogadores conectados"),
        N("say", "Envia uma mensagem para todos", "<mensagem>"),
        N("whitelist", "Gerencia quem pode entrar", "<ação>", false,
            N("add", "Autoriza um jogador", "<jogador>", true),
            N("remove", "Remove um jogador da lista", "<jogador>", true),
            N("list", "Mostra os jogadores autorizados"), N("on", "Ativa a lista de permissões"),
            N("off", "Desativa a lista de permissões"), N("reload", "Recarrega a lista")),
        N("op", "Concede administração a um jogador", "<jogador>", true),
        N("deop", "Remove a administração de um jogador", "<jogador>", true),
        N("time", "Ajusta o horário do mundo", "<ação>", false,
            N("set", "Define o horário", "<horário>", false,
                N("day", "Início do dia"), N("noon", "Meio-dia"), N("night", "Início da noite"), N("midnight", "Meia-noite")),
            N("add", "Avança o horário", "<ticks>")),
        N("weather", "Ajusta o clima", "<clima>", false,
            N("clear", "Céu limpo"), N("rain", "Chuva"), N("thunder", "Tempestade")),
        N("difficulty", "Altera a dificuldade", "<nível>", false,
            N("peaceful", "Pacífico"), N("easy", "Fácil"), N("normal", "Normal"), N("hard", "Difícil")),
        N("gamemode", "Altera o modo de jogo", "<modo>", false,
            N("survival", "Sobrevivência", "<jogador>", true), N("creative", "Criativo", "<jogador>", true),
            N("adventure", "Aventura", "<jogador>", true), N("spectator", "Espectador", "<jogador>", true)),
        N("save-all", "Salva o mundo", "", false, N("flush", "Salva e descarrega os dados no disco")),
        N("seed", "Mostra a semente do mundo"),
        N("kick", "Expulsa um jogador", "<jogador>", true),
        N("ban", "Bane um jogador", "<jogador>", true),
        N("pardon", "Remove o banimento", "<jogador>", true),
        N("stop", "Salva e encerra o servidor")
    ];

    private static readonly Node[] BedrockRoots = [
        .. Roots.Where(n => n.Token is not ("whitelist" or "save-all" or "seed" or "ban" or "pardon")),
        N("allowlist", "Gerencia quem pode entrar no Bedrock", "<ação>", false,
            N("add", "Autoriza uma conta Xbox", "<gamertag entre aspas se houver espaços>"),
            N("remove", "Remove uma conta Xbox", "<gamertag>"), N("list", "Lista contas autorizadas"), N("reload", "Recarrega a lista")),
        N("save", "Gerencia a gravação Bedrock", "<ação>", false,
            N("hold", "Suspende a gravação"), N("query", "Consulta o estado da gravação"), N("resume", "Retoma a gravação"))
    ];
    private static readonly Node[] TerrariaRoots = [
        N("help", "Mostra os comandos do Terraria"), N("playing", "Lista jogadores conectados"),
        N("save", "Salva o mundo"), N("exit", "Salva e encerra o Terraria"),
        N("say", "Envia uma mensagem", "<mensagem>"), N("kick", "Expulsa um jogador", "<nome>"),
        N("ban", "Bane um jogador", "<nome>"), N("dawn", "Início do dia"), N("noon", "Meio-dia"),
        N("dusk", "Início da noite"), N("midnight", "Meia-noite"), N("settle", "Acomoda líquidos")
    ];
    public static IReadOnlyList<CommandCompletion> Suggest(string text, int caret, IEnumerable<string>? players = null, string edition = "Java")
    {
        if (text.Length > 512 || text.Any(c => c is '\n' or '\r' or '\0')) return [];
        caret = Math.Clamp(caret, 0, text.Length);
        var left = text[..caret].TrimStart();
        var slash = left.StartsWith('/');
        if (slash) left = left[1..];
        var words = left.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var endsSpace = left.Length > 0 && char.IsWhiteSpace(left[^1]);
        var previous = endsSpace ? words : words.Take(Math.Max(0, words.Length - 1)).ToArray();
        var partial = endsSpace || words.Length == 0 ? "" : words[^1];
        var children = edition == "Terraria" ? TerrariaRoots : edition == "Bedrock" ? BedrockRoots : Roots;
        Node? parent = null;
        var path = new List<string>();
        foreach (var word in previous)
        {
            parent = children.FirstOrDefault(n => n.Token.Equals(word, StringComparison.OrdinalIgnoreCase));
            if (parent == null) return [];
            path.Add(parent.Token);
            children = parent.Children ?? [];
        }
        var candidates = parent?.Players == true
            ? (players ?? []).Where(p => ValidPlayer(p) || edition == "Crossplay" && p.StartsWith('.') && ValidPlayer(p[1..])).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(p => new Node(p, Tr("Nome conhecido pelo painel"))).ToArray()
            : children;
        var end = caret;
        while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
        var suffix = text[end..];
        return candidates.Where(n => n.Token.StartsWith(partial, StringComparison.OrdinalIgnoreCase)).Take(8)
            .Select(n =>
            {
                var label = string.Join(' ', path.Append(n.Token));
                var prefix = (slash ? "/" : "") + label;
                if (n.Usage.Length > 0 || n.Children is { Length: > 0 }) prefix += " ";
                var tail = prefix.EndsWith(' ') ? suffix.TrimStart() : suffix;
                return new CommandCompletion(prefix + tail, prefix.Length, label, Tr(n.Description) + (n.Usage.Length > 0 ? " · " + Tr(n.Usage) : ""));
            }).ToArray();
    }
    private static bool ValidPlayer(string value) => value.Length is >= 1 and <= 16 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}

public sealed class CommandHistory
{
    private readonly List<string> commands = [];
    private int position;
    private string draft = "";
    public int Count => commands.Count;
    public bool IsNavigating => position < commands.Count;
    public void Remember(string command)
    {
        command = command.Trim();
        if (command.Length is 0 or > 512 || command.Any(c => c is '\n' or '\r' or '\0')) return;
        if (commands.Count == 0 || commands[^1] != command) commands.Add(command);
        if (commands.Count > 100) commands.RemoveAt(0);
        Reset();
    }
    public string Previous(string current)
    {
        if (commands.Count == 0) return current;
        if (position == commands.Count) draft = current;
        position = Math.Max(0, position - 1);
        return commands[position];
    }
    public string Next()
    {
        position = Math.Min(commands.Count, position + 1);
        return position == commands.Count ? draft : commands[position];
    }
    public void Reset() { position = commands.Count; draft = ""; }
    public void Clear() { commands.Clear(); Reset(); }
}
