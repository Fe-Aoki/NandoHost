using System.Text.Json;
using System.Text.RegularExpressions;
using static NandoHost.Core.Localizer;

namespace NandoHost.Core;

public sealed record ServerVersion(string Id, bool Preview = false, string DownloadUrl = "", string DownloadSha256 = "")
{
    public string Label => Id + (Preview ? " · Preview (experimental)" : "");
    public override string ToString() => Label;
}
public sealed record ModArtifact(string Project, string Version, string Url, string Sha512, string Target);
public sealed record CrossplayPlan(string Minecraft, ModArtifact Geyser, ModArtifact Floodgate, ModArtifact FabricApi);

public sealed class ReleaseCatalog(Downloads downloads)
{
    public const string BedrockLinks = "https://net-secondary.web.minecraft-services.net/api/v1.0/download/links";
    public const string GeyserId = "wKkoqHrH", FloodgateId = "bWrNNfkb", FabricApiId = "P7dR8mSH";
    private JsonElement? geyser;
    private readonly Dictionary<string, List<ServerVersion>> cache = new();
    public void Refresh() { cache.Clear(); geyser = null; }
    public async Task<List<ServerVersion>> VersionsAsync(string edition, CancellationToken ct = default)
    {
        if (cache.TryGetValue(edition, out var saved)) return [.. saved];
        List<ServerVersion> versions;
        if (edition == "Bedrock")
        {
            using var doc = await downloads.JsonAsync(BedrockLinks, ct);
            versions = ParseBedrock(doc.RootElement);
        }
        else if (edition is "Java" or "Crossplay")
        {
            versions = (await new Installer(downloads).VersionsAsync(ct)).Select(v => new ServerVersion(v)).ToList();
            if (edition == "Crossplay")
            {
                var latest = await LatestGeyserAsync(ct);
                var supported = latest.GetProperty("game_versions").EnumerateArray().Select(v => v.GetString()).ToHashSet();
                versions = versions.Where(v => supported.Contains(v.Id)).ToList();
                // Only offer current Geyser protocols with matching Floodgate and Fabric API artifacts.
                var compatible = new List<ServerVersion>();
                foreach (var v in versions)
                    try { await PlanAsync(v.Id, ct); compatible.Add(v); }
                    catch (InvalidDataException) { }
                versions = compatible;
            }
        }
        else throw new InvalidDataException(Tr("Edição desconhecida."));
        if (versions.Count == 0) throw new InvalidDataException(Tr("Nenhuma versão compatível foi encontrada no catálogo oficial."));
        ct.ThrowIfCancellationRequested(); cache[edition] = versions;
        return [.. versions];
    }
    public static List<ServerVersion> ParseBedrock(JsonElement root)
    {
        var versions = new List<ServerVersion>();
        foreach (var link in root.GetProperty("result").GetProperty("links").EnumerateArray())
        {
            var type = link.GetProperty("downloadType").GetString();
            if (type is not ("serverBedrockWindows" or "serverBedrockPreviewWindows")) continue;
            var url = link.GetProperty("downloadUrl").GetString()!;
            Downloads.ValidateUri(new Uri(url));
            var match = Regex.Match(new Uri(url).AbsolutePath, @"/bedrock-server-(?<version>[0-9]+(?:\.[0-9]+){2,3})\.zip$");
            if (!match.Success || new Uri(url).Host != "www.minecraft.net") throw new InvalidDataException(Tr("Link Bedrock inválido."));
            versions.Add(new(match.Groups["version"].Value, type == "serverBedrockPreviewWindows", url));
        }
        return versions.OrderBy(v => v.Preview).ToList();
    }
    private static string Query(string project, string? version = null)
    {
        var query = "https://api.modrinth.com/v2/project/" + project + "/version?loaders=%5B%22fabric%22%5D";
        return version == null ? query : query + "&game_versions=" + Uri.EscapeDataString(JsonSerializer.Serialize(new[] { version }));
    }
    private async Task<JsonElement> LatestGeyserAsync(CancellationToken ct)
    {
        if (geyser is { } g) return g;
        using var doc = await downloads.JsonAsync(Query(GeyserId), ct);
        var newest = doc.RootElement.EnumerateArray()
            .Where(v => v.GetProperty("version_type").GetString() != "alpha" && v.GetProperty("loaders").EnumerateArray().Any(l => l.GetString() == "fabric"))
            .OrderByDescending(v => v.GetProperty("date_published").GetDateTimeOffset()).FirstOrDefault();
        if (newest.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException(Tr("Geyser Fabric indisponível."));
        geyser = newest.Clone(); return geyser.Value;
    }
    public async Task<CrossplayPlan> PlanAsync(string minecraft, CancellationToken ct = default)
    {
        var g = await LatestGeyserAsync(ct);
        if (!g.GetProperty("game_versions").EnumerateArray().Any(v => v.GetString() == minecraft))
            throw new InvalidDataException(Tr("O modo Java + Bedrock exige uma versão suportada pelo Geyser atual. Atualize a lista de versões."));
        return new(minecraft, Artifact(g, GeyserId, "Geyser-Fabric.jar"),
            await FindModAsync(FloodgateId, minecraft, "floodgate-fabric.jar", ct),
            await FindModAsync(FabricApiId, minecraft, "fabric-api.jar", ct));
    }
    private async Task<ModArtifact> FindModAsync(string project, string minecraft, string target, CancellationToken ct)
    {
        using var doc = await downloads.JsonAsync(Query(project, minecraft), ct);
        var version = doc.RootElement.EnumerateArray().Where(v => v.GetProperty("version_type").GetString() == "release"
            && v.GetProperty("game_versions").EnumerateArray().Any(g => g.GetString() == minecraft)
            && v.GetProperty("loaders").EnumerateArray().Any(l => l.GetString() == "fabric"))
            .OrderByDescending(v => v.GetProperty("date_published").GetDateTimeOffset()).FirstOrDefault();
        if (version.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException(Tr("Componente compatível indisponível: ") + target);
        return Artifact(version, project, target);
    }
    private static ModArtifact Artifact(JsonElement version, string project, string target)
    {
        if (version.GetProperty("project_id").GetString() != project) throw new InvalidDataException(Tr("Projeto inesperado no catálogo."));
        var files = version.GetProperty("files").EnumerateArray().Where(f => f.GetProperty("filename").GetString()!.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)).ToList();
        var file = files.FirstOrDefault(f => f.GetProperty("primary").GetBoolean());
        if (file.ValueKind == JsonValueKind.Undefined) file = files.FirstOrDefault();
        if (file.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException(Tr("JAR não encontrado."));
        var hash = file.GetProperty("hashes").GetProperty("sha512").GetString()!;
        if (!Regex.IsMatch(hash, "^[0-9a-fA-F]{128}$")) throw new InvalidDataException(Tr("Hash de componente inválido."));
        foreach (var dependency in version.GetProperty("dependencies").EnumerateArray())
            if (dependency.GetProperty("dependency_type").GetString() == "required" && dependency.GetProperty("project_id").GetString() != FabricApiId)
                throw new InvalidDataException(Tr("O componente passou a exigir outra dependência. Atualize o NandoHost."));
        var url = file.GetProperty("url").GetString()!; Downloads.ValidateUri(new Uri(url));
        if (new Uri(url).Host != "cdn.modrinth.com") throw new InvalidDataException(Tr("Origem de componente inválida."));
        return new(project, version.GetProperty("version_number").GetString()!, url, hash, target);
    }
}
