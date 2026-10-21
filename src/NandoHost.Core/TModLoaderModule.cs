using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static NandoHost.Core.Localizer;

namespace NandoHost.Core;

public record TerrariaMod(string Name, string Version, string LoaderVersion, string Sha256)
{
    public override string ToString() => Name + " · " + Version;
}

public sealed class TModLoaderModule(Downloads downloads)
{
    public const string ReleasesUrl = "https://api.github.com/repos/tModLoader/tModLoader/releases/latest";
    public async Task<List<ServerVersion>> VersionsAsync(CancellationToken ct = default)
    {
        using var json = await downloads.JsonAsync(ReleasesUrl, ct);
        var release = json.RootElement;
        var tag = release.GetProperty("tag_name").GetString()!;
        SafePaths.Token(tag);
        if (release.GetProperty("prerelease").GetBoolean()) throw new InvalidDataException(Tr("O catálogo não retornou uma versão estável."));
        var asset = release.GetProperty("assets").EnumerateArray().First(a => a.GetProperty("name").GetString() == "tModLoader.zip");
        var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() : null;
        var sha256 = digest?.StartsWith("sha256:", StringComparison.Ordinal) == true ? digest[7..] : "";
        if (sha256.Length > 0 && !Regex.IsMatch(sha256, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException(Tr("Hash do lançamento inválido."));
        return [new ServerVersion(tag, DownloadUrl: asset.GetProperty("browser_download_url").GetString()!, DownloadSha256: sha256)];
    }
    public async Task InstallAsync(ServerProfile p, string root, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var choice = (await VersionsAsync(ct)).FirstOrDefault(v => v.Id == p.MinecraftVersion)
            ?? throw new InvalidDataException(Tr("Atualize o catálogo do tModLoader antes de instalar. Perfis instalados mantêm sua versão."));
        var stage = SafePaths.Inside(root, ".tml-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var package = SafePaths.Inside(stage, "package.zip");
        var engine = SafePaths.Inside(stage, "engine"); Directory.CreateDirectory(engine);
        try
        {
            progress?.Report(Tr("Baixando tModLoader estável do repositório oficial…"));
            await downloads.FileAsync(choice.DownloadUrl, package, hash: choice.DownloadSha256.Length > 0 ? choice.DownloadSha256 : null, algorithm: "SHA256", maxBytes: 256L * 1024 * 1024, ct: ct);
            await Task.Run(() => Extract(package, engine, true, ct), ct);
            using var config = JsonDocument.Parse(File.ReadAllText(SafePaths.Inside(engine, "tModLoader.runtimeconfig.json")));
            var framework = config.RootElement.GetProperty("runtimeOptions").GetProperty("framework");
            var required = Version.Parse(framework.GetProperty("version").GetString()!);
            if (framework.GetProperty("name").GetString() != "Microsoft.NETCore.App" || required.Major is < 8 or > 10)
                throw new InvalidDataException(Tr("Este runtime do tModLoader ainda não é suportado pelo instalador."));
            progress?.Report(Tr("Instalando runtime .NET privado para este servidor…"));
            using var metadata = await downloads.JsonAsync($"https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/{required.Major}.{required.Minor}/releases.json", ct);
            var runtime = metadata.RootElement.GetProperty("releases").EnumerateArray()
                .Where(r => r.TryGetProperty("runtime", out _)).Select(r => r.GetProperty("runtime"))
                .First(r => Version.TryParse(r.GetProperty("version").GetString(), out var version) && version >= required);
            var runtimeFile = runtime.GetProperty("files").EnumerateArray().First(f => f.GetProperty("rid").GetString() == "win-x64" && f.GetProperty("name").GetString()!.EndsWith(".zip"));
            await downloads.FileAsync(runtimeFile.GetProperty("url").GetString()!, package, runtimeFile.GetProperty("hash").GetString(), maxBytes: 128L * 1024 * 1024, ct: ct);
            var runtimeRoot = SafePaths.Inside(engine, "dotnet"); Directory.CreateDirectory(runtimeRoot);
            await Task.Run(() => Extract(package, runtimeRoot, false, ct), ct);
            if (!File.Exists(SafePaths.Inside(runtimeRoot, "dotnet.exe"))) throw new InvalidDataException(Tr("Runtime Windows incompleto."));
            var target = SafePaths.Inside(root, "engine");
            if (Directory.Exists(target)) throw new InvalidOperationException(Tr("Crie outro perfil para instalar outra versão do tModLoader."));
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(engine, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                using var input = File.OpenRead(file);
                hashes["engine/" + Path.GetRelativePath(engine, file).Replace('\\', '/')] = Convert.ToHexString(await SHA256.HashDataAsync(input, ct));
            }
            Directory.Move(engine, target);
            p.NativeFileHashes = hashes; p.LaunchJar = "engine/tModLoader.dll";
            p.LoaderVersion = choice.Id; p.RequiredJava = 0; p.Installed = true;
            progress?.Report(Tr("tModLoader instalado. Importe os mods deste perfil antes de ligar."));
        }
        finally
        {
            // The staging directory is created under this owned profile, never derived from an archive entry.
            if (Directory.Exists(stage) && Path.GetFullPath(stage).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(stage, true);
        }
    }
    internal static void Extract(string archive, string root, bool loader, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 15000) throw new InvalidDataException(Tr("Pacote com arquivos demais."));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in zip.Entries)
        {
            var relative = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (relative.Length == 0) continue;
            _ = SafePaths.Inside(root, relative);
            if (!seen.Add(relative) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 || (entry.ExternalAttributes & 0x400) != 0)
                throw new InvalidDataException(Tr("Pacote contém caminho repetido ou link."));
            total += entry.Length;
            if (entry.Length > 256L * 1024 * 1024 || total > 1536L * 1024 * 1024) throw new InvalidDataException(Tr("Pacote grande demais."));
        }
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var relative = entry.FullName.Replace('\\', '/');
            if (relative.EndsWith('/')) continue;
            if (loader && (relative.StartsWith("LaunchUtils/", StringComparison.OrdinalIgnoreCase) ||
                relative is "serverconfig.txt" or "cli-argsConfig.txt" || relative.EndsWith(".runtimeconfig.dev.json", StringComparison.OrdinalIgnoreCase) ||
                new[] { ".bat", ".cmd", ".sh", ".ps1", ".exe" }.Contains(Path.GetExtension(relative).ToLowerInvariant()))) continue;
            var target = SafePaths.Inside(root, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = entry.Open(); using var output = File.Create(target);
            Downloads.CopyLimitedAsync(source, output, 256L * 1024 * 1024, ct).GetAwaiter().GetResult();
        }
        if (loader && (!File.Exists(SafePaths.Inside(root, "tModLoader.dll")) || !File.Exists(SafePaths.Inside(root, "tModLoader.runtimeconfig.json"))))
            throw new InvalidDataException(Tr("Pacote tModLoader incompleto."));
    }
    internal static void Validate(ServerProfile p)
    {
        if (p.Mods.Count > 300 || p.Mods.Select(m => m.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != p.Mods.Count)
            throw new InvalidDataException(Tr("Lista de mods inválida ou repetida."));
        foreach (var mod in p.Mods)
            if (!Regex.IsMatch(mod.Name, "^[A-Za-z][A-Za-z0-9_]{0,99}$") || !Version.TryParse(mod.Version, out _) || !Regex.IsMatch(mod.Sha256, "^[A-Fa-f0-9]{64}$"))
                throw new InvalidDataException(Tr("Metadados do mod inválidos."));
        if (p.TerrariaPreset is not ("Custom" or "Calamity")) throw new InvalidDataException(Tr("Preset de mods desconhecido."));
        if (!p.Installed) return;
        if (p.LaunchJar != "engine/tModLoader.dll" || p.NativeFileHashes.Count is < 3 or > 15000 ||
            !p.NativeFileHashes.ContainsKey("engine/tModLoader.dll") || !p.NativeFileHashes.ContainsKey("engine/dotnet/dotnet.exe"))
            throw new InvalidDataException(Tr("Instalação tModLoader incompleta."));
        foreach (var (name, hash) in p.NativeFileHashes)
            if (!name.StartsWith("engine/", StringComparison.Ordinal) || !Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$")) throw new InvalidDataException(Tr("Verificação tModLoader inválida."));
    }
    public static TerrariaMod ReadMod(string file)
    {
        SafePaths.RejectLink(file);
        using var input = File.OpenRead(file);
        if (input.Length is < 290 or > 1073741824) throw new InvalidDataException(Tr("O arquivo .tmod é inválido ou excede 1 GB."));
        using var reader = new BinaryReader(input, Encoding.UTF8, true);
        string ReadText() { var count = reader.Read7BitEncodedInt(); if (count is < 1 or > 200) throw new InvalidDataException(Tr("Cabeçalho .tmod inválido.")); var bytes = reader.ReadBytes(count); if (bytes.Length != count) throw new EndOfStreamException(); return Encoding.UTF8.GetString(bytes); }
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "TMOD") throw new InvalidDataException(Tr("Este arquivo não é um mod tModLoader."));
        var loader = ReadText();
        if (!Version.TryParse(loader, out var loaderVersion) || loaderVersion.Major < 2022) throw new InvalidDataException(Tr("Importe mods da linha tModLoader 1.4 atual; mods antigos não são suportados."));
        var embeddedHash = reader.ReadBytes(20); if (reader.ReadBytes(256).Length != 256) throw new EndOfStreamException();
        var size = reader.ReadInt32(); var hashStart = input.Position;
        if (size != input.Length - hashStart) throw new InvalidDataException(Tr("Arquivo .tmod truncado."));
        var name = ReadText(); var version = ReadText();
        if (!Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_]{0,99}$") || !Version.TryParse(version, out _)) throw new InvalidDataException(Tr("Identidade interna do mod inválida."));
        input.Position = hashStart;
        if (!SHA1.HashData(input).SequenceEqual(embeddedHash)) throw new InvalidDataException(Tr("O conteúdo do .tmod não corresponde ao hash interno."));
        input.Position = 0;
        return new(name, version, loader, Convert.ToHexString(SHA256.HashData(input)));
    }
    public static List<TerrariaMod> ImportMods(ServerProfile p, string root, IEnumerable<string> sources)
    {
        if (!p.IsTModLoader) throw new InvalidOperationException(Tr("Crie um perfil tModLoader para importar mods."));
        var files = sources.ToList(); if (files.Count is < 1 or > 300) throw new InvalidDataException(Tr("Selecione até 300 mods por vez."));
        var inspected = files.Select(f => (file: f, mod: ReadMod(f))).ToList();
        if (inspected.Select(x => x.mod.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != inspected.Count) throw new InvalidDataException(Tr("Selecione apenas uma versão de cada mod."));
        var combined = p.Mods.Where(m => !inspected.Any(x => x.mod.Name.Equals(m.Name, StringComparison.OrdinalIgnoreCase))).Concat(inspected.Select(x => x.mod)).ToList();
        if (combined.Count > 300) throw new InvalidDataException(Tr("Este perfil permite até 300 mods."));
        var stage = SafePaths.Inside(root, ".mods-stage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            foreach (var (file, mod) in inspected)
            {
                var copy = SafePaths.Inside(stage, mod.Name + ".tmod"); File.Copy(file, copy);
                if (ReadMod(copy) != mod) throw new InvalidDataException(Tr("O mod mudou durante a importação."));
            }
            var mods = SafePaths.Inside(root, "mods"); Directory.CreateDirectory(mods);
            foreach (var (_, mod) in inspected) File.Move(SafePaths.Inside(stage, mod.Name + ".tmod"), SafePaths.Inside(mods, mod.Name + ".tmod"), true);
            return combined;
        }
        finally { if (Directory.Exists(stage) && Path.GetFullPath(stage).StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) Directory.Delete(stage, true); }
    }
    public static void WriteEnabled(ServerProfile p, string root)
    {
        var mods = SafePaths.Inside(root, "mods"); Directory.CreateDirectory(mods);
        var temp = SafePaths.Inside(mods, ".enabled-" + Guid.NewGuid().ToString("N"));
        try { File.WriteAllText(temp, JsonSerializer.Serialize(p.Mods.Select(m => m.Name))); File.Move(temp, SafePaths.Inside(mods, "enabled.json"), true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static void VerifyMods(ServerProfile p, string root, CancellationToken ct = default)
    {
        foreach (var mod in p.Mods)
        {
            ct.ThrowIfCancellationRequested();
            if (ReadMod(SafePaths.Inside(root, "mods/" + mod.Name + ".tmod")) != mod) throw new InvalidDataException(Tr("Um mod foi alterado. Importe a versão desejada novamente."));
        }
        if (p.TerrariaPreset == "Calamity" && (!p.Mods.Any(m => m.Name == "CalamityMod") || !p.Mods.Any(m => m.Name == "CalamityModMusic")))
            throw new InvalidDataException("Calamity precisa de CalamityMod e CalamityModMusic. Importe os dois arquivos .tmod em Mods.");
        WriteEnabled(p, root);
    }
    public static string[] Arguments(string root) => ["exec", "--additionalprobingpath", SafePaths.Inside(root, "engine/Libraries"), SafePaths.Inside(root, "engine/tModLoader.dll"), "-server", "-nosteam", "-config", SafePaths.Inside(root, "serverconfig.txt"), "-modpath", SafePaths.Inside(root, "mods"), "-tmlsavedirectory", SafePaths.Inside(root, "saves"), "-noupnp"];
}
