using System.Text.Json;
using static NandoHost.Core.Localizer;

namespace NandoHost.Core;

public sealed class Installer(Downloads downloads)
{
    private const string Manifest = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    public async Task<List<string>> VersionsAsync(CancellationToken ct = default)
    {
        using var json = await downloads.JsonAsync(Manifest, ct);
        return json.RootElement.GetProperty("versions").EnumerateArray()
            .Where(v => v.GetProperty("type").GetString() == "release")
            .Select(v => v.GetProperty("id").GetString()!).ToList();
    }
    public async Task InstallAsync(ServerProfile p, string root, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        ProfileStore.Validate(p);
        p.Installed = false;
        Directory.CreateDirectory(root);
        if (p.IsTModLoader) { await new TModLoaderModule(downloads).InstallAsync(p, root, progress, ct); return; }
        if (p.IsTerraria) { await new TerrariaModule(downloads).InstallAsync(p, root, progress, ct); return; }
        if (p.IsBedrock)
        {
            await new BedrockInstaller(downloads).InstallAsync(p, root, progress, ct); return;
        }
        CrossplayPlan? crossplay = p.IsCrossplay ? await new ReleaseCatalog(downloads).PlanAsync(p.MinecraftVersion, ct) : null;
        progress?.Report(Tr("Consultando a versão oficial do Minecraft…"));
        using var manifest = await downloads.JsonAsync(Manifest, ct);
        var match = manifest.RootElement.GetProperty("versions").EnumerateArray()
            .FirstOrDefault(v => v.GetProperty("id").GetString() == p.MinecraftVersion);
        if (match.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException(Tr("Versão do Minecraft não encontrada."));
        using var version = await downloads.JsonAsync(match.GetProperty("url").GetString()!, ct);
        p.RequiredJava = version.RootElement.TryGetProperty("javaVersion", out var java) ? java.GetProperty("majorVersion").GetInt32() : 8;
        if (!version.RootElement.GetProperty("downloads").TryGetProperty("server", out var server))
            throw new InvalidDataException(Tr("Esta versão não fornece servidor dedicado."));
        progress?.Report(Tr("Baixando o servidor oficial e verificando a integridade…"));
        await downloads.FileAsync(server.GetProperty("url").GetString()!, SafePaths.Inside(root, "server.jar"),
            server.GetProperty("sha1").GetString(), "SHA1", ct: ct);
        p.LaunchJar = "server.jar";
        if (p.Engine == "Fabric")
        {
            using var loaders = await downloads.JsonAsync("https://meta.fabricmc.net/v2/versions/loader/" + SafePaths.Token(p.MinecraftVersion), ct);
            if (string.IsNullOrEmpty(p.LoaderVersion))
            {
                var stable = loaders.RootElement.EnumerateArray().FirstOrDefault(v => v.GetProperty("loader").GetProperty("stable").GetBoolean());
                if (stable.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException(Tr("Nenhum Fabric estável disponível para esta versão."));
                p.LoaderVersion = stable.GetProperty("loader").GetProperty("version").GetString()!;
            }
            if (!loaders.RootElement.EnumerateArray().Any(v => v.GetProperty("loader").GetProperty("version").GetString() == p.LoaderVersion))
                throw new InvalidDataException(Tr("Fabric incompatível com a versão do Minecraft."));
            using var installers = await downloads.JsonAsync("https://meta.fabricmc.net/v2/versions/installer", ct);
            var installer = installers.RootElement.EnumerateArray().First(v => v.GetProperty("stable").GetBoolean()).GetProperty("version").GetString()!;
            progress?.Report(Tr("Baixando o inicializador oficial do Fabric…"));
            await downloads.FileAsync($"https://meta.fabricmc.net/v2/versions/loader/{SafePaths.Token(p.MinecraftVersion)}/{SafePaths.Token(p.LoaderVersion)}/{SafePaths.Token(installer)}/server/jar",
                SafePaths.Inside(root, "fabric-server-launch.jar"), maxBytes: 32 * 1024 * 1024, ct: ct);
            p.LaunchJar = "fabric-server-launch.jar";
        }
        if (crossplay != null)
        {
            progress?.Report(Tr("Instalando Geyser, Floodgate e Fabric API com verificação SHA-512…"));
            foreach (var component in new[] { crossplay.Geyser, crossplay.Floodgate, crossplay.FabricApi })
                await downloads.FileAsync(component.Url, SafePaths.Inside(root, "mods/" + component.Target), component.Sha512, maxBytes: 128L * 1024 * 1024, ct: ct);
            ProfileStore.WriteCrossplayConfig(p, root);
            File.WriteAllText(SafePaths.Inside(root, "nandohost-crossplay-components.json"), JsonSerializer.Serialize(crossplay, new JsonSerializerOptions { WriteIndented = true }));
            p.RequiredJava = Math.Max(p.RequiredJava, 21);
        }
        p.Installed = true;
        progress?.Report(Tr("Servidor instalado. Java ") + p.RequiredJava + Tr(" ou superior necessário."));
    }
}
