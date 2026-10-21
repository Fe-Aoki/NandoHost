using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using static NandoHost.Core.Localizer;

namespace NandoHost.Core;

public static partial class SafePaths
{
    [GeneratedRegex("^[a-zA-Z0-9._+\\-]+$")]
    private static partial Regex TokenPattern();
    public static string Token(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100 || !TokenPattern().IsMatch(value) || value is "." or "..")
            throw new InvalidDataException(Tr("Versão ou identificador inválido."));
        return value;
    }

    public static string Inside(string root, string relative)
    {
        root = Path.GetFullPath(root);
        var normalized = relative.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || normalized.StartsWith('/') ||
            normalized.Contains(':') || normalized.Split('/').Any(p => p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.')))
            throw new InvalidDataException(Tr("O pacote contém um caminho inseguro: ") + relative);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Tr("O arquivo sairia da pasta do servidor."));
        var current = root;
        RejectLink(current);
        foreach (var part in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var stem = part.Split('.')[0].ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" || Regex.IsMatch(stem, "^(COM|LPT)[0-9]$"))
                throw new InvalidDataException(Tr("Nome de arquivo reservado pelo Windows."));
            current = Path.Combine(current, part);
            RejectLink(current);
        }
        return full;
    }

    public static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(Tr("Links de diretório não são permitidos nas operações de instalação e backup."));
    }
}

public sealed class ProfileStore
{
    private readonly string root;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public ProfileStore(string root)
    {
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(this.root);
    }
    public string Root => root;
    public string ServerRoot(ServerProfile profile)
    {
        if (!Guid.TryParseExact(profile.Id, "N", out _)) throw new InvalidDataException(Tr("Identificador inválido."));
        return SafePaths.Inside(root, "servers/" + profile.Id);
    }
    public List<ServerProfile> Load()
    {
        var file = Path.Combine(root, "profiles.json");
        if (!File.Exists(file)) return [];
        return JsonSerializer.Deserialize<List<ServerProfile>>(File.ReadAllText(file)) ?? [];
    }
    public void Save(IEnumerable<ServerProfile> profiles)
    {
        var file = Path.Combine(root, "profiles.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(profiles, JsonOptions));
        File.Move(file + ".tmp", file, true);
    }
    public static void Validate(ServerProfile p)
    {
        if (p.Game is not ("Minecraft" or "Terraria")) throw new InvalidDataException(Tr("Jogo não reconhecido."));
        if (p.IsTerraria)
        {
            if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 64) throw new InvalidDataException(Tr("Use um nome de 1 a 64 caracteres."));
            SafePaths.Token(p.MinecraftVersion);
            if (p.Port is < 1024 or > 65535) throw new InvalidDataException(Tr("Porta: use entre 1024 e 65535."));
            TerrariaModule.Validate(p); return;
        }
        if (p.Edition is not ("Java" or "Bedrock" or "Crossplay")) throw new InvalidDataException(Tr("Selecione Java, Bedrock ou Java + Bedrock."));
        if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 64) throw new InvalidDataException(Tr("Use um nome de 1 a 64 caracteres."));
        SafePaths.Token(p.MinecraftVersion);
        if (p.MemoryMb < 1024 || p.MemoryMb > 65536) throw new InvalidDataException(Tr("Memória: use entre 1024 e 65536 MB."));
        if (p.Port < 1024 || p.Port > 65535) throw new InvalidDataException(Tr("Porta: use entre 1024 e 65535."));
        if (p.ViewDistance < 2 || p.ViewDistance > 32 || p.SimulationDistance < 2 || p.SimulationDistance > 32)
            throw new InvalidDataException(Tr("Distâncias: use valores entre 2 e 32."));
        if (p.MaxPlayers < 1 || p.MaxPlayers > 200) throw new InvalidDataException(Tr("Jogadores: use entre 1 e 200."));
        if (!p.IsBedrock && string.IsNullOrWhiteSpace(p.JavaPath)) throw new InvalidDataException(Tr("Informe o executável do Java."));
        if (!p.IsBedrock && p.JavaPath != "java" && (!Path.IsPathFullyQualified(p.JavaPath) || !File.Exists(p.JavaPath)))
            throw new InvalidDataException(Tr("Selecione um caminho absoluto válido para java.exe."));
        if (p.IsBedrock)
        {
            if (p.Engine != "Bedrock" || p.Port == 65535 || p.ViewDistance < 5 || p.SimulationDistance < 4 || p.SimulationDistance > 12)
                throw new InvalidDataException(Tr("Bedrock: porta entre 1024 e 65534, visão a partir de 5 e simulação entre 4 e 12 chunks."));
            if (p.Installed && (p.LaunchJar != "bedrock_server.exe" || !Regex.IsMatch(p.NativeExecutableSha256, "^[0-9A-Fa-f]{64}$")))
                throw new InvalidDataException(Tr("Instalação Bedrock sem verificação. Instale novamente pelo painel."));
        }
        else if (p.Engine is not ("Vanilla" or "Fabric")) throw new InvalidDataException(Tr("Java executa Vanilla e Fabric."));
        if (p.IsCrossplay && (p.Engine != "Fabric" || p.BedrockPort < 1024 || p.BedrockPort > 65535 || p.BedrockPort == p.Port))
            throw new InvalidDataException(Tr("Java + Bedrock usa Fabric e exige uma porta Bedrock diferente da porta Java."));
    }
    public void WriteProperties(ServerProfile p)
    {
        Validate(p);
        var folder = ServerRoot(p);
        if (p.IsTerraria) { TerrariaModule.WriteConfig(p, folder); return; }
        Directory.CreateDirectory(folder);
        var path = SafePaths.Inside(folder, "server.properties");
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : [];
        var values = new Dictionary<string, string>
        {
            ["server-port"] = p.Port.ToString(), ["server-ip"] = "", ["online-mode"] = "true",
            ["enable-rcon"] = "false", ["enable-query"] = "false", ["enable-jmx-monitoring"] = "false",
            ["white-list"] = p.Whitelist.ToString().ToLowerInvariant(),
            ["enforce-whitelist"] = p.Whitelist.ToString().ToLowerInvariant(),
            ["view-distance"] = p.ViewDistance.ToString(), ["simulation-distance"] = p.SimulationDistance.ToString(),
            ["max-players"] = p.MaxPlayers.ToString()
        };
        if (p.IsBedrock)
        {
            values = new()
            {
                ["server-port"] = p.Port.ToString(), ["server-portv6"] = (p.Port + 1).ToString(),
                ["online-mode"] = "true", ["allow-list"] = p.Whitelist.ToString().ToLowerInvariant(),
                ["view-distance"] = p.ViewDistance.ToString(), ["tick-distance"] = p.SimulationDistance.ToString(),
                ["max-players"] = p.MaxPlayers.ToString(), ["enable-lan-visibility"] = "false"
            };
        }
        else values["enforce-secure-profile"] = (!p.IsCrossplay).ToString().ToLowerInvariant();
        lines.RemoveAll(line => values.Keys.Any(k => Regex.IsMatch(line, "^\\s*" + Regex.Escape(k) + "\\s*[=:]")));
        lines.AddRange(values.Select(kv => kv.Key + "=" + kv.Value));
        File.WriteAllLines(path, lines);
        File.WriteAllText(SafePaths.Inside(folder, "eula.txt"), "eula=" + p.EulaAccepted.ToString().ToLowerInvariant() + Environment.NewLine);
        if (p.IsCrossplay && p.Installed) WriteCrossplayConfig(p, folder);
    }
    public static void WriteCrossplayConfig(ServerProfile p, string root)
    {
        var path = SafePaths.Inside(root, "config/Geyser-Fabric/config.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, $"# Managed by NandoHost; Java authentication stays enabled.\nbedrock:\n  address: 0.0.0.0\n  port: {p.BedrockPort}\n  transport: nethernet\njava:\n  auth-type: floodgate\nconfig-version: 10\n");
            return;
        }
        var text = File.ReadAllText(path);
        var block = Regex.Match(text, @"(?ms)^bedrock:\s*\r?\n(?<body>(?:[ \t]+[^\n]*\n|\r?\n)*)");
        if (!block.Success || !Regex.IsMatch(block.Value, @"(?m)^  port:\s*\d+\s*$"))
            throw new InvalidDataException(Tr("Não foi possível atualizar a porta no config/Geyser-Fabric/config.yml. Confira o arquivo antes de iniciar."));
        var replacement = Regex.Replace(block.Value, @"(?m)^  port:\s*\d+\s*$", "  port: " + p.BedrockPort);
        text = text[..block.Index] + replacement + text[(block.Index + block.Length)..];
        File.WriteAllText(path, text);
    }
    public async Task<string> BackupAsync(ServerProfile p, CancellationToken ct = default)
    {
        var folder = ServerRoot(p);
        if (!Directory.Exists(folder)) throw new InvalidOperationException(Tr("Instale o servidor antes de criar um backup."));
        var target = SafePaths.Inside(root, "backups/" + p.Id);
        Directory.CreateDirectory(target);
        if (p.IsTerraria) TerrariaModule.ProtectFolder(target);
        var file = Path.Combine(target, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff") + ".zip");
        await Task.Run(() =>
        {
            try
            {
                using var zip = ZipFile.Open(file + ".tmp", ZipArchiveMode.Create);
                AddFolder(zip, folder, folder, ct);
            }
            catch { File.Delete(file + ".tmp"); throw; }
            File.Move(file + ".tmp", file);
        }, ct);
        // Retain all successful backups: automatic deletion requires an explicit retention setting.
        return file;
    }
    private static void AddFolder(ZipArchive zip, string root, string folder, CancellationToken ct)
    {
        SafePaths.RejectLink(folder);
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            ct.ThrowIfCancellationRequested();
            SafePaths.RejectLink(file);
            if (Path.GetFileName(file).EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) continue;
            zip.CreateEntryFromFile(file, Path.GetRelativePath(root, file).Replace('\\', '/'), CompressionLevel.Fastest);
        }
        foreach (var sub in Directory.EnumerateDirectories(folder)) AddFolder(zip, root, sub, ct);
    }
}
