using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace NandoHost.Core;

public sealed class Downloads : IDisposable
{
    private readonly HttpClient http;
    private static readonly HashSet<string> Hosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "piston-meta.mojang.com", "piston-data.mojang.com", "launchermeta.mojang.com", "launcher.mojang.com",
        "meta.fabricmc.net", "maven.fabricmc.net", "api.modrinth.com", "cdn.modrinth.com",
        "api.curseforge.com", "edge.forgecdn.net", "mediafilez.forgecdn.net", "www.curseforge.com",
        "net-secondary.web.minecraft-services.net"
    };
    public Downloads(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All });
        http.Timeout = TimeSpan.FromMinutes(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("NandoHost/0.8.0 (local game server manager)");
    }
    public static void ValidateUri(Uri uri)
    {
        var agentDownload = uri.Host == "github.com" && uri.AbsolutePath == new Uri(PlayitAgent.DownloadUrl).AbsolutePath;
        var tml = string.IsNullOrEmpty(uri.Query) && (uri.Host == "api.github.com" && uri.AbsolutePath == "/repos/tModLoader/tModLoader/releases/latest" ||
            uri.Host == "github.com" && System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/tModLoader/tModLoader/releases/download/v[0-9]+(?:\.[0-9]+){3}/tModLoader\.zip$"));
        var runtime = string.IsNullOrEmpty(uri.Query) && (uri.Host == "dotnetcli.blob.core.windows.net" && System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/dotnet/release-metadata/(?:8|9|10)\.0/releases\.json$") ||
            uri.Host == "builds.dotnet.microsoft.com" && System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/dotnet/Runtime/(?:8|9|10)\.0\.[0-9]+/dotnet-runtime-(?:8|9|10)\.0\.[0-9]+-win-x64\.zip$"));
        var bedrockDownload = uri.Host == "www.minecraft.net" && System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/bedrockdedicatedserver/bin-win(?:-preview)?/bedrock-server-[0-9]+(?:\.[0-9]+){2,3}\.zip$");
        var terrariaDownload = uri.Host == "terraria.org" && string.IsNullOrEmpty(uri.Query) &&
            (uri.AbsolutePath == "/api/get/dedicated-servers-names" || System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/api/download/pc-dedicated-server/terraria-server-[0-9]{4,8}\.zip$"));
        if (uri.Scheme != "https" || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) || !(Hosts.Contains(uri.Host) || agentDownload || bedrockDownload || terrariaDownload || tml || runtime))
            throw new InvalidDataException("Download bloqueado: somente HTTPS em domínios oficiais autorizados. " + uri.Host);
    }
    private async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct)
    {
        var uri = new Uri(url);
        var agentRequest = url == PlayitAgent.DownloadUrl;
        ValidateUri(uri);
        var tmlRequest = uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/tModLoader/tModLoader/releases/download/", StringComparison.Ordinal);
        for (var i = 0; i < 6; i++)
        {
            if (!((agentRequest || tmlRequest) && uri.Scheme == "https" && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) && uri.Host == "release-assets.githubusercontent.com")) ValidateUri(uri);
            var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                response.Dispose();
                continue;
            }
            try { response.EnsureSuccessStatusCode(); }
            catch { response.Dispose(); throw; }
            return response;
        }
        throw new HttpRequestException("O download excedeu o limite de redirecionamentos.");
    }
    public async Task<JsonDocument> JsonAsync(string url, CancellationToken ct = default)
    {
        using var response = await GetAsync(url, ct);
        if (response.Content.Headers.ContentLength > 16 * 1024 * 1024) throw new InvalidDataException("Resposta de API grande demais.");
        using var source = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        await CopyLimitedAsync(source, buffer, 16 * 1024 * 1024, ct);
        return JsonDocument.Parse(buffer.ToArray());
    }
    public async Task FileAsync(string url, string path, string? hash = null, string algorithm = "SHA512", long maxBytes = 1024L * 1024 * 1024, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".download-" + Guid.NewGuid().ToString("N");
        try
        {
            using var response = await GetAsync(url, ct);
            if (response.Content.Headers.ContentLength > maxBytes) throw new InvalidDataException("Arquivo excede o tamanho permitido.");
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                await CopyLimitedAsync(source, destination, maxBytes, ct);
            if (hash != null)
            {
                await using var stream = File.OpenRead(temporary);
                var digest = algorithm switch { "SHA1" => await SHA1.HashDataAsync(stream, ct), "SHA256" => await SHA256.HashDataAsync(stream, ct), "SHA512" => await SHA512.HashDataAsync(stream, ct), _ => throw new InvalidDataException("Hash não suportado.") };
                if (!Convert.ToHexString(digest).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Integridade inválida: o arquivo baixado foi rejeitado.");
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static async Task CopyLimitedAsync(Stream input, Stream output, long limit, CancellationToken ct)
    {
        var buffer = new byte[65536];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct)) != 0)
        {
            total += read;
            if (total > limit) throw new InvalidDataException("Arquivo excede o tamanho permitido.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }
    public void Dispose() => http.Dispose();
}
