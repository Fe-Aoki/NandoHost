using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.RegularExpressions;
using static NandoHost.Core.Localizer;

namespace NandoHost.Core;

public sealed class TerrariaModule(Downloads downloads)
{
    public const string CatalogUrl = "https://terraria.org/api/get/dedicated-servers-names";
    public static readonly string[] Binaries = ["TerrariaServer.exe", "ReLogic.Native.dll"];
    public static string VersionForPackage(string digits) => digits.Length == 4 ? string.Join('.', digits.ToCharArray()) : "pacote-" + digits;
    public async Task<List<ServerVersion>> VersionsAsync(CancellationToken ct = default)
    {
        using var json = await downloads.JsonAsync(CatalogUrl, ct);
        var result = json.RootElement.EnumerateArray().Select(v => v.GetString() ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase).Select(name => (name, match: Regex.Match(name, @"^terraria-server-(?<id>[0-9]{4,8})\.zip$")))
            .Where(v => v.match.Success)
            .Select(v => new ServerVersion(VersionForPackage(v.match.Groups["id"].Value), DownloadUrl: "https://terraria.org/api/download/pc-dedicated-server/" + v.name)).ToList();
        if (result.Count == 0) throw new InvalidDataException(Tr("O catálogo oficial não forneceu um servidor Terraria para PC."));
        return result;
    }
    public async Task InstallAsync(ServerProfile p, string root, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var versions = await VersionsAsync(ct);
        var choice = versions.FirstOrDefault(v => v.Id == p.MinecraftVersion)
            ?? throw new InvalidDataException(Tr("A versão Terraria escolhida não está mais no catálogo oficial. Atualize a lista; não são usados espelhos."));
        var package = Regex.Match(choice.DownloadUrl, @"terraria-server-(?<id>[0-9]+)\.zip$").Groups["id"].Value;
        var archive = SafePaths.Inside(root, ".terraria-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            progress?.Report(Tr("Baixando Terraria dedicado para PC do site oficial…"));
            await downloads.FileAsync(choice.DownloadUrl, archive, maxBytes: 200L * 1024 * 1024, ct: ct);
            progress?.Report(Tr("Validando o pacote e extraindo somente os binários Windows…"));
            await Task.Run(() => ExtractOfficial(archive, root, package, ct), ct);
            p.NativeFileHashes.Clear();
            foreach (var name in Binaries)
            {
                await using var stream = File.OpenRead(SafePaths.Inside(root, name));
                p.NativeFileHashes[name] = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            }
            p.NativeExecutableSha256 = p.NativeFileHashes["TerrariaServer.exe"];
            p.LaunchJar = "TerrariaServer.exe"; p.RequiredJava = 0; p.Installed = true;
            progress?.Report(Tr("Terraria instalado. O primeiro início cria o mundo; não precisa de Java."));
        }
        finally { if (File.Exists(archive)) File.Delete(archive); }
    }
    internal static void ExtractOfficial(string archive, string root, string package, CancellationToken ct = default)
    {
        if (!Regex.IsMatch(package, "^[0-9]{4,8}$")) throw new InvalidDataException(Tr("Pacote Terraria inválido."));
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 10000) throw new InvalidDataException(Tr("Pacote contém arquivos demais."));
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expanded = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var relative = entry.FullName.TrimEnd('/'); if (relative.Length == 0) continue;
            SafePaths.Inside(root, relative);
            if (!paths.Add(relative) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                throw new InvalidDataException(Tr("Entrada duplicada ou link no pacote Terraria."));
            expanded += entry.Length;
            if (entry.Length > 128L * 1024 * 1024 || expanded > 512L * 1024 * 1024)
                throw new InvalidDataException(Tr("Pacote Terraria grande demais."));
        }
        var entries = Binaries.Select(name => (name, entry: zip.GetEntry(package + "/Windows/" + name)
            ?? throw new InvalidDataException(Tr("Binário Windows ausente no pacote: ") + name))).ToList();
        // Preflight all required executables before writing anything; scripts and other platforms are ignored.
        foreach (var (_, entry) in entries)
        {
            using var stream = entry.Open();
            if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z') throw new InvalidDataException(Tr("Binário Windows inválido."));
        }
        Directory.CreateDirectory(root);
        foreach (var (name, entry) in entries)
        {
            var target = SafePaths.Inside(root, name);
            var temporary = SafePaths.Inside(root, ".native-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var input = entry.Open())
                using (var output = File.Create(temporary))
                    Downloads.CopyLimitedAsync(input, output, 128L * 1024 * 1024, ct).GetAwaiter().GetResult();
                File.Move(temporary, target, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    public static void Validate(ServerProfile p)
    {
        if (p.Edition != "PC" || p.Engine is not ("Terraria" or "tModLoader")) throw new InvalidDataException(Tr("Escolha Terraria PC Vanilla ou tModLoader."));
        if (p.MaxPlayers is < 1 or > 255 || p.WorldSize is < 1 or > 3 || p.WorldDifficulty is < 0 or > 3)
            throw new InvalidDataException(Tr("Terraria: 1–255 jogadores; mundo pequeno, médio ou grande; dificuldade válida."));
        ValidateLine(p.WorldName, 64, false); ValidateLine(p.WorldSeed, 128, true); ValidateLine(p.WorldPassword, 64, true);
        if (p.IsTModLoader) { TModLoaderModule.Validate(p); return; }
        if (p.Installed && (p.LaunchJar != "TerrariaServer.exe" || p.NativeFileHashes.Count != 2 ||
            Binaries.Any(name => !p.NativeFileHashes.TryGetValue(name, out var hash) || !Regex.IsMatch(hash, "^[0-9a-fA-F]{64}$"))))
            throw new InvalidDataException(Tr("A instalação Terraria não tem a verificação dos binários. Instale novamente pelo painel."));
    }
    private static void ValidateLine(string value, int max, bool empty)
    {
        if ((!empty && string.IsNullOrWhiteSpace(value)) || value.Length > max || value.Any(c => char.IsControl(c) || c == '='))
            throw new InvalidDataException(Tr("Nome, semente e senha devem estar em uma linha, sem '=' e dentro do limite do campo."));
    }
    public static string ReadPassword(string root)
    {
        var file = SafePaths.Inside(root, "serverconfig.txt");
        if (!File.Exists(file)) return "";
        if (new FileInfo(file).Length > 64 * 1024) throw new InvalidDataException(Tr("Configuração Terraria grande demais."));
        return File.ReadLines(file).FirstOrDefault(l => l.StartsWith("password=", StringComparison.Ordinal))?[9..] ?? "";
    }
    public static void WriteConfig(ServerProfile p, string root)
    {
        Validate(p); Directory.CreateDirectory(root); SafePaths.RejectLink(root);
        ProtectFolder(root);
        var worlds = SafePaths.Inside(root, "worlds"); Directory.CreateDirectory(worlds);
        var values = new Dictionary<string, string>
        {
            ["world"] = SafePaths.Inside(root, "worlds/world.wld"), ["worldpath"] = worlds,
            ["autocreate"] = p.WorldSize.ToString(), ["worldname"] = p.WorldName, ["seed"] = p.WorldSeed,
            ["difficulty"] = p.WorldDifficulty.ToString(), ["maxplayers"] = p.MaxPlayers.ToString(),
            ["port"] = p.Port.ToString(), ["password"] = p.WorldPassword, ["secure"] = "1",
            ["upnp"] = "0", ["priority"] = "3", ["language"] = "en-US", ["worldrollbackstokeep"] = "2",
            ["banlist"] = SafePaths.Inside(root, "banlist.txt")
        };
        var file = SafePaths.Inside(root, "serverconfig.txt");
        var temp = SafePaths.Inside(root, ".config-" + Guid.NewGuid().ToString("N"));
        if (p.IsTModLoader) { values["modpath"] = SafePaths.Inside(root, "mods"); TModLoaderModule.WriteEnabled(p, root); }
        try { File.WriteAllLines(temp, values.Select(kv => kv.Key + "=" + kv.Value)); File.Move(temp, file, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void ProtectFolder(string root)
    {
        Directory.CreateDirectory(root); SafePaths.RejectLink(root);
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(root).SetAccessControl(security);
        }
    }
}
