using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using NandoHost.Core;
using static NandoHost.Core.Localizer;

namespace NandoHost.App;

public partial class MainWindow
{
    private string currentGame = "Minecraft";
    private ListCollectionView serverView = null!;
    private ServerProfile NewTerrariaProfile() => new()
    {
        Game = "Terraria", Name = Tr("Meu Terraria ") + (profiles.Count(p => p.IsTerraria) + 1), Edition = "PC", Engine = "Terraria",
        MinecraftVersion = "1.4.5.8", Port = NextPort("Terraria"), MaxPlayers = 8, Whitelist = false,
        LaunchJar = "TerrariaServer.exe", RequiredJava = 0
    };
    private bool SwitchGame(string game, ServerProfile? target = null)
    {
        if (game is not ("Minecraft" or "Terraria")) return false;
        if (target == null && selected?.Game == game && currentGame == game) return true;
        if (target?.Id == selected?.Id && currentGame == game) return true;
        if (busy || runner.IsActive || tunnel.IsActive)
        {
            SetStatus(() => Tr("Pare o servidor e o agente e conclua a tarefa antes de trocar de jogo ou perfil.")); return false;
        }
        target ??= profiles.FirstOrDefault(p => p.Game == game);
        if (target == null)
        {
            target = game == "Terraria" ? NewTerrariaProfile() : new ServerProfile { Port = NextPort() };
            profiles.Add(target); store.Save(profiles);
        }
        currentGame = game; serverView.Refresh(); ServerList.SelectedItem = target;
        return selected?.Id == target.Id;
    }
    private void RefreshGameLibrary()
    {
        var entries = profiles.Where(p => p.Game == homeGame).ToList();
        HomeServerShortcuts.ItemsSource = entries.Take(3).ToList();
        LocalizedUi.Set(HomeProfileCount, "Text", () => entries.Count > 3 ? Tr("3 de ") + entries.Count + Tr(" perfis") : entries.Count == 1 ? Tr("1 perfil") : entries.Count + Tr(" perfis"));
        LocalizedUi.Set(HomeProfileCount, "ToolTip", () => entries.Count > 3 ? Tr("Abra o jogo para acessar todos os perfis.") : null);
        RefreshLibraryCovers();
        var same = selected?.Game == homeGame;
        HomeManageButton.IsEnabled = same || !busy && !runner.IsActive && !tunnel.IsActive;
        LocalizedUi.Set(MinecraftGameAction, "Text", () => runner.IsActive && same ? Tr("Voltar ao servidor  →") : Tr("Abrir servidores  →"));
        LocalizedUi.Set(HomeManageButton, "ToolTip", () => HomeManageButton.IsEnabled ? Tr("Abre o painel sem iniciar um servidor.") : Tr("Pare o servidor e o agente antes de trocar de jogo."));
        System.Windows.Automation.AutomationProperties.SetName(HomeManageButton, Tr("Abrir servidores de ") + homeGame);
    }
    private void LoadTerrariaFields(ServerProfile p)
    {
        WorldNameBox.Text = p.WorldName; WorldSeedBox.Text = p.WorldSeed;
        WorldSizeBox.SelectedIndex = p.WorldSize - 1; WorldDifficultyBox.SelectedIndex = p.WorldDifficulty;
        p.WorldPassword = p.IsTerraria ? TerrariaModule.ReadPassword(store.ServerRoot(p)) : "";
        WorldPasswordBox.Password = p.WorldPassword;
    }
    private void ApplyGameLayout()
    {
        var terraria = selected?.IsTerraria == true;
        var show = terraria ? Visibility.Visible : Visibility.Collapsed;
        var minecraft = terraria ? Visibility.Collapsed : Visibility.Visible;
        WorldFields.Visibility = show; WorldFields.IsEnabled = !busy && !runner.IsActive && !tunnel.IsActive;
        var hasWorld = terraria && selected != null && File.Exists(SafePaths.Inside(store.ServerRoot(selected), "worlds/world.wld"));
        WorldGenerationFields.IsEnabled = selected?.Installed != true && !hasWorld;
        if (terraria && hasWorld) VersionBox.IsEnabled = false;
        EditionFields.Visibility = minecraft;
        foreach (var element in new FrameworkElement[] { RamCaption, RamBox, ViewCaption, ViewBox, SimulationCaption, SimulationBox,
            EngineCaption, EngineBox, WhitelistCheck, WhitelistHelp, EulaCheck, EulaLink, AccessCaption, AccessSeparator })
            element.Visibility = minecraft;
        if (!terraria)
        {
            RamBox.Visibility = DraftEdition == "Bedrock" ? Visibility.Collapsed : Visibility.Visible;
        }
        JavaFields.Visibility = terraria || DraftEdition == "Bedrock" ? Visibility.Collapsed : Visibility.Visible;
        PacksNav.Visibility = Visibility.Visible;
        LocalizedUi.Set(PacksNav, "Content", () => terraria ? "▦    Mods" : "▦    Modpacks");
        MinecraftPacksPanel.Visibility = minecraft; TerrariaModsPanel.Visibility = show;
        ApplyGameTheme(); RefreshTerrariaMods();
        LocalizedUi.Set(VersionCaption, "Text", () => selected?.IsTModLoader == true ? Tr("Versão do tModLoader") : terraria ? Tr("Versão do Terraria (PC)") : Tr("Versão do Minecraft"));
        System.Windows.Automation.AutomationProperties.SetName(VersionBox, VersionCaption.Text);
        if (terraria) LocalizedUi.Set(PortCaption, "Text", () => Tr("Porta Terraria (TCP)"));
        GameBrandIcon.Source = (ImageSource)FindResource(terraria ? "TerrariaGameImage" : "MinecraftGameImage");
        LocalizedUi.Set(GameBrandIcon, "ToolTip", () => terraria ? "Terraria" : "Minecraft");
        System.Windows.Automation.AutomationProperties.SetName(GameBrandIcon, terraria ? "Terraria" : "Minecraft");
        BannerViewport.Background = (Brush)FindResource(terraria ? selected?.TerrariaPreset == "Calamity" && selected.IsTModLoader ? "CalamitySceneBrush" : "TerrariaSceneBrush" : "LandscapeBrush");
        PageTitle.FontFamily = terraria ? (FontFamily)FindResource("TerrariaFont") : (FontFamily)FindResource("MinecraftFont");
        GameEntryImage.Source = GameBrandIcon.Source;
        LocalizedUi.Set(GameEntryTitle, "Text", () => selected?.IsTModLoader == true && selected.TerrariaPreset == "Calamity" ? "Calamity" : terraria ? "Terraria" : "Minecraft");
        HeroTitle.FontFamily = terraria ? (FontFamily)FindResource("TerrariaFont") : (FontFamily)FindResource("MinecraftFont");
        LocalizedUi.Set(NativeInfoText, "Text", () => selected?.IsTModLoader == true ? Tr("tModLoader para PC. Mods e mundos isolados neste perfil; seus amigos precisam das mesmas versões.") : Tr("Terraria Vanilla para PC. Cada perfil tem um mundo próprio. Não utiliza Java."));
        GameEntryTitle.FontFamily = PageTitle.FontFamily;
        System.Windows.Automation.AutomationProperties.SetName(GameEntryOverlay, Tr("Entrando no painel de ") + (selected?.Game ?? Tr("jogo")));
        GameEntryLandscape.Background = BannerViewport.Background;
        if (terraria)
        {
            var terrariaProfile = selected!;
            LocalJavaPanel.Visibility = Visibility.Collapsed; LocalBedrockPanel.Visibility = Visibility.Visible;
            LocalizedUi.Set(LocalBedrockAddress, "Text", () => Tr("127.0.0.1 · porta ") + terrariaProfile.Port);
            BedrockLocalHint.Visibility = Visibility.Collapsed;
            LocalizedUi.Set(PublicAddressLabel, "Text", () => Tr("Endereço externo Terraria (TCP)"));
            LocalizedUi.Set(TunnelTargetText, "Text", () => TrFormat("Terraria → este agente → 127.0.0.1:{0} (TCP).", terrariaProfile.Port));
            LocalizedUi.Set(TunnelPlanText, "Text", () => Tr("No playit, escolha o preset Terraria do plano gratuito e a porta local acima. O NandoHost não contrata planos. Seus amigos usam o endereço e a porta públicos com a mesma versão Terraria para PC."));
            if (terrariaProfile.IsTModLoader) LocalizedUi.Set(TunnelPlanText, "Text", () => Tr("No playit, escolha o preset Terraria gratuito e a porta local acima. Seus amigos entram pelo tModLoader usando o endereço e a porta públicos, com versões compatíveis do tModLoader e dos mods. O NandoHost não contrata planos."));
            LocalizedUi.Set(InstalledText, "Text", () => terrariaProfile.Installed ? terrariaProfile.IsTModLoader ? Tr("tModLoader instalado · runtime privado") : Tr("Terraria dedicado instalado · sem Java") : Tr("Ainda não instalado"));
            LocalizedUi.Set(MemoryDetail, "Text", () => Tr("Uso real do servidor nativo; RAM automática"));
        }
        LocalizedUi.Set(LocalNativeCaption, "Text", () => terraria ? Tr("TERRARIA · ENDEREÇO E PORTA SEPARADOS") : Tr("BEDROCK · PREENCHA ENDEREÇO E PORTA SEPARADAMENTE"));
    }
    private ServerProfile SaveTerrariaSettings()
    {
        if (selected == null || !selected.IsTerraria || runner.IsActive)
            throw new InvalidOperationException(Tr("Pare o servidor para alterar as configurações."));
        if (versionLoading || VersionBox.SelectedItem is not ServerVersion version)
            throw new InvalidOperationException(Tr("Aguarde o catálogo e escolha uma versão Terraria disponível."));
        var p = JsonSerializer.Deserialize<ServerProfile>(JsonSerializer.Serialize(selected))!;
        p.Name = NameBox.Text.Trim(); p.MinecraftVersion = version.Id;
        if (!int.TryParse(PortBox.Text, out var port) || !int.TryParse(PlayersBox.Text, out var players))
            throw new InvalidDataException(Tr("Preencha porta e jogadores com números inteiros."));
        p.Port = port; p.MaxPlayers = players;
        if (tunnel.IsActive && p.Port != selected.Port)
            throw new InvalidOperationException(Tr("Pare o agente antes de alterar a porta local do túnel."));
        p.WorldName = WorldNameBox.Text.Trim(); p.WorldSeed = WorldSeedBox.Text.Trim();
        p.WorldSize = WorldSizeBox.SelectedIndex + 1; p.WorldDifficulty = WorldDifficultyBox.SelectedIndex;
        p.WorldPassword = WorldPasswordBox.Password;
        var existingWorld = File.Exists(SafePaths.Inside(store.ServerRoot(selected), "worlds/world.wld"));
        if ((selected.Installed || existingWorld) && (p.MinecraftVersion != selected.MinecraftVersion || p.WorldName != selected.WorldName ||
            p.WorldSeed != selected.WorldSeed || p.WorldSize != selected.WorldSize || p.WorldDifficulty != selected.WorldDifficulty))
            throw new InvalidOperationException(Tr("Crie outro perfil para trocar a versão ou gerar outro mundo. O mundo atual será preservado."));
        ProfileStore.Validate(p);
        var index = profiles.IndexOf(selected); var replacement = profiles.ToList(); replacement[index] = p;
        store.WriteProperties(p); store.Save(replacement); profiles[index] = p; selected = p; ServerList.SelectedItem = p;
        return p;
    }
    internal async Task PreviewTerrariaFlowAsync()
    {
        if (!smoke) return;
        var minecraftIds = profiles.Where(p => !p.IsTerraria).Select(p => p.Id).ToList();
        var minecraftSettings = JsonSerializer.Serialize(profiles.Where(p => !p.IsTerraria));
        SelectHomeGame("Terraria"); MinecraftGameClick(this, new RoutedEventArgs());
        await Task.Delay(750);
        if (selected?.IsTerraria != true || Pages.SelectedIndex != 0 || JavaFields.Visibility != Visibility.Collapsed || EulaCheck.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("A entrada no Terraria ainda apresenta requisitos Minecraft.");
        if (serverView.Cast<ServerProfile>().Any(p => !p.IsTerraria)) throw new InvalidOperationException("O painel misturou perfis de jogos.");
        if (allowSmokeCatalog) await ReloadVersionsAsync();
        WorldNameBox.Text = "Mundo de teste"; WorldSeedBox.Text = "12345"; WorldSizeBox.SelectedIndex = 0;
        WorldDifficultyBox.SelectedIndex = 1; WorldPasswordBox.Password = "TEST_PRIVATE_PASSWORD";
        var profile = SaveSettings();
        if (File.ReadAllText(Path.Combine(store.Root, "profiles.json")).Contains("TEST_PRIVATE_PASSWORD"))
            throw new InvalidOperationException("A senha vazou para o JSON de perfis.");
        if (profile.WorldDifficulty != 1 || TerrariaModule.ReadPassword(store.ServerRoot(profile)) != "TEST_PRIVATE_PASSWORD")
            throw new InvalidOperationException("As opções Terraria não foram salvas.");
        SelectHomeGame("Minecraft"); if (!SwitchGame("Minecraft")) throw new InvalidOperationException("Não retornou ao Minecraft.");
        if (!minecraftIds.SequenceEqual(profiles.Where(p => !p.IsTerraria).Select(p => p.Id))) throw new InvalidOperationException("Perfis Minecraft foram alterados.");
        if (minecraftSettings != JsonSerializer.Serialize(profiles.Where(p => !p.IsTerraria))) throw new InvalidOperationException("As configurações Minecraft foram alteradas.");
        SelectHomeGame("Terraria"); if (!SwitchGame("Terraria", profile)) throw new InvalidOperationException("Não reabriu o Terraria.");
        if (allowSmokeCatalog) await ReloadVersionsAsync();
        WorldPasswordBox.Clear(); profile = SaveSettings();
        if (allowSmokeCatalog) await ReloadVersionsAsync();
        if (runner.IsActive || tunnel.IsActive) throw new InvalidOperationException("A revisão iniciou processos de rede.");
    }
}
