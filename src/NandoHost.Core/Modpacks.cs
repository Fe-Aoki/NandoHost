using System.IO.Compression;
using System.Text.Json;
using static NandoHost.Core.Localizer;

namespace NandoHost.Core;

public sealed class Modpacks(Downloads downloads)
{
    public async Task<List<PackHit>> SearchAsync(string query, CancellationToken ct = default)
    {
        using var json = await downloads.JsonAsync("https://api.modrinth.com/v2/search?limit=20&facets=" +
            Uri.EscapeDataString("[[\"project_type:modpack\"]]") + "&query=" + Uri.EscapeDataString(query), ct);
        return json.RootElement.GetProperty("hits").EnumerateArray().Select(v => new PackHit(
            v.GetProperty("project_id").GetString()!, v.GetProperty("title").GetString()!,
            v.GetProperty("description").GetString()!, v.GetProperty("downloads").GetInt32(), v.GetProperty("slug").GetString()!)).ToList();
    }
    public async Task<List<PackVersion>> VersionsAsync(string projectId, CancellationToken ct = default)
    {
        using var json = await downloads.JsonAsync("https://api.modrinth.com/v2/project/" + SafePaths.Token(projectId) + "/version", ct);
        return json.RootElement.EnumerateArray().Select(v => new PackVersion(v.GetProperty("id").GetString()!,
            v.GetProperty("name").GetString()!, v.GetProperty("version_number").GetString()!,
            string.Join(", ", v.GetProperty("game_versions").EnumerateArray().Select(x => x.GetString())),
            string.Join(", ", v.GetProperty("loaders").EnumerateArray().Select(x => x.GetString())))).ToList();
    }
    public async Task DownloadAsync(string versionId, string destination, CancellationToken ct = default)
    {
        using var json = await downloads.JsonAsync("https://api.modrinth.com/v2/version/" + SafePaths.Token(versionId), ct);
        var file = json.RootElement.GetProperty("files").EnumerateArray().FirstOrDefault(v => v.GetProperty("primary").GetBoolean());
        if (file.ValueKind == JsonValueKind.Undefined) file = json.RootElement.GetProperty("files")[0];
        if (!file.GetProperty("filename").GetString()!.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(Tr("A versão selecionada não é um modpack .mrpack."));
        await downloads.FileAsync(file.GetProperty("url").GetString()!, destination,
            file.GetProperty("hashes").GetProperty("sha512").GetString(), ct: ct);
    }

    public static (string Name, string Minecraft, string Engine, string Loader) Inspect(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        var entry = zip.GetEntry("modrinth.index.json") ?? throw new InvalidDataException(Tr("Selecione um arquivo .mrpack do Modrinth."));
        if (entry.Length > 8 * 1024 * 1024) throw new InvalidDataException(Tr("Índice do pacote grande demais."));
        using var stream = entry.Open();
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        if (root.GetProperty("formatVersion").GetInt32() != 1 || root.GetProperty("game").GetString() != "minecraft")
            throw new InvalidDataException(Tr("Formato de modpack não suportado."));
        var deps = root.GetProperty("dependencies");
        var minecraft = SafePaths.Token(deps.GetProperty("minecraft").GetString()!);
        if (deps.TryGetProperty("forge", out _) || deps.TryGetProperty("neoforge", out _) || deps.TryGetProperty("quilt-loader", out _))
            throw new InvalidDataException(Tr("Este pacote exige Forge, NeoForge ou Quilt. O instalador automático desta primeira versão suporta Vanilla e Fabric. Nenhum arquivo foi instalado."));
        var unknown = deps.EnumerateObject().Where(p => p.Name is not ("minecraft" or "fabric-loader")).Select(p => p.Name).ToList();
        if (unknown.Count > 0) throw new InvalidDataException(Tr("Dependência não reconhecida: ") + string.Join(", ", unknown));
        var fabric = deps.TryGetProperty("fabric-loader", out var loader);
        return (root.GetProperty("name").GetString() ?? "Modpack", minecraft, fabric ? "Fabric" : "Vanilla", fabric ? SafePaths.Token(loader.GetString()!) : "");
    }

    public async Task ImportAsync(string archive, string destination, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        Inspect(archive);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new InvalidOperationException(Tr("Importe o modpack em um novo servidor para não misturar arquivos."));
        using var zip = ZipFile.OpenRead(archive);
        ValidateArchive(zip);
        using var indexStream = zip.GetEntry("modrinth.index.json")!.Open();
        using var json = JsonDocument.Parse(indexStream);
        // Validate both layers before downloading anything. The staging folder is never
        // committed when a pack asks to overwrite bootstrap or administrative files.
        foreach (var layer in new[] { "overrides/", "server-overrides/" })
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(layer, StringComparison.Ordinal) && !e.FullName.EndsWith('/')))
            {
                var relative = entry.FullName[layer.Length..];
                if (!IsClientOnly(relative)) SafePackPath(destination, relative);
            }
        var files = json.RootElement.GetProperty("files").EnumerateArray().ToArray();
        if (files.Length > 3000) throw new InvalidDataException(Tr("Modpack excede o limite de 3000 arquivos."));
        long total = 0;
        var planned = new List<(string Path, string Url, string Hash, long Size)>();
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            if (file.TryGetProperty("env", out var env) && env.TryGetProperty("server", out var server) && server.GetString() is "unsupported" or "optional") continue;
            var relative = file.GetProperty("path").GetString()!;
            if (IsClientOnly(relative)) continue;
            var path = SafePackPath(destination, relative);
            if (!duplicates.Add(path)) throw new InvalidDataException(Tr("O pacote contém arquivos duplicados."));
            var size = file.GetProperty("fileSize").GetInt64();
            if (size < 0 || size > 512L * 1024 * 1024) throw new InvalidDataException(Tr("Tamanho de arquivo inválido no modpack."));
            total += size;
            if (total > 4L * 1024 * 1024 * 1024) throw new InvalidDataException(Tr("O modpack excede 4 GB."));
            var hash = file.GetProperty("hashes").GetProperty("sha512").GetString()!;
            if (hash.Length != 128 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException(Tr("Hash SHA512 inválido no pacote."));
            // Choose a permitted mirror; never request arbitrary hosts supplied by an archive.
            var urls = file.GetProperty("downloads").EnumerateArray().Select(u => u.GetString()!).ToList();
            var url = urls.FirstOrDefault(u => { try { Downloads.ValidateUri(new Uri(u)); return true; } catch { return false; } });
            if (url == null) throw new InvalidDataException(Tr("O pacote exige um domínio não autorizado. Arquivo: ") + relative);
            planned.Add((path, url, hash, size));
        }
        Directory.CreateDirectory(destination);
        var count = 0;
        // Two simultaneous downloads keep disk, memory and bandwidth use bounded.
        await Parallel.ForEachAsync(planned, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = ct }, async (item, token) =>
        {
            await downloads.FileAsync(item.Url, item.Path, item.Hash, maxBytes: Math.Max(1, item.Size), ct: token);
            progress?.Report(TrFormat("Mods verificados: {0}/{1}", Interlocked.Increment(ref count), planned.Count));
        });
        foreach (var layer in new[] { "overrides/", "server-overrides/" })
            foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith(layer, StringComparison.Ordinal) && !e.FullName.EndsWith('/')))
            {
                ct.ThrowIfCancellationRequested();
                var relative = entry.FullName[layer.Length..];
                if (IsClientOnly(relative)) continue;
                var path = SafePackPath(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await using var source = entry.Open();
                await using var output = File.Create(path);
                await Downloads.CopyLimitedAsync(source, output, 512L * 1024 * 1024, ct);
            }
        File.Copy(archive, SafePaths.Inside(destination, "installed-pack.mrpack"));
        progress?.Report(Tr("Arquivos do modpack instalados. Mods opcionais de servidor foram ignorados."));
    }

    public static void ValidateArchive(ZipArchive zip)
    {
        if (zip.Entries.Count > 10000) throw new InvalidDataException(Tr("Arquivo ZIP contém entradas demais."));
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000) throw new InvalidDataException(Tr("Links simbólicos não são permitidos em modpacks."));
            if (entry.Length > 512L * 1024 * 1024) throw new InvalidDataException(Tr("Arquivo descompactado grande demais."));
            total += entry.Length;
            if (total > 4L * 1024 * 1024 * 1024) throw new InvalidDataException(Tr("O ZIP descompactado excede 4 GB."));
            // Validate even entries that will not be extracted.
            var name = entry.FullName.TrimEnd('/');
            if (name.Length > 0) SafePaths.Inside(Path.Combine(Path.GetTempPath(), "lume-validation"), name);
        }
    }
    public static string SafePackPath(string root, string relative)
    {
        var path = SafePaths.Inside(root, relative);
        var first = relative.Replace('\\', '/').Split('/')[0];
        var allowed = new[] { "mods", "config", "defaultconfigs", "kubejs", "scripts", "datapacks", "resourcepacks", "shaderpacks", "global_packs", "openloader" };
        if (!allowed.Contains(first, StringComparer.OrdinalIgnoreCase) || !relative.Replace('\\', '/').Contains('/'))
            throw new InvalidDataException(Tr("Arquivo fora das pastas autorizadas do modpack: ") + relative + Tr(". Use um pacote preparado para servidor."));
        if (new[] { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".vbs", ".lnk", ".com", ".scr" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException(Tr("Executável ou script do Windows bloqueado: ") + relative);
        return path;
    }

    private static bool IsClientOnly(string relative)
    {
        var value = relative.Replace('\\', '/').ToLowerInvariant();
        return value is "options.txt" or "optionsof.txt" or "optionsshaders.txt" or "servers.dat" or "icon.png" or "pack.png" ||
            value.StartsWith("resourcepacks/") || value.StartsWith("shaderpacks/");
    }

    public static async Task ImportCurseServerZipAsync(string archive, string destination, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(archive);
        ValidateArchive(zip);
        if (zip.GetEntry("manifest.json") != null)
            throw new InvalidDataException(Tr("Este é um pacote de cliente CurseForge. Baixe o Server Pack oficial na página do modpack. Downloads restritos não são contornados."));
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new InvalidOperationException(Tr("Use um novo servidor para importar o pacote."));
        var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/')).ToList();
        // Support a single wrapping folder, but require actual mods/config content.
        var prefix = entries.Any(e => e.FullName.StartsWith("mods/")) ? "" : CommonPrefix(entries);
        var copied = 0;
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var relative = entry.FullName[prefix.Length..];
            var first = relative.Split('/')[0];
            if (first is not ("mods" or "config" or "defaultconfigs" or "kubejs" or "scripts" or "datapacks" or "global_packs" or "openloader")) continue;
            var path = SafePackPath(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var source = entry.Open();
            await using var output = File.Create(path);
            await Downloads.CopyLimitedAsync(source, output, 512L * 1024 * 1024, ct);
            copied++;
        }
        if (copied == 0) throw new InvalidDataException(Tr("Nenhuma pasta de mods/configuração encontrada no pacote de servidor."));
        progress?.Report(TrFormat("{0} arquivos importados. Scripts de inicialização não foram executados.", copied));
    }
    private static string CommonPrefix(List<ZipArchiveEntry> entries)
    {
        var first = entries.FirstOrDefault()?.FullName.Split('/')[0] + "/";
        return first != "/" && entries.All(e => e.FullName.StartsWith(first, StringComparison.Ordinal)) ? first : "";
    }
}
