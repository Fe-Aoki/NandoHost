using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NandoHost.Core;

public sealed record TunnelNotice(string Message, string? ClaimUrl = null, string? Address = null, int? LocalPort = null);

// Only structured, non-secret notices leave the agent process. Raw output is never logged.
public static partial class TunnelOutput
{
    [GeneratedRegex(@"https://playit\.gg/claim/[a-fA-F0-9]{10}(?![a-zA-Z0-9])")]
    private static partial Regex Claim();
    [GeneratedRegex(@"^\s*(?<address>[a-zA-Z0-9][a-zA-Z0-9.\-]*(?::[0-9]{1,5})?) => 127\.0\.0\.1:(?<port>[0-9]{1,5})\s*$")]
    private static partial Regex Mapping();
    public static TunnelNotice? Parse(string line, int port)
    {
        if (line.Length > 4096) return null;
        var claim = Claim().Match(line);
        if (claim.Success) return new("Aguardando sua autorização no navegador.", claim.Value);
        var mapping = Mapping().Match(line);
        if (mapping.Success && int.TryParse(mapping.Groups["port"].Value, out var target) && target == port)
            return new("Endereço informado pelo agente; peça a um amigo para testar a entrada.", Address: mapping.Groups["address"].Value, LocalPort: target);
        if (line.Contains("tunnel running", StringComparison.OrdinalIgnoreCase)) return new("Relay iniciado pelo agente. A entrada no jogo ainda precisa ser testada.");
        if (line.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Contains("failed", StringComparison.OrdinalIgnoreCase))
            return new("O agente relatou uma falha. Confira sua internet e a configuração no playit.");
        return null;
    }
}

public sealed class PlayitAgent : IDisposable
{
    public const string Version = "0.17.1";
    public const string Hash = "9b00d6ff7d37d1052e5ae097e1348e11deae8617cd7a8ba39d1777f2006316a3";
    public const string DownloadUrl = "https://github.com/playit-cloud/playit-agent/releases/download/v0.17.1/playit-windows-x86_64-signed.exe";
    private Process? process;
    private Task? completion;
    private readonly string expectedHash;
    private bool disposed;
    public bool IsActive => process != null;
    public event Action<TunnelNotice>? Notice;
    public event Action? Changed;
    public PlayitAgent() : this(Hash) { }
    internal PlayitAgent(string expectedHash) => this.expectedHash = expectedHash;
    public async Task StartAsync(string executable, string privateFolder, int port, CancellationToken ct = default, int? secondaryPort = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsActive) throw new InvalidOperationException("O agente já está em execução.");
        if (port is < 1024 or > 65535) throw new InvalidDataException("Porta inválida.");
        SafePaths.RejectLink(privateFolder); SafePaths.RejectLink(executable);
        await using (var file = File.OpenRead(executable))
        {
            if (!Convert.ToHexString(await SHA256.HashDataAsync(file, ct)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Agente alterado ou versão incompatível. Baixe novamente pelo painel.");
        }
        var info = new ProcessStartInfo(Path.GetFullPath(executable))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, RedirectStandardInput = true, WorkingDirectory = privateFolder
        };
        info.ArgumentList.Add("--secret_path"); info.ArgumentList.Add(SafePaths.Inside(privateFolder, "playit.toml"));
        info.ArgumentList.Add("--stdout"); info.ArgumentList.Add("start");
        foreach (var key in new[] { "PLAYIT_SECRET", "PLAYIT_SECRET_PATH", "API_BASE", "PLAYIT_LOG" }) info.Environment.Remove(key);
        info.Environment["PLAYIT_LOG"] = "info";
        var child = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível iniciar o agente.");
        process = child;
        child.StandardInput.Close();
        completion = ObserveAsync(child, port, secondaryPort);
        Changed?.Invoke();
    }
    private async Task ObserveAsync(Process child, int port, int? secondaryPort)
    {
        try { await Task.WhenAll(child.WaitForExitAsync(), PumpAsync(child.StandardOutput, port, secondaryPort), PumpAsync(child.StandardError, port, secondaryPort)); }
        finally
        {
            if (process == child) process = null;
            child.Dispose();
            Notice?.Invoke(new("Agente parado. O endereço externo depende do agente ligado."));
            Changed?.Invoke();
        }
    }
    private async Task PumpAsync(StreamReader reader, int port, int? secondaryPort)
    {
        var buffer = new char[1024]; var line = new StringBuilder(); var oversized = false;
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
            for (var i = 0; i < count; i++)
            {
                var ch = buffer[i];
                if (ch is '\n' or '\r')
                {
                    if (!oversized)
                    {
                        var notice = TunnelOutput.Parse(line.ToString(), port) ?? (secondaryPort is { } secondary ? TunnelOutput.Parse(line.ToString(), secondary) : null);
                        if (notice != null) Notice?.Invoke(notice);
                    }
                    line.Clear(); oversized = false;
                }
                else if (!oversized && line.Length < 4096) line.Append(ch);
                else { oversized = true; line.Clear(); }
            }
    }
    public async Task StopAsync()
    {
        var child = process; var finished = completion;
        if (child == null) return;
        // This is exclusively our own portable agent, never a system service or another installation.
        try { if (!child.HasExited) child.Kill(true); } catch (InvalidOperationException) { }
        if (finished != null) await finished;
    }
    public void Dispose()
    {
        disposed = true;
        try { if (process is { HasExited: false } child) child.Kill(true); } catch (InvalidOperationException) { }
    }
}
