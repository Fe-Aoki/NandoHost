using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using static NandoHost.Core.Localizer;

namespace NandoHost.Core;

public sealed partial class ServerProcess : IDisposable
{
    private Process? process;
    private WindowsGameConsole? terminal;
    private Task? stdoutTask, stderrTask;
    private StreamWriter? diskLog;
    private readonly object logLock = new();
    private string logFile = "";
    private bool disposed;
    private bool nativeBedrock, nativeTerraria;
    private readonly List<string> secrets = new();
    public ServerState State { get; private set; } = ServerState.Stopped;
    public event Action<LogEntry>? Log;
    public event Action<ServerState>? StateChanged;
    public bool IsActive => State != ServerState.Stopped;
    public int? ProcessId => process is { HasExited: false } ? process.Id : null;
    public (double MemoryMb, TimeSpan Cpu)? Metrics()
    {
        try { process?.Refresh(); return process is { HasExited: false } ? (process.WorkingSet64 / 1048576d, process.TotalProcessorTime) : null; }
        catch (InvalidOperationException) { return null; }
    }
    private void SetState(ServerState value) { State = value; StateChanged?.Invoke(value); }
    public void Message(string text, bool error = false) => Emit(text, error);
    private void Emit(string text, bool error = false)
    {
        if (nativeTerraria) text = Regex.Replace(text, @"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07\x1B]*(?:\x07|\x1B\\))", "");
        if (nativeTerraria && string.IsNullOrWhiteSpace(text)) return;
        var readinessText = text;
        lock (logLock) { foreach (var secret in secrets) if (secret.Length > 0) text = text.Replace(secret, "[oculto]", StringComparison.Ordinal); }
        text = text.Length > 4096 ? text[..4096] + "…" : text;
        var entry = new LogEntry(DateTime.Now, text, error);
        lock (logLock)
        {
            if (diskLog != null)
            {
                try
                {
                    if (diskLog.BaseStream.Length > 4 * 1024 * 1024)
                    {
                        diskLog.Dispose();
                        File.Move(logFile, logFile + ".previous", true);
                        diskLog = new StreamWriter(logFile, false, Encoding.UTF8) { AutoFlush = true };
                    }
                    diskLog.WriteLine(entry.ToString());
                }
                catch (IOException) { diskLog?.Dispose(); diskLog = null; }
            }
        }
        Log?.Invoke(entry);
        if (State == ServerState.Starting && readinessText.Contains("Done (") && readinessText.Contains("For help")) SetState(ServerState.Running);
        if (nativeTerraria && State == ServerState.Starting && readinessText.Contains("Server started", StringComparison.OrdinalIgnoreCase)) SetState(ServerState.Running);
        if (nativeBedrock && State == ServerState.Starting && readinessText.Contains("Server started.", StringComparison.OrdinalIgnoreCase)) SetState(ServerState.Running);
    }
    [GeneratedRegex("version \"(?<major>[0-9]+)(?:\\.(?<minor>[0-9]+))?")]
    private static partial Regex JavaVersionPattern();
    public static async Task<int> CheckJavaAsync(string executable, CancellationToken ct = default)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        info.ArgumentList.Add("-version");
        using var check = Process.Start(info) ?? throw new InvalidOperationException(Tr("Não foi possível iniciar o Java."));
        var errors = check.StandardError.ReadToEndAsync(ct);
        var output = check.StandardOutput.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try { await check.WaitForExitAsync(timeout.Token); }
        catch { try { check.Kill(true); } catch (InvalidOperationException) { } throw; }
        var result = await errors + await output;
        var match = JavaVersionPattern().Match(result);
        if (check.ExitCode != 0 || !match.Success) throw new InvalidOperationException(Tr("Java não reconhecido. Selecione um java.exe válido nas configurações."));
        var major = int.Parse(match.Groups["major"].Value);
        return major == 1 ? int.Parse(match.Groups["minor"].Value) : major;
    }
    public async Task StartAsync(ServerProfile profile, string root, CancellationToken ct = default)
    {
        if (disposed) throw new ObjectDisposedException(nameof(ServerProcess));
        if (IsActive) throw new InvalidOperationException(Tr("O servidor já está em execução."));
        if (!profile.IsTerraria && !profile.EulaAccepted) throw new InvalidOperationException(Tr("Leia e aceite a EULA do Minecraft antes de iniciar."));
        if (!profile.Installed) throw new InvalidOperationException(Tr("A instalação precisa terminar antes de iniciar o servidor."));
        ProfileStore.Validate(profile);
        nativeBedrock = profile.IsBedrock; nativeTerraria = profile.IsTerraria;
        lock (logLock) { secrets.Clear(); if (profile.WorldPassword.Length > 0) secrets.Add(profile.WorldPassword); }
        var jar = SafePaths.Inside(root, profile.LaunchJar);
        if (!File.Exists(jar)) throw new FileNotFoundException(Tr("Instale o servidor antes de iniciar."));
        SetState(ServerState.Starting);
        try
        {
            var java = profile.IsNative ? 0 : await CheckJavaAsync(profile.JavaPath, ct);
            if (!profile.IsNative && java < profile.RequiredJava) throw new InvalidOperationException(TrFormat("Este servidor precisa de Java {0} ou superior. Detectado: Java {1}. Selecione um Java compatível em Configurações.", profile.RequiredJava, java));
            CheckPorts(profile);
            if (profile.IsTerraria)
                foreach (var name in profile.IsTModLoader ? profile.NativeFileHashes.Keys.ToArray() : TerrariaModule.Binaries)
                {
                    await using var binary = File.OpenRead(SafePaths.Inside(root, name));
                    var hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(binary, ct));
                    if (!hash.Equals(profile.NativeFileHashes[name], StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(Tr("Um binário Terraria foi alterado. Reinstale pelo painel antes de iniciar."));
                }
            if (profile.IsBedrock)
            {
                await using var binary = File.OpenRead(jar);
                var hash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(binary, ct));
                if (!hash.Equals(profile.NativeExecutableSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(Tr("O executável Bedrock foi alterado. Reinstale pelo painel antes de iniciar."));
            }
            logFile = SafePaths.Inside(root, "lume-console.log");
            diskLog = new StreamWriter(logFile, true, Encoding.UTF8) { AutoFlush = true };
            process?.Dispose();
            if (profile.IsTerraria && OperatingSystem.IsWindows())
            {
                if (profile.IsTModLoader) await Task.Run(() => TModLoaderModule.VerifyMods(profile, root, ct), ct);
                terminal = profile.IsTModLoader
                    ? WindowsGameConsole.Start(SafePaths.Inside(root, "engine/dotnet/dotnet.exe"), TModLoaderModule.Arguments(root), SafePaths.Inside(root, "engine"))
                    : WindowsGameConsole.Start(jar, ["-config", SafePaths.Inside(root, "serverconfig.txt"), "-noupnp"], root);
                process = terminal.Process;
                stdoutTask = PumpAsync(terminal.Output, false); stderrTask = null;
                Emit(TrFormat("Iniciando {0} · Terraria dedicado · console nativo do Windows", profile.Name));
                _ = ObserveExitAsync(process); return;
            }
            var info = new ProcessStartInfo(profile.IsNative ? jar : profile.JavaPath)
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            if (profile.IsTerraria)
                foreach (var arg in new[] { "-config", SafePaths.Inside(root, "serverconfig.txt"), "-noupnp" }) info.ArgumentList.Add(arg);
            if (!profile.IsNative)
                foreach (var arg in new[] { "-Xms512M", $"-Xmx{profile.MemoryMb}M", "-Dfile.encoding=UTF-8", "-jar", jar, "nogui" }) info.ArgumentList.Add(arg);
            process = new Process { StartInfo = info, EnableRaisingEvents = false };
            if (!process.Start()) throw new InvalidOperationException(Tr("Falha ao iniciar o servidor."));
            Emit(profile.IsTerraria ? TrFormat("Iniciando {0} · Terraria dedicado · criação/carregamento do mundo pode demorar", profile.Name) : profile.IsBedrock ? TrFormat("Iniciando {0} · Bedrock nativo · memória gerenciada pelo servidor", profile.Name) : TrFormat("Iniciando {0} · Java {1} · limite {2} MB", profile.Name, java, profile.MemoryMb));
            stdoutTask = PumpAsync(process.StandardOutput, false);
            stderrTask = PumpAsync(process.StandardError, true);
            _ = ObserveExitAsync(process);
        }
        catch { SetState(ServerState.Stopped); CloseLog(); throw; }
    }
    internal static void CheckPorts(ServerProfile p)
    {
        if (!p.IsBedrock) CheckTcp(p.Port);
        if (p.IsBedrock) { CheckTcp(p.Port); CheckUdp(p.Port); CheckUdp(p.Port + 1); }
        if (p.IsCrossplay) { CheckTcp(p.BedrockPort); CheckUdp(p.BedrockPort); }
    }
    private static void CheckTcp(int port)
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, port);
        try { listener.Server.ExclusiveAddressUse = true; listener.Start(); } finally { listener.Stop(); }
    }
    private static void CheckUdp(int port)
    {
        using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
        socket.ExclusiveAddressUse = true; socket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, port));
    }
    private async Task PumpAsync(StreamReader reader, bool error)
    {
        // Bounded read buffer: even a mod writing a gigantic line cannot grow a ReadLine buffer forever.
        var buffer = new char[2048];
        var line = new StringBuilder();
        int read;
        try
        {
            while ((read = await reader.ReadAsync(buffer)) != 0)
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] == '\n' || nativeTerraria && buffer[i] == '\r') { if (line.Length > 0) Emit(line.ToString().TrimEnd('\r'), error); line.Clear(); }
                    else if (line.Length < 4096) line.Append(buffer[i]);
                }
            if (line.Length > 0) Emit(line.ToString(), error);
        }
        catch (IOException e) { Emit(Tr("Leitura do console: ") + e.Message, true); }
    }
    private async Task ObserveExitAsync(Process child)
    {
        await child.WaitForExitAsync();
        if (terminal != null) await terminal.CloseAsync();
        if (stdoutTask != null) await stdoutTask;
        if (stderrTask != null) await stderrTask;
        Emit(Tr("Processo encerrado · código ") + child.ExitCode, child.ExitCode != 0);
        CloseLog();
        terminal?.Dispose(); terminal = null;
        SetState(ServerState.Stopped);
    }
    public async Task SendAsync(string command)
    {
        if (process is not { HasExited: false }) throw new InvalidOperationException(Tr("O servidor está desligado."));
        command = command.Trim().TrimStart('/');
        if (command.Length == 0 || command.Length > 512 || command.Contains('\n') || command.Contains('\r') || command.Contains('\0'))
            throw new InvalidDataException(Tr("Envie apenas um comando por vez, com até 512 caracteres."));
        if (nativeTerraria && command.Any(char.IsControl))
            throw new InvalidDataException(Tr("Caracteres de controle não são permitidos no console Terraria."));
        if (nativeTerraria && Regex.IsMatch(command, @"^password(?:\s|$)", RegexOptions.IgnoreCase))
            throw new InvalidDataException(Tr("Altere a senha no campo protegido de Configurações com o servidor parado."));
        var input = terminal?.Input ?? process.StandardInput;
        await input.WriteLineAsync(command);
        await input.FlushAsync();
        Emit("> " + command);
    }
    public async Task<bool> StopAsync(TimeSpan? timeout = null)
    {
        if (!IsActive) return true;
        if (process is not { HasExited: false }) return State == ServerState.Stopped;
        SetState(ServerState.Stopping);
        await SendAsync(nativeTerraria ? "exit" : "stop");
        using var timer = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timer.Token); return true; }
        catch (OperationCanceledException) { Emit(Tr("O servidor ainda está salvando. Aguarde ou use Encerrar à força."), true); return false; }
    }
    public void ForceStop()
    {
        if (process is { HasExited: false }) { Emit(Tr("Encerramento forçado solicitado. O mundo pode precisar de recuperação."), true); process.Kill(true); }
    }
    private void CloseLog() { lock (logLock) { diskLog?.Dispose(); diskLog = null; } }
    public void Dispose() { if (IsActive) throw new InvalidOperationException(Tr("Pare o servidor antes de fechar.")); disposed = true; process?.Dispose(); CloseLog(); }
}
