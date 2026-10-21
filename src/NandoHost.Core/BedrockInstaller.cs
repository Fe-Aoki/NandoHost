using System.IO.Compression;
using System.Security.Cryptography;

namespace NandoHost.Core;

public sealed class BedrockInstaller(Downloads downloads)
{
    public async Task InstallAsync(ServerProfile profile, string root, IProgress<string>? progress, CancellationToken ct)
    {
        var available = await new ReleaseCatalog(downloads).VersionsAsync("Bedrock", ct);
        var choice = available.FirstOrDefault(v => v.Id == profile.MinecraftVersion && v.Preview == profile.BedrockPreview)
            ?? throw new InvalidDataException("Esta versão Bedrock não está mais no catálogo oficial. Atualize a lista. Versões antigas não são baixadas de espelhos.");
        var zip = SafePaths.Inside(root, ".bedrock-download-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            progress?.Report("Baixando Bedrock dedicado do site oficial…");
            await downloads.FileAsync(choice.DownloadUrl, zip, maxBytes: 400L * 1024 * 1024, ct: ct);
            progress?.Report("Extraindo o servidor nativo com limites de tamanho e caminhos…");
            await Task.Run(() => ExtractOfficial(zip, root, ct), ct);
            var executable = SafePaths.Inside(root, "bedrock_server.exe");
            await using var stream = File.OpenRead(executable);
            if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z') throw new InvalidDataException("O pacote não contém um executável Windows válido.");
            stream.Position = 0;
            profile.NativeExecutableSha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
            profile.LaunchJar = "bedrock_server.exe"; profile.RequiredJava = 0;
            profile.Installed = true;
            progress?.Report("Bedrock instalado. Não precisa de Java. O limite de RAM da JVM não se aplica ao servidor nativo.");
        }
        finally { if (File.Exists(zip)) File.Delete(zip); }
    }
    internal static void ExtractOfficial(string archive, string root, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (zip.Entries.Count > 20000) throw new InvalidDataException("Pacote Bedrock contém arquivos demais.");
        long expanded = 0;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var relative = entry.FullName.TrimEnd('/'); if (relative.Length == 0) continue;
            SafePaths.Inside(root, relative);
            if (!paths.Add(relative)) throw new InvalidDataException("Entrada duplicada no pacote Bedrock.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Links não são permitidos.");
            expanded += entry.Length;
            if (entry.Length > 512L * 1024 * 1024 || expanded > 2L * 1024 * 1024 * 1024) throw new InvalidDataException("Pacote Bedrock grande demais.");
            var extension = Path.GetExtension(relative).ToLowerInvariant();
            if (extension is ".bat" or ".cmd" or ".ps1" or ".vbs" or ".msi" || extension == ".exe" && relative != "bedrock_server.exe")
                throw new InvalidDataException("Executável inesperado no pacote Bedrock.");
        }
        if (!paths.Contains("bedrock_server.exe")) throw new InvalidDataException("Servidor Bedrock nativo ausente no ZIP.");
        long actualExpanded = 0;
        foreach (var entry in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var relative = entry.FullName.TrimEnd('/'); if (relative.Length == 0) continue;
            var target = SafePaths.Inside(root, relative);
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            // Updates preserve worlds and operator/access configuration.
            if (File.Exists(target) && (relative.StartsWith("worlds/", StringComparison.OrdinalIgnoreCase) || relative is "server.properties" or "allowlist.json" or "permissions.json")) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var source = entry.Open(); using var file = File.Create(target);
            var buffer = new byte[65536]; long actualFile = 0; int read;
            while ((read = source.Read(buffer)) > 0)
            {
                ct.ThrowIfCancellationRequested(); actualFile += read; actualExpanded += read;
                if (actualFile > 512L * 1024 * 1024 || actualExpanded > 2L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("O conteúdo expandido ultrapassou o limite de segurança.");
                file.Write(buffer, 0, read);
            }
        }
    }
}
