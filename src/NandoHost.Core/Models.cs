namespace NandoHost.Core;

public sealed class ServerProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Game { get; set; } = "Minecraft";
    public string WorldName { get; set; } = "Mundo Terraria";
    public int WorldSize { get; set; } = 1;
    public int WorldDifficulty { get; set; }
    public string WorldSeed { get; set; } = "";
    public string TerrariaPreset { get; set; } = "Custom";
    public List<TerrariaMod> Mods { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public bool IsTModLoader => IsTerraria && Engine == "tModLoader";
    [System.Text.Json.Serialization.JsonIgnore] public string WorldPassword { get; set; } = "";
    public Dictionary<string, string> NativeFileHashes { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public bool IsTerraria => Game == "Terraria";
    [System.Text.Json.Serialization.JsonIgnore] public bool IsNative => IsBedrock || IsTerraria;
    public string Name { get; set; } = "Meu servidor";
    public string MinecraftVersion { get; set; } = "1.21.1";
    public string Engine { get; set; } = "Vanilla";
    public string Edition { get; set; } = "Java";
    public int BedrockPort { get; set; } = 19132;
    public string BedrockPublicAddress { get; set; } = "";
    public string NativeExecutableSha256 { get; set; } = "";
    public bool BedrockPreview { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public bool IsBedrock => !IsTerraria && Edition == "Bedrock";
    [System.Text.Json.Serialization.JsonIgnore] public bool IsCrossplay => !IsTerraria && Edition == "Crossplay";
    [System.Text.Json.Serialization.JsonIgnore] public string EditionLabel => IsTModLoader ? (TerrariaPreset == "Calamity" ? "Calamity" : "tModLoader") : IsTerraria ? "Terraria PC" : IsCrossplay ? "Java + Bedrock" : Edition;
    public string LoaderVersion { get; set; } = "";
    public int MemoryMb { get; set; } = 4096;
    public int Port { get; set; } = 25565;
    public int ViewDistance { get; set; } = 8;
    public int SimulationDistance { get; set; } = 6;
    public int MaxPlayers { get; set; } = 10;
    public bool Whitelist { get; set; } = true;
    public bool EulaAccepted { get; set; }
    public string JavaPath { get; set; } = "java";
    public int RequiredJava { get; set; } = 21;
    public string LaunchJar { get; set; } = "server.jar";
    public bool Installed { get; set; }
    public string PackName { get; set; } = "";
    public string PublicAddress { get; set; } = "";
    public override string ToString() => Name;
}

public record PackHit(string Id, string Title, string Description, int Downloads, string Slug);
public record PackVersion(string Id, string Name, string Number, string Minecraft, string Loader)
{
    public override string ToString() => $"{Name} · {Minecraft} · {Loader}";
}
public record LogEntry(DateTime Time, string Text, bool Error = false)
{
    public override string ToString() => $"{Time:HH:mm:ss}  {Text}";
}

public enum ServerState { Stopped, Starting, Running, Stopping }
