using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NandoHost.Core;

if (string.Equals(Path.GetFileName(Environment.ProcessPath),"bedrock_server.exe",StringComparison.OrdinalIgnoreCase))
{
    Console.WriteLine("[INFO] Server started.");
    string? cmd;
    while ((cmd = Console.ReadLine()) != null)
    {
        Console.WriteLine("received:"+cmd);
        if (cmd == "stop") break;
    }
    return 0;
}

if (string.Equals(Path.GetFileName(Environment.ProcessPath), "TerrariaServer.exe", StringComparison.OrdinalIgnoreCase))
{
    if (!args.Contains("-config") || !args.Contains("-noupnp") || !File.Exists(args[Array.IndexOf(args, "-config") + 1])) return 3;
    Console.WriteLine("Listening on port 7777");
    Console.WriteLine("Server started");
    string? command;
    while ((command = Console.ReadLine()) != null)
    {
        Console.WriteLine("received:" + command);
        if (command == "exit") { File.WriteAllText("exit-marker", "saved"); break; }
    }
    return 0;
}

// Portable-agent fixture: private credentials and arbitrary raw output must never escape.
if (args.Contains("--secret_path"))
{
    Console.WriteLine("Visit link to setup https://playit.gg/claim/0123456789");
    Console.WriteLine("secret_key=DO_NOT_LOG_THIS");
    Console.WriteLine("https://playit.gg/login/guest-account/DO_NOT_LOG_THIS");
    Console.WriteLine(new string('x', 20000));
    Console.WriteLine("tunnel running");
    Console.WriteLine("test.joinmc.link:12345 => 127.0.0.1:25565");
    Console.Out.Flush();
    await Task.Delay(Timeout.Infinite);
    return 0;
}

// A subprocess fixture exercises real pipes, argument boundaries and graceful shutdown.
if (args.Contains("-version")) { Console.Error.WriteLine("openjdk version \"21.0.1\""); return 0; }
if (args.Contains("-jar"))
{
    Console.WriteLine("[Server thread/INFO]: Done (0.001s)! For help, type \"help\"");
    string? command;
    while ((command = Console.ReadLine()) != null)
    {
        Console.WriteLine("received:" + command);
        if (command == "stop") { Console.WriteLine("Saving chunks"); break; }
        if (command == "flood") { for (var i = 0; i < 2000; i++) Console.WriteLine(new string('x', 5000)); }
    }
    return 0;
}

var root = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith('-')) ?? Path.Combine(Path.GetTempPath(), "lume-tests-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var results = new List<string>();
var failures = 0;
async Task Test(string name, Func<Task> action)
{
    try { await action(); results.Add("PASS " + name); Console.WriteLine(results[^1]); }
    catch (Exception ex) { failures++; results.Add("FAIL " + name + ": " + ex); Console.WriteLine(results[^1]); }
}
void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(Action action)
{
    try { action(); } catch (InvalidDataException) { return; }
    throw new Exception("Unsafe input was accepted");
}
async Task RejectAsync(Func<Task> action)
{
    try { await action(); } catch (InvalidDataException) { return; }
    throw new Exception("Unsafe input was accepted");
}
string Archive(string name, params (string Name, string Text)[] files)
{
    var path = Path.Combine(root, name);
    using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
    foreach (var entry in files) { using var writer = new StreamWriter(zip.CreateEntry(entry.Name).Open()); writer.Write(entry.Text); }
    return path;
}
var store = new ProfileStore(Path.Combine(root, "data"));
await Test("profile persistence + safe properties", () =>
{
    var p = new ServerProfile { Name = "Teste & sem shell" };
    store.Save([p]); var loaded = store.Load().Single();
    Check(loaded.Name == p.Name, "Round trip"); store.WriteProperties(loaded);
    var properties = File.ReadAllText(Path.Combine(store.ServerRoot(loaded), "server.properties"));
    Check(properties.Contains("online-mode=true") && properties.Contains("enable-rcon=false") && properties.Contains("white-list=true"), "Safe defaults");
    Check(File.ReadAllText(Path.Combine(store.ServerRoot(loaded), "eula.txt")).Contains("eula=false"), "No implicit EULA agreement");
    return Task.CompletedTask;
});
await Test("reject unsafe profile id", () => { Reject(() => store.ServerRoot(new ServerProfile { Id = "../escape" })); return Task.CompletedTask; });
await Test("reject invalid RAM and port", () => { Reject(() => ProfileStore.Validate(new ServerProfile { MemoryMb = 1 })); Reject(() => ProfileStore.Validate(new ServerProfile { Port = 80 })); return Task.CompletedTask; });
foreach (var path in new[] { "../outside", "a/../../outside", "C:/Windows/test", "/root/test", "\\\\server\\share", "config/a:stream", "config/CON.txt", "config/a./bad", "config/../bad" })
    await Test("reject archive path: " + path, () => { Reject(() => SafePaths.Inside(root, path)); return Task.CompletedTask; });
await Test("reject root executable and library overrides", () =>
{
    foreach (var path in new[] { "server.jar", "libraries/a.jar", "mods/payload.exe", "config/start.ps1", "eula.txt", "server.properties" }) Reject(() => Modpacks.SafePackPath(root, path));
    Check(Modpacks.SafePackPath(root, "mods/real.jar").StartsWith(root), "Normal mods accepted"); return Task.CompletedTask;
});
await Test("HTTPS and host restrictions", () =>
{
    foreach (var uri in new[] { "http://cdn.modrinth.com/a", "https://localhost/a", "https://127.0.0.1/a", "https://cdn.modrinth.com.evil.example/a", "https://cdn.modrinth.com:444/a", "https://user@cdn.modrinth.com/a" }) Reject(() => Downloads.ValidateUri(new Uri(uri)));
    Downloads.ValidateUri(new Uri("https://cdn.modrinth.com/a")); return Task.CompletedTask;
});
await Test("redirect target must also be permitted", async () =>
{
    var calls = 0;
    using var downloads = new Downloads(new Handler(_ => { calls++; var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new Uri("https://127.0.0.1/secret"); return r; }));
    await RejectAsync(() => downloads.FileAsync("https://cdn.modrinth.com/a", Path.Combine(root, "redirect.bin")));
    Check(calls == 1, "Unsafe redirect must never be requested");
});
await Test("hash mismatch is rejected atomically", async () =>
{
    var target = Path.Combine(root, "hash.bin"); File.WriteAllText(target, "original");
    using var downloads = new Downloads(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("corrupt") }));
    await RejectAsync(() => downloads.FileAsync("https://cdn.modrinth.com/a", target, new string('0', 128)));
    Check(File.ReadAllText(target) == "original", "Original preserved"); Check(!Directory.EnumerateFiles(root, "*.download-*").Any(), "Temporary cleaned");
});
await Test("download body has a hard size limit", async () =>
{
    using var downloads = new Downloads(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("too large") }));
    await RejectAsync(() => downloads.FileAsync("https://cdn.modrinth.com/a", Path.Combine(root, "limit.bin"), maxBytes: 2));
});
await Test("backup contains saved world and remains inside managed storage", async () =>
{
    var p = store.Load().Single(); var world = Path.Combine(store.ServerRoot(p), "world"); Directory.CreateDirectory(world);
    File.WriteAllText(Path.Combine(world, "level.dat"), "saved-world");
    var file = await store.BackupAsync(p); using var zip = ZipFile.OpenRead(file);
    Check(zip.Entries.Any(e => e.FullName.Replace('\\', '/') == "world/level.dat"), "World backed up");
});
await Test("malicious ZIP traversal rejected", () =>
{
    var archive = Archive("traversal.zip", ("mods/../../escape.jar", "bad")); using var zip = ZipFile.OpenRead(archive);
    Reject(() => Modpacks.ValidateArchive(zip)); return Task.CompletedTask;
});
await Test("client-only mod skipped + server layer wins", async () =>
{
    var hash = Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes("mod"))).ToLowerInvariant();
    var index = JsonSerializer.Serialize(new { formatVersion = 1, game = "minecraft", name = "Test", dependencies = new Dictionary<string, string> { ["minecraft"] = "1.21.1", ["fabric-loader"] = "0.16.10" }, files = new[] {
        new { path = "mods/client.jar", hashes = new { sha512 = hash }, env = new { server = "unsupported" }, fileSize = 3, downloads = new[] { "https://cdn.modrinth.com/client" } },
        new { path = "mods/server.jar", hashes = new { sha512 = hash }, env = new { server = "required" }, fileSize = 3, downloads = new[] { "https://cdn.modrinth.com/server" } }
    }});
    var archive = Archive("test.mrpack", ("modrinth.index.json", index), ("overrides/config/test.txt", "base"), ("server-overrides/config/test.txt", "server"));
    using var downloads = new Downloads(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("mod") }));
    var destination = Path.Combine(root, "pack"); await new Modpacks(downloads).ImportAsync(archive, destination);
    Check(!File.Exists(Path.Combine(destination, "mods/client.jar")), "Client-only skipped");
    Check(File.ReadAllText(Path.Combine(destination, "config/test.txt")) == "server", "Override order");
});
await Test("unsupported loader fails before installation", () =>
{
    var archive = Archive("forge.mrpack", ("modrinth.index.json", "{\"formatVersion\":1,\"game\":\"minecraft\",\"name\":\"Forge\",\"dependencies\":{\"minecraft\":\"1.21.1\",\"forge\":\"52.0.0\"},\"files\":[]}"));
    Reject(() => Modpacks.Inspect(archive)); return Task.CompletedTask;
});
await Test("unsafe override fails before any download", async () =>
{
    var index = "{\"formatVersion\":1,\"game\":\"minecraft\",\"name\":\"Bad\",\"dependencies\":{\"minecraft\":\"1.21.1\"},\"files\":[]}";
    var archive = Archive("bad-override.mrpack", ("modrinth.index.json", index), ("overrides/server.properties", "online-mode=false"));
    using var downloads = new Downloads(new Handler(_ => throw new Exception("No network calls permitted")));
    var destination = Path.Combine(root, "unsafe-override");
    await RejectAsync(() => new Modpacks(downloads).ImportAsync(archive, destination));
    Check(!Directory.Exists(destination), "Preflight must not write files");
});
await Test("CurseForge client manifest never treated as server pack", async () =>
{
    var archive = Archive("curse-client.zip", ("manifest.json", "{}"), ("mods/a.jar", "a"));
    await RejectAsync(() => Modpacks.ImportCurseServerZipAsync(archive, Path.Combine(root, "curse-client")));
});
await Test("CurseForge server ZIP extracts content but ignores launch scripts", async () =>
{
    var archive = Archive("curse-server.zip", ("server/mods/a.jar", "a"), ("server/config/a.json", "{}"), ("server/start.bat", "bad"));
    var destination = Path.Combine(root, "curse-server");
    await Modpacks.ImportCurseServerZipAsync(archive, destination);
    Check(File.Exists(Path.Combine(destination, "mods/a.jar")) && !File.Exists(Path.Combine(destination, "start.bat")), "Only content imported");
});
await Test("incomplete installation cannot start Java", async () =>
{
    using var runner = new ServerProcess();
    try { await runner.StartAsync(new ServerProfile { EulaAccepted = true }, root); }
    catch (InvalidOperationException ex) { Check(ex.Message.Contains("instalação"), "Expected incomplete installation check"); return; }
    throw new Exception("Incomplete installation was accepted");
});
await Test("real subprocess: start, ready, command, bounded logs, graceful stop", async () =>
{
    var p = new ServerProfile { JavaPath = Environment.ProcessPath!, EulaAccepted = true, Installed = true, MemoryMb = 1024, Port = 35654 };
    var folder = store.ServerRoot(p); Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "server.jar"), "fixture");
    using var runner = new ServerProcess();
    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var lineTooLong = false;
    runner.StateChanged += state => { if (state == ServerState.Running) ready.TrySetResult(); if (state == ServerState.Stopped) stopped.TrySetResult(); };
    runner.Log += entry => { if (entry.Text == "received:say hello & safe") received.TrySetResult(); if (entry.Text.Length > 4097) lineTooLong = true; };
    try
    {
        await runner.StartAsync(p, folder); await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        await RejectAsync(() => runner.SendAsync("say hello\nstop"));
        await runner.SendAsync("say hello & safe"); await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await runner.SendAsync("flood");
        Check(await runner.StopAsync(TimeSpan.FromSeconds(30)), "Graceful stop");
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Check(!lineTooLong, "Console lines bounded");
    }
    finally
    {
        if (runner.IsActive) { runner.ForceStop(); await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
    }
});
await Test("command completion: root, nested arguments and case", () =>
{
    Check(CommandAssist.Suggest("whi", 3).Single().Text == "whitelist ", "Root completion");
    Check(CommandAssist.Suggest("whitelist a", 11).Single().Text == "whitelist add ", "Nested completion");
    Check(CommandAssist.Suggest("TIME SET n", 10).Select(c => c.Text).SequenceEqual(new[] { "time set noon", "time set night" }), "Case insensitive choices");
    return Task.CompletedTask;
});
await Test("command completion: slash and middle token preserve suffix", () =>
{
    var completion = CommandAssist.Suggest("/time se night", 8).Single();
    Check(completion.Text == "/time set night" && completion.Caret == 10, "Replace token without eating following argument");
    return Task.CompletedTask;
});
await Test("command completion: known players are filtered and bounded", () =>
{
    var matches = CommandAssist.Suggest("op Na", 5, ["Nando", "nando", "Another", "Name\nstop", "Na_bad;cmd"]);
    Check(matches.Count == 1 && matches[0].Text == "op Nando", "Only valid distinct usernames");
    Check(CommandAssist.Suggest("", 0).Count <= 8, "UI list is bounded"); return Task.CompletedTask;
});
await Test("command completion: free text and unknown mod commands are untouched", () =>
{
    Check(CommandAssist.Suggest("say hello friends", 17).Count == 0, "No completion in chat text");
    Check(CommandAssist.Suggest("custommod:command test", 22).Count == 0, "No fabricated plugin completions");
    return Task.CompletedTask;
});
await Test("command completion: multiline input rejected", () =>
{
    Check(CommandAssist.Suggest("op Name\nstop", 12, ["Name"]).Count == 0, "No multiline completion"); return Task.CompletedTask;
});
await Test("command history restores the unfinished draft", () =>
{
    var history = new CommandHistory(); history.Remember("list"); history.Remember("say hello");
    Check(history.Previous("unfinished") == "say hello", "Newest command");
    Check(history.Previous("") == "list", "Previous command");
    Check(history.Next() == "say hello" && history.Next() == "unfinished", "Draft restored");
    Check(!history.IsNavigating, "Navigation finished"); return Task.CompletedTask;
});
await Test("command history is bounded, rejects multiline and ignores adjacent duplicates", () =>
{
    var history = new CommandHistory(); history.Remember("list"); history.Remember("list");
    Check(history.Count == 1, "No duplicate"); history.Remember("say hi\nstop"); Check(history.Count == 1, "No multiline");
    for (var i = 0; i < 150; i++) history.Remember("say " + i);
    Check(history.Count == 100, "Bounded memory"); history.Clear(); Check(history.Count == 0, "Per-server reset"); return Task.CompletedTask;
});
await Test("tunnel notices: claim allowlist, target port, disabled mapping and secret suppression", () =>
{
    Check(TunnelOutput.Parse("https://playit.gg/claim/0123456789",25565)?.ClaimUrl == "https://playit.gg/claim/0123456789", "Official claim");
    foreach (var line in new[] { "secret_key=abc123", "https://playit.gg/login/guest-account/secret", "https://evil.example/claim/0123456789", "test.joinmc.link => (disabled payment)", "test.joinmc.link => 127.0.0.1:25566", new string('a',5000) })
        Check(TunnelOutput.Parse(line,25565) == null, "Ignore sensitive or unrelated output");
    Check(TunnelOutput.Parse("test.joinmc.link:22222 => 127.0.0.1:25565",25565)?.Address == "test.joinmc.link:22222", "Matching mapping");
    return Task.CompletedTask;
});
await Test("agent downloads: pinned HTTPS source and SHA256 validation", async () =>
{
    Downloads.ValidateUri(new Uri(PlayitAgent.DownloadUrl));
    Reject(() => Downloads.ValidateUri(new Uri("https://github.com/evil/agent.exe")));
    var bytes = Encoding.UTF8.GetBytes("agent fixture");
    using var download = new Downloads(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }));
    var target = Path.Combine(root,"agent-download.bin");
    await download.FileAsync(PlayitAgent.DownloadUrl,target,Convert.ToHexString(SHA256.HashData(bytes)),"SHA256");
    await RejectAsync(() => download.FileAsync(PlayitAgent.DownloadUrl,target,new string('0',64),"SHA256"));
    Check(File.ReadAllBytes(target).SequenceEqual(bytes),"Bad update preserves existing file");
});
await Test("owned tunnel process: bounded output, no secrets, stop and restart", async () =>
{
    var folder = Path.Combine(root,"private-agent"); Directory.CreateDirectory(folder);
    var executable = Environment.ProcessPath!;
    var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable)));
    using var agent = new PlayitAgent(hash);
    var notices = new System.Collections.Concurrent.ConcurrentQueue<TunnelNotice>(); agent.Notice += notices.Enqueue;
    for (var run=0;run<2;run++)
    {
        await agent.StartAsync(executable,folder,25565);
        for (var i=0;i<100 && !notices.Any(n => n.Address != null);i++) await Task.Delay(50);
        Check(agent.IsActive && notices.Any(n => n.Address == "test.joinmc.link:12345"), "Real child output and active state");
        await agent.StopAsync(); Check(!agent.IsActive,"Child stopped");
    }
    Check(notices.All(n => !n.ToString().Contains("DO_NOT_LOG_THIS")),"No raw secrets in notices");
    Check(notices.Count < 15,"Bounded structured notices");
    using var rejected = new PlayitAgent(new string('0',64));
    await RejectAsync(() => rejected.StartAsync(executable,folder,25565));
    Check(!rejected.IsActive,"Tampered executable not launched");
});
await Test("old Java profiles retain their edition and Bedrock properties differ", () =>
{
    var old = JsonSerializer.Deserialize<ServerProfile>("{\"Name\":\"Old\",\"MinecraftVersion\":\"1.20.1\"}")!;
    Check(old.Edition == "Java" && !old.IsBedrock,"Backwards compatible default");
    var p = new ServerProfile { Edition="Bedrock", Engine="Bedrock", Port=19132, JavaPath="not-a-java" };
    store.WriteProperties(p);
    var props = File.ReadAllText(Path.Combine(store.ServerRoot(p),"server.properties"));
    Check(props.Contains("allow-list=true") && props.Contains("online-mode=true") && props.Contains("server-portv6=19133") && props.Contains("tick-distance=6"),"Bedrock properties");
    Check(!props.Contains("white-list=") && !props.Contains("simulation-distance="),"No Java-only properties");
    Reject(() => ProfileStore.Validate(new ServerProfile { Edition="Bedrock", Engine="Bedrock", SimulationDistance=3 }));
    Reject(() => ProfileStore.Validate(new ServerProfile { Edition="Crossplay", Engine="Vanilla" }));
    Reject(() => ProfileStore.Validate(new ServerProfile { Edition="Crossplay", Engine="Fabric", BedrockPort=25565 }));
    return Task.CompletedTask;
});
await Test("Bedrock catalogs: stable, Preview and approved URLs only", () =>
{
    using var doc=JsonDocument.Parse(JsonSerializer.Serialize(new { result=new { links=new[] {
        new { downloadType="serverBedrockWindows",downloadUrl="https://www.minecraft.net/bedrockdedicatedserver/bin-win/bedrock-server-1.26.52.3.zip" },
        new { downloadType="serverBedrockPreviewWindows",downloadUrl="https://www.minecraft.net/bedrockdedicatedserver/bin-win-preview/bedrock-server-1.26.70.21.zip" },
        new { downloadType="serverBedrockLinux",downloadUrl="https://example.com/ignored.zip" }
    }}}));
    var versions=ReleaseCatalog.ParseBedrock(doc.RootElement);
    Check(versions.Count==2 && versions[0].Id=="1.26.52.3" && !versions[0].Preview && versions[1].Preview,"Catalog separates stable and Preview");
    Reject(() => Downloads.ValidateUri(new Uri("https://www.minecraft.net/malicious.exe")));
    Reject(() => Downloads.ValidateUri(new Uri("https://www.minecraft.net/bedrockdedicatedserver/bin-win/../../evil.zip")));
    return Task.CompletedTask;
});
await Test("native ZIP preflight rejects traversal and executables, preserves saved state", () =>
{
    var folder=Path.Combine(root,"native-zip");Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder,"server.properties"),"keep=true");
    File.WriteAllText(Path.Combine(folder,"allowlist.json"),"[]");
    var zip=Archive("native-good.zip",("bedrock_server.exe","fixture"),("server.properties","replace=false"),("allowlist.json","bad"),("behavior_packs/test.json","{}"));
    BedrockInstaller.ExtractOfficial(zip,folder);
    Check(File.ReadAllText(Path.Combine(folder,"server.properties"))=="keep=true","Preserve properties");
    Check(File.ReadAllText(Path.Combine(folder,"allowlist.json"))=="[]","Preserve access list");
    var bad=Archive("native-bad.zip",("bedrock_server.exe","fixture"),("../escape.exe","bad"));
    Reject(() => BedrockInstaller.ExtractOfficial(bad,Path.Combine(root,"native-rejected")));
    var script=Archive("native-script.zip",("bedrock_server.exe","fixture"),("setup.ps1","bad"));
    Reject(() => BedrockInstaller.ExtractOfficial(script,Path.Combine(root,"native-script-rejected")));
    Check(!Directory.Exists(Path.Combine(root,"native-rejected")),"No writes before path validation");
    return Task.CompletedTask;
});
await Test("crossplay configuration preserves authentication and non-managed options", () =>
{
    var p=new ServerProfile { Edition="Crossplay",Engine="Fabric",BedrockPort=19142 };
    store.WriteProperties(p); var folder=store.ServerRoot(p);
    ProfileStore.WriteCrossplayConfig(p,folder);
    var file=Path.Combine(folder,"config/Geyser-Fabric/config.yml");
    File.AppendAllText(file,"# custom retained\nmotd:\n  primary-motd: test\n");
    p.BedrockPort=19144;ProfileStore.WriteCrossplayConfig(p,folder);
    var text=File.ReadAllText(file);
    Check(text.Contains("port: 19144") && text.Contains("transport: nethernet") && text.Contains("auth-type: floodgate") && text.Contains("primary-motd: test"),"Port update preserves config");
    var props=File.ReadAllText(Path.Combine(folder,"server.properties"));
    Check(props.Contains("online-mode=true") && props.Contains("enforce-secure-profile=false"),"Keep Java auth, adjust chat signature");
    return Task.CompletedTask;
});
await Test("native process: no Java required, ready, commands, graceful stop and binary integrity", async () =>
{
    var folder=Path.Combine(root,"native-process");Directory.CreateDirectory(folder);
    foreach(var file in Directory.GetFiles(AppContext.BaseDirectory))File.Copy(file,Path.Combine(folder,Path.GetFileName(file)),true);
    var executable=Path.Combine(folder,"bedrock_server.exe");File.Copy(Environment.ProcessPath!,executable,true);
    var p=new ServerProfile { Edition="Bedrock",Engine="Bedrock",Port=35660,Installed=true,EulaAccepted=true,JavaPath="missing-java",LaunchJar="bedrock_server.exe",NativeExecutableSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(executable))) };
    using var runner=new ServerProcess();
    var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var command=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var stopped=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    runner.StateChanged+=state=>{if(state==ServerState.Running)ready.TrySetResult();if(state==ServerState.Stopped)stopped.TrySetResult();};
    runner.Log+=entry=>{if(entry.Text=="received:allowlist add \"Nando Host\"")command.TrySetResult();};
    try
    {
        await runner.StartAsync(p,folder);await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await runner.SendAsync("allowlist add \"Nando Host\"");await command.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(await runner.StopAsync(TimeSpan.FromSeconds(5)),"Native stop command");await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        p.NativeExecutableSha256=new string('0',64);
        await RejectAsync(()=>runner.StartAsync(p,folder));
        Check(runner.State==ServerState.Stopped,"Bad native binary never executes");
    }
    finally {if(runner.IsActive){runner.ForceStop();await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));}}
});
await Test("native UDP and crossplay TCP conflicts are caught before launching", () =>
{
    using(var socket=new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Any,35662)))
    {
        try{ServerProcess.CheckPorts(new ServerProfile{Edition="Bedrock",Engine="Bedrock",Port=35662});throw new Exception("UDP conflict ignored");}
        catch(System.Net.Sockets.SocketException){}
    }
    var listener=new System.Net.Sockets.TcpListener(IPAddress.Any,35664);listener.Start();
    try
    {
        try{ServerProcess.CheckPorts(new ServerProfile{Edition="Crossplay",Engine="Fabric",Port=35665,BedrockPort=35664});throw new Exception("Crossplay TCP conflict ignored");}
        catch(System.Net.Sockets.SocketException){}
    }
    finally{listener.Stop();}
    return Task.CompletedTask;
});
await Test("edition-specific console completions", () =>
{
    Check(CommandAssist.Suggest("all",3,edition:"Bedrock").Single().Text=="allowlist ","Bedrock allowlist");
    Check(CommandAssist.Suggest("whi",3,edition:"Bedrock").Count==0,"No Java whitelist on native Bedrock");
    Check(CommandAssist.Suggest("save ",5,edition:"Bedrock").Count==3,"Bedrock save subcommands");
    Check(CommandAssist.Suggest("whitelist add .N",16,new[]{".Nando"},"Crossplay").Single().Text=="whitelist add .Nando","Floodgate prefix");
    return Task.CompletedTask;
});
await Test("crossplay catalog: current protocol only, official projects and version compatibility", async () =>
{
    var requests=0; var wrongFloodgate=false;
    object Mod(string project,string game,string type) => new {
        project_id=project,version_number="fixture",version_type=type,date_published="2026-10-20T00:00:00Z",
        game_versions=new[]{game},loaders=new[]{"fabric"},dependencies=Array.Empty<object>(),
        files=new[]{new{filename="mod.jar",primary=true,url="https://cdn.modrinth.com/data/fixture.jar",hashes=new{sha512=new string('a',128)}}}
    };
    using var downloads=new Downloads(new Handler(request=>
    {
        requests++;
        object result=request.RequestUri!.Host=="piston-meta.mojang.com"
            ? new { versions=new[]{new {type="release",id="26.3"},new {type="release",id="1.21.1"}} }
            : request.RequestUri.AbsolutePath.Contains(ReleaseCatalog.GeyserId) ? new[]{Mod(ReleaseCatalog.GeyserId,"26.3","beta")}
            : request.RequestUri.AbsolutePath.Contains(ReleaseCatalog.FloodgateId) ? new[]{Mod(ReleaseCatalog.FloodgateId,wrongFloodgate?"1.21.1":"26.3","release")}
            : new[]{Mod(ReleaseCatalog.FabricApiId,"26.3","release")};
        return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(result))};
    }));
    var catalog=new ReleaseCatalog(downloads);var list=await catalog.VersionsAsync("Crossplay");
    Check(list.Count==1 && list[0].Id=="26.3","Older Java versions not offered for current bridge");
    var count=requests;await catalog.VersionsAsync("Crossplay");Check(requests==count,"Catalog cached between UI switches");
    wrongFloodgate=true;catalog.Refresh();
    await RejectAsync(()=>catalog.VersionsAsync("Crossplay"));
});
var agentIndex = Array.IndexOf(args, "--agent-path");
if (agentIndex >= 0)
    await Test("official portable agent: real browser claim detection without account authorization", async () =>
    {
        using var agent = new PlayitAgent();
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        agent.Notice += notice => { if (notice.ClaimUrl != null) pending.TrySetResult(true); };
        var folder = Path.Combine(root,"unclaimed-official-agent"); Directory.CreateDirectory(folder);
        try
        {
            await agent.StartAsync(args[agentIndex+1],folder,25565);
            await pending.Task.WaitAsync(TimeSpan.FromSeconds(25));
        }
        finally { await agent.StopAsync(); }
        Check(!File.Exists(Path.Combine(folder,"playit.toml")),"No credential created without browser approval");
    });

ServerProfile Terra() => new() { Game="Terraria", Edition="PC", Engine="Terraria", MinecraftVersion="1.4.5.8", Port=37777, MaxPlayers=8, LaunchJar="TerrariaServer.exe", JavaPath="missing-java" };
await Test("Terraria profiles: migration, native fields, config injection rejection", () =>
{
    Check(JsonSerializer.Deserialize<ServerProfile>("{}")!.Game == "Minecraft", "Old profiles default to Minecraft");
    var p=Terra(); ProfileStore.Validate(p); Check(p.IsNative && !p.IsBedrock && !p.IsCrossplay,"Native PC classification");
    p.WorldSeed="hello\nport=80"; Reject(()=>ProfileStore.Validate(p)); p.WorldSeed="";
    p.WorldSize=4; Reject(()=>ProfileStore.Validate(p)); p.WorldSize=1;
    p.Game="Unknown"; Reject(()=>ProfileStore.Validate(p));
    return Task.CompletedTask;
});
await Test("Terraria official catalog and HTTPS routes are restricted", async () =>
{
    using var downloads=new Downloads(new Handler(_=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("[\"terraria-server-1458.zip\",\"terraria-server-1458.zip\",\"../evil.exe\"]")}));
    var versions=await new TerrariaModule(downloads).VersionsAsync();
    Check(versions.Count==1 && versions[0].Id=="1.4.5.8","Deduplicated official release mapping");
    Downloads.ValidateUri(new Uri(versions[0].DownloadUrl));
    Reject(()=>Downloads.ValidateUri(new Uri("https://terraria.org/evil.exe")));
    Reject(()=>Downloads.ValidateUri(new Uri("https://terraria.org/api/download/pc-dedicated-server/evil.zip")));
    Reject(()=>Downloads.ValidateUri(new Uri("https://terraria.org:444/api/get/dedicated-servers-names")));
});
await Test("Terraria ZIP validates before writing and preserves saved state", () =>
{
    var folder=Path.Combine(root,"terraria-extract"); Directory.CreateDirectory(Path.Combine(folder,"worlds"));
    File.WriteAllText(Path.Combine(folder,"worlds/world.wld"),"KEEP_WORLD");
    File.WriteAllText(Path.Combine(folder,"serverconfig.txt"),"KEEP_CONFIG");
    var zip=Archive("terraria-good.zip",("1458/Windows/TerrariaServer.exe","MZfixture"),("1458/Windows/ReLogic.Native.dll","MZfixture"),("1458/Windows/start-server.bat","NEVER_RUN"));
    TerrariaModule.ExtractOfficial(zip,folder,"1458");
    Check(File.ReadAllText(Path.Combine(folder,"worlds/world.wld"))=="KEEP_WORLD" && File.ReadAllText(Path.Combine(folder,"serverconfig.txt"))=="KEEP_CONFIG","World/config preserved");
    Check(!File.Exists(Path.Combine(folder,"start-server.bat")),"Scripts not extracted");
    var bad=Archive("terraria-bad.zip",("1458/Windows/TerrariaServer.exe","MZfixture"),("1458/Windows/ReLogic.Native.dll","MZfixture"),("../escape","bad"));
    Reject(()=>TerrariaModule.ExtractOfficial(bad,Path.Combine(root,"terraria-reject"),"1458"));
    Check(!Directory.Exists(Path.Combine(root,"terraria-reject")),"No writes on traversal");
    var invalid=Archive("terraria-invalid.zip",("1458/Windows/TerrariaServer.exe","MZfixture"),("1458/Windows/ReLogic.Native.dll","not-a-PE"));
    Reject(()=>TerrariaModule.ExtractOfficial(invalid,Path.Combine(root,"terraria-invalid"),"1458"));
    return Task.CompletedTask;
});
await Test("Terraria config: private password, safe defaults, protected backups", async () =>
{
    var p=Terra();p.WorldPassword="TEST_PRIVATE_PASSWORD";store.WriteProperties(p);store.Save([p]);
    var folder=store.ServerRoot(p); var config=File.ReadAllText(Path.Combine(folder,"serverconfig.txt"));
    Check(config.Contains("upnp=0") && config.Contains("priority=3") && config.Contains("world="+Path.Combine(folder,"worlds/world.wld").Replace('/',Path.DirectorySeparatorChar)),"Safe native defaults");
    Check(!File.Exists(Path.Combine(folder,"eula.txt")),"No Minecraft EULA for Terraria");
    Check(!File.ReadAllText(Path.Combine(store.Root,"profiles.json")).Contains(p.WorldPassword),"Password not in profile JSON");
    Check(TerrariaModule.ReadPassword(folder)==p.WorldPassword,"Password round trip");
    var backup=await store.BackupAsync(p); using var zip=ZipFile.OpenRead(backup);Check(zip.GetEntry("serverconfig.txt")!=null,"Config backed up");
    if (OperatingSystem.IsWindows())
    {
        var acl=System.IO.FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(Path.GetDirectoryName(backup)!));
        Check(acl.AreAccessRulesProtected && acl.GetAccessRules(true,true,typeof(System.Security.Principal.SecurityIdentifier)).Count==1,"Backup folder private");
    }
});
await Test("Terraria native process: no Java/EULA, exit saves, password redaction and DLL integrity", async () =>
{
    var folder=Path.Combine(root,"terraria fixture process");Directory.CreateDirectory(folder);
    foreach(var file in Directory.GetFiles(AppContext.BaseDirectory))File.Copy(file,Path.Combine(folder,Path.GetFileName(file)),true);
    var executable=Path.Combine(folder,"TerrariaServer.exe");File.Copy(Environment.ProcessPath!,executable,true);
    File.WriteAllText(Path.Combine(folder,"ReLogic.Native.dll"),"MZfixture");
    var p=Terra();p.Installed=true;p.WorldPassword="port";
    foreach(var name in TerrariaModule.Binaries)p.NativeFileHashes[name]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder,name))));
    TerrariaModule.WriteConfig(p,folder);
    using var runner=new ServerProcess();
    var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var stopped=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var echoed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var logs=new System.Collections.Concurrent.ConcurrentQueue<string>();
    runner.StateChanged+=state=>{if(state==ServerState.Running)ready.TrySetResult();if(state==ServerState.Stopped)stopped.TrySetResult();};
    runner.Log+=entry=>{logs.Enqueue(entry.Text);if(entry.Text.Contains("received:say TEST_MESSAGE"))echoed.TrySetResult();};
    try
    {
        await runner.StartAsync(p,folder);await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        runner.Message("private="+p.WorldPassword);
        await RejectAsync(()=>runner.SendAsync("password CHANGED_SECRET"));
        await RejectAsync(()=>runner.SendAsync("say \u001b[2J"));
        await runner.SendAsync("say TEST_MESSAGE");await echoed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(await runner.StopAsync(TimeSpan.FromSeconds(10)),"exit command accepted");await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(File.Exists(Path.Combine(folder,"exit-marker")),"Stop used exit, not Minecraft stop");
        Check(logs.All(l=>!l.Contains(p.WorldPassword) && !l.Contains("CHANGED_SECRET")),"Console output redacted; private console changes blocked");
        File.AppendAllText(Path.Combine(folder,"ReLogic.Native.dll"),"tampered");
        await RejectAsync(()=>runner.StartAsync(p,folder));Check(!runner.IsActive,"Changed DLL never executed");
    }
    finally { if(runner.IsActive){runner.ForceStop();await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));} }
});
await Test("Terraria completion uses native console vocabulary", () =>
{
    Check(CommandAssist.Suggest("pl",2,edition:"Terraria").Single().Text=="playing","Native players command");
    Check(CommandAssist.Suggest("ex",2,edition:"Terraria").Single().Text=="exit","Save-and-exit");
    Check(CommandAssist.Suggest("stop",4,edition:"Terraria").Count==0 && CommandAssist.Suggest("whi",3,edition:"Terraria").Count==0,"No Minecraft-specific commands");
    return Task.CompletedTask;
});

ServerProfile Modded() { var p=Terra();p.Engine="tModLoader";p.MinecraftVersion="v2026.08.3.0";p.LaunchJar="engine/tModLoader.dll";return p; }
string Tmod(string filename,string name="FixtureMod",bool corrupt=false)
{
    var path=Path.Combine(root,filename);using var payload=new MemoryStream();
    using(var writer=new BinaryWriter(payload,Encoding.UTF8,true)){writer.Write(name);writer.Write("1.2.3");writer.Write(0);}
    var data=payload.ToArray();var hash=SHA1.HashData(data);if(corrupt)hash[0]^=1;
    using var output=new BinaryWriter(File.Create(path));output.Write(Encoding.ASCII.GetBytes("TMOD"));output.Write("2026.8.3.0");output.Write(hash);output.Write(new byte[256]);output.Write(data.Length);output.Write(data);return path;
}
await Test("tModLoader: headers use internal names, bounded fields and embedded integrity",()=>
{
    var path=Tmod("different-filename.tmod");var mod=TModLoaderModule.ReadMod(path);
    Check(mod.Name=="FixtureMod"&&mod.Version=="1.2.3"&&mod.Sha256.Length==64,"Internal identity read");
    Reject(()=>TModLoaderModule.ReadMod(Tmod("corrupt.tmod",corrupt:true)));
    Reject(()=>TModLoaderModule.ReadMod(Tmod("traversal.tmod","../evil")));
    File.WriteAllText(Path.Combine(root,"notmod.tmod"),new string('x',400));Reject(()=>TModLoaderModule.ReadMod(Path.Combine(root,"notmod.tmod")));
    return Task.CompletedTask;
});
await Test("tModLoader: imports are isolated, duplicates rejected, enabled list and tamper detection",()=>
{
    var p=Modded();var folder=store.ServerRoot(p);Directory.CreateDirectory(folder);
    var a=Tmod("a.tmod");var b=Tmod("b.tmod","SecondMod");p.Mods=TModLoaderModule.ImportMods(p,folder,[a,b]);
    TModLoaderModule.VerifyMods(p,folder);Check(File.Exists(Path.Combine(folder,"mods/FixtureMod.tmod")),"Canonical owned copy");
    var enabled=JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(folder,"mods/enabled.json")))!;Check(enabled.SequenceEqual(new[]{"FixtureMod","SecondMod"}),"Explicit enabled names");
    Reject(()=>TModLoaderModule.ImportMods(p,folder,[a,a]));
    var another=store.ServerRoot(Modded());Check(!Directory.Exists(Path.Combine(another,"mods")),"Profiles never share mod folders");
    File.AppendAllText(Path.Combine(folder,"mods/FixtureMod.tmod"),"changed");Reject(()=>TModLoaderModule.VerifyMods(p,folder));return Task.CompletedTask;
});
await Test("Calamity: both main mod and required music must be present",()=>
{
    var p=Modded();p.TerrariaPreset="Calamity";var folder=store.ServerRoot(p);Directory.CreateDirectory(folder);
    p.Mods=TModLoaderModule.ImportMods(p,folder,[Tmod("calamity.tmod","CalamityMod")]);Reject(()=>TModLoaderModule.VerifyMods(p,folder));
    p.Mods=TModLoaderModule.ImportMods(p,folder,[Tmod("music.tmod","CalamityModMusic")]);TModLoaderModule.VerifyMods(p,folder);return Task.CompletedTask;
});
await Test("tModLoader ZIP: preflight rejects escapes, duplicate paths and bundled startup scripts",()=>
{
    var good=Archive("tml-good.zip",("tModLoader.dll","MZfixture"),("tModLoader.runtimeconfig.json","{}"),("LaunchUtils/busybox.exe","NEVER_RUN"),("start.bat","NEVER_RUN"),("cli-argsConfig.txt","-world BAD"));
    var folder=Path.Combine(root,"tml-extract");Directory.CreateDirectory(folder);TModLoaderModule.Extract(good,folder,true);
    Check(File.Exists(Path.Combine(folder,"tModLoader.dll"))&&!File.Exists(Path.Combine(folder,"start.bat"))&&!File.Exists(Path.Combine(folder,"cli-argsConfig.txt")),"Only runtime content extracted");
    Reject(()=>TModLoaderModule.Extract(Archive("tml-escape.zip",("../escape","BAD")),Path.Combine(root,"tml-escape"),true));
    Reject(()=>TModLoaderModule.Extract(Archive("tml-duplicate.zip",("same","a"),("SAME","b")),Path.Combine(root,"tml-duplicate"),true));return Task.CompletedTask;
});
await Test("tModLoader routes: only official release and Microsoft runtime downloads allowed",()=>
{
    Downloads.ValidateUri(new(TModLoaderModule.ReleasesUrl));Downloads.ValidateUri(new("https://github.com/tModLoader/tModLoader/releases/download/v2026.08.3.0/tModLoader.zip"));
    Downloads.ValidateUri(new("https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/8.0/releases.json"));
    Reject(()=>Downloads.ValidateUri(new("https://github.com/attacker/tModLoader/releases/download/v2026.08.3.0/tModLoader.zip")));
    Reject(()=>Downloads.ValidateUri(new("https://release-assets.githubusercontent.com/arbitrary.exe")));
    Reject(()=>Downloads.ValidateUri(new("https://builds.dotnet.microsoft.com/unrelated.exe")));return Task.CompletedTask;
});
await Test("tModLoader config and launch: private saves, absolute mods, no scripts or Steam login",()=>
{
    var p=Modded();var folder=store.ServerRoot(p);TerrariaModule.WriteConfig(p,folder);var launch=TModLoaderModule.Arguments(folder);
    Check(launch.Contains("-server")&&launch.Contains("-nosteam")&&launch.Contains("-tmlsavedirectory")&&launch.Contains(Path.Combine(folder,"mods")),"Dedicated scoped arguments");
    Check(!launch.Any(x=>x.EndsWith(".bat")),"No shell scripts");Check(!File.Exists(Path.Combine(folder,"eula.txt")),"No Java requirements");return Task.CompletedTask;
});
if(args.Contains("--tml-live") && failures==0)
{
    await Test("official tModLoader: install private runtime, world, TCP, commands, graceful save and reload",async()=>
    {
        using var downloads=new Downloads();var choices=await new TModLoaderModule(downloads).VersionsAsync();var p=Modded();p.MinecraftVersion=choices[0].Id;
        var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();p.Port=((System.Net.IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        p.WorldSeed="12345";var folder=store.ServerRoot(p);Console.WriteLine("Live tModLoader profile: "+folder);
        await new Installer(downloads).InstallAsync(p,folder,new Progress<string>(Console.WriteLine));ProfileStore.Validate(p);TerrariaModule.WriteConfig(p,folder);
        var exampleIndex=Array.IndexOf(args,"--example-mod");
        if(exampleIndex>=0 && args.Length>exampleIndex+1)
        {
            p.Mods=TModLoaderModule.ImportMods(p,folder,[Path.GetFullPath(args[exampleIndex+1])]);
            Check(p.Mods.Single().Name=="ExampleMod","Real official mod parsed and imported");TerrariaModule.WriteConfig(p,folder);
        }
        using var runner=new ServerProcess();var logs=new System.Collections.Concurrent.ConcurrentQueue<string>();runner.Log+=entry=>{logs.Enqueue(entry.Text);};
        async Task WaitState(ServerState target,int seconds){using var timer=new CancellationTokenSource(TimeSpan.FromSeconds(seconds));while(runner.State!=target){if(target==ServerState.Running&&runner.State==ServerState.Stopped)throw new Exception("Server exited: "+string.Join("\n",logs.TakeLast(15)));timer.Token.ThrowIfCancellationRequested();await Task.Delay(100,timer.Token);}}
        try
        {
            await runner.StartAsync(p,folder);await WaitState(ServerState.Running,300);
            if(p.Mods.Count>0)Check(logs.Any(l=>l.Contains("Adding Content: Example Mod")),"Real mod appeared in loader output");
            using(var client=new System.Net.Sockets.TcpClient()){await client.ConnectAsync(IPAddress.Loopback,p.Port);Check(client.Connected,"Local tML TCP reachable");}
            await runner.SendAsync("playing");await runner.SendAsync("save");Check(await runner.StopAsync(TimeSpan.FromSeconds(90)),"Graceful exit accepted");await WaitState(ServerState.Stopped,20);
            Check(File.Exists(Path.Combine(folder,"worlds/world.wld")),"Profile world saved");Check(File.Exists(Path.Combine(folder,"worlds/world.twld")),"Modded world metadata saved");
            logs.Clear();await runner.StartAsync(p,folder);await WaitState(ServerState.Running,90);Check(await runner.StopAsync(TimeSpan.FromSeconds(90)),"Reload and exit");await WaitState(ServerState.Stopped,20);
            var backup=await store.BackupAsync(p);using var zip=ZipFile.OpenRead(backup);
            Check(zip.GetEntry("worlds/world.wld")!=null&&zip.GetEntry("worlds/world.twld")!=null,"Both world formats backed up");
            if(p.Mods.Count>0)Check(zip.GetEntry("mods/ExampleMod.tmod")!=null&&zip.GetEntry("mods/enabled.json")!=null,"Real mod and enabled list included in backup");
        }
        finally {if(runner.IsActive){runner.ForceStop();await WaitState(ServerState.Stopped,20);}File.WriteAllLines(Path.Combine(root,"tmodloader-live.log"),logs);}
    });
}

if(args.Contains("--terraria-live") && failures == 0)
{
    await Test("official Terraria: download, generate world, local TCP, save, reload, backup",async()=>
    {
        using var downloads=new Downloads();var module=new TerrariaModule(downloads);var versions=await module.VersionsAsync();
        var p=Terra();p.MinecraftVersion=versions[0].Id;p.WorldName="NandoHost test world";p.WorldSeed="12345";p.WorldPassword="TEST_"+Guid.NewGuid().ToString("N");
        var listener=new System.Net.Sockets.TcpListener(IPAddress.Loopback,0);listener.Start();p.Port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();
        var folder=store.ServerRoot(p);Directory.CreateDirectory(folder);
        await new Installer(downloads).InstallAsync(p,folder,new Progress<string>(Console.WriteLine));store.WriteProperties(p);
        using var runner=new ServerProcess();var logs=new System.Collections.Concurrent.ConcurrentQueue<string>();
        runner.Log+=entry=>{logs.Enqueue(entry.Text);if(entry.Text.Contains("Listening")||entry.Error)Console.WriteLine(entry.Text);};
        async Task WaitState(ServerState state,int seconds)
        {
            using var timer=new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            while(runner.State!=state){timer.Token.ThrowIfCancellationRequested();await Task.Delay(100,timer.Token);}
        }
        try
        {
            await runner.StartAsync(p,folder);await WaitState(ServerState.Running,240);
            using(var client=new System.Net.Sockets.TcpClient()){await client.ConnectAsync(IPAddress.Loopback,p.Port);Check(client.Connected,"Local Terraria TCP reachable");}
            await runner.SendAsync("playing");await runner.SendAsync("save");
            Check(await runner.StopAsync(TimeSpan.FromSeconds(90)),"Official server saves and exits");await WaitState(ServerState.Stopped,15);
            var world=Path.Combine(folder,"worlds/world.wld");Check(File.Exists(world)&&new FileInfo(world).Length>1024,"World saved inside profile");
            logs.Clear();await runner.StartAsync(p,folder);await WaitState(ServerState.Running,60);
            Check(!logs.Any(l=>l.Contains("Generating terrain",StringComparison.OrdinalIgnoreCase)),"Existing world reused");
            Check(await runner.StopAsync(TimeSpan.FromSeconds(90)),"Second graceful stop");await WaitState(ServerState.Stopped,15);
            var backup=await store.BackupAsync(p);using var zip=ZipFile.OpenRead(backup);Check(zip.GetEntry("worlds/world.wld")!=null,"World included in backup");
            File.WriteAllLines(Path.Combine(root,"terraria-live.log"),logs);
        }
        finally {if(runner.IsActive){runner.ForceStop();await WaitState(ServerState.Stopped,15);}}
    });
}

if (args.Contains("--editions-online"))
{
    await Test("online Java release catalog includes multiple selectable server versions", async () =>
    {
        using var downloads=new Downloads();var list=await new ReleaseCatalog(downloads).VersionsAsync("Java");
        Check(list.Count>20 && list.Any(v=>v.Id=="1.20.1") && list.Any(v=>v.Id=="1.21.1"),"Live Java choices");
    });
    await Test("online official Bedrock stable installation without starting the server", async () =>
    {
        using var downloads=new Downloads();var list=await new ReleaseCatalog(downloads).VersionsAsync("Bedrock");
        var version=list.First(v=>!v.Preview);
        var p=new ServerProfile{Edition="Bedrock",Engine="Bedrock",Port=19132,MinecraftVersion=version.Id};
        var folder=Path.Combine(root,"official-bedrock");await new Installer(downloads).InstallAsync(p,folder);
        Check(p.Installed && p.RequiredJava==0 && p.NativeExecutableSha256.Length==64 && !p.EulaAccepted,"Native download validated, EULA not accepted");
        Check(File.ReadAllBytes(Path.Combine(folder,"bedrock_server.exe")).Take(2).SequenceEqual(new byte[]{77,90}),"Windows executable header");
        store.Save(new[]{p});
    });
    await Test("online crossplay catalog and verified Fabric/Geyser/Floodgate installation", async () =>
    {
        using var downloads=new Downloads();var list=await new ReleaseCatalog(downloads).VersionsAsync("Crossplay");
        var p=new ServerProfile{Edition="Crossplay",Engine="Fabric",MinecraftVersion=list[0].Id};
        var folder=Path.Combine(root,"official-crossplay");await new Installer(downloads).InstallAsync(p,folder);
        Check(p.Installed && p.RequiredJava>=21 && !p.EulaAccepted,"Compatible Java requirement and explicit EULA");
        foreach(var mod in new[]{"Geyser-Fabric.jar","floodgate-fabric.jar","fabric-api.jar"})Check(File.Exists(Path.Combine(folder,"mods",mod)),"Verified component: "+mod);
        Check(File.Exists(Path.Combine(folder,"config/Geyser-Fabric/config.yml")),"Bridge config installed");
    });
}
if (args.Contains("--online"))
{
    await Test("online Mojang: release manifest + verified official server download", async () =>
    {
        using var downloads = new Downloads(); var installer = new Installer(downloads);
        var versions = await installer.VersionsAsync(); Check(versions.Contains("1.21.1"), "Minecraft releases loaded");
        var p = new ServerProfile { MinecraftVersion = "1.20.1" };
        var folder = Path.Combine(root, "official-server");
        await installer.InstallAsync(p, folder);
        Check(File.Exists(Path.Combine(folder, "server.jar")) && p.RequiredJava == 17, "Java 1.20.1 JAR installed and Java 17 read from metadata");
    });
    await Test("online Modrinth: catalog and version metadata", async () =>
    {
        using var downloads = new Downloads(); var packs = new Modpacks(downloads);
        var hits = await packs.SearchAsync("fabric"); Check(hits.Count > 0, "Search results");
        var versions = await packs.VersionsAsync(hits[0].Id); Check(versions.Count > 0, "Versions loaded");
    });
    await Test("online Fabric: official bootstrap download", async () =>
    {
        using var downloads = new Downloads();
        var p = new ServerProfile { MinecraftVersion = "1.21.1", Engine = "Fabric" };
        var folder = Path.Combine(root, "fabric-server");
        await new Installer(downloads).InstallAsync(p, folder);
        Check(File.Exists(Path.Combine(folder, "fabric-server-launch.jar")) && p.LoaderVersion.Length > 0, "Fabric bootstrap installed");
    });
    await Test("installed Java runtime detection", async () =>
    {
        var version = await ServerProcess.CheckJavaAsync("java"); Check(version >= 21, "Installed Java recognized");
    });
}
results.Add($"{results.Count} checks, {failures} failures");
File.WriteAllLines(Path.Combine(root, "test-results.txt"), results);
Console.WriteLine(results[^1]);
return failures == 0 ? 0 : 1;

sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
}
