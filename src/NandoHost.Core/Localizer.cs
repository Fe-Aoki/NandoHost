using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace NandoHost.Core;

public sealed class LanguageState
{
    public static LanguageState Current { get; } = new();
    public event EventHandler? Changed;
    internal void Notify() => Changed?.Invoke(this, EventArgs.Empty);
}

public static class Localizer
{
    private static readonly FrozenDictionary<string, string> English = Load();
    private static string cultureCode = "pt-BR";
    public static string CultureCode => Volatile.Read(ref cultureCode);
    public static IReadOnlyDictionary<string, string> EnglishCatalog => English;

    private static FrozenDictionary<string, string> Load()
    {
        using var stream = typeof(Localizer).Assembly.GetManifestResourceStream("NandoHost.Core.Localization.en-US.json")
            ?? throw new InvalidOperationException("Missing localization catalog.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public static string Normalize(string? code) => code?.StartsWith("pt", StringComparison.OrdinalIgnoreCase) == true ? "pt-BR" : "en-US";
    public static void SetCulture(string code)
    {
        var value = Normalize(code);
        if (CultureCode == value) return;
        Volatile.Write(ref cultureCode, value);
        LanguageState.Current.Notify();
    }
    public static string Tr(string text) => CultureCode == "en-US" && English.TryGetValue(text, out var translated) ? translated : text;
    public static string TrFormat(string format, params object?[] arguments) => string.Format(CultureInfo.CurrentCulture, Tr(format), arguments);
}

public sealed record UiPreferences(string Language);

public static class UiPreferencesStore
{
    public static string Load(string root, string? systemLanguage = null)
    {
        var fallback = Localizer.Normalize(systemLanguage ?? CultureInfo.CurrentUICulture.Name);
        var path = SafePaths.Inside(root, "ui-settings.json");
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 4096) return fallback;
            var preferences = JsonSerializer.Deserialize<UiPreferences>(File.ReadAllText(path));
            return preferences?.Language is "pt-BR" or "en-US" ? preferences.Language : fallback;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return fallback; }
    }
    public static void Save(string root, string language)
    {
        if (language is not ("pt-BR" or "en-US")) throw new ArgumentException("Unsupported language.", nameof(language));
        Directory.CreateDirectory(root);
        var path = SafePaths.Inside(root, "ui-settings.json");
        var temporary = SafePaths.Inside(root, ".ui-settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new UiPreferences(language), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
