using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using NandoHost.Core;
using static NandoHost.Core.Localizer;

namespace NandoHost.App;

public partial class MainWindow
{
    private string appliedGameTheme = "";
    private string GameSelectionColor => selected?.IsTerraria == true ? selected.IsTModLoader && selected.TerrariaPreset == "Calamity" ? "#633044" : "#304E85" : "#3F4933";
    private void ApplyGameTheme()
    {
        var key = selected?.IsTerraria == true ? selected.IsTModLoader && selected.TerrariaPreset == "Calamity" ? "Calamity" : "Terraria" : "Minecraft";
        if (appliedGameTheme == key) return;
        appliedGameTheme = key;
        var names = new[] { "Surface", "Muted", "Accent", "ButtonSurface", "PrimarySurface", "InputSurface", "InputBorder", "PopupSurface", "HoverSurface", "SelectionSurface", "SelectionBorder", "CardBorder", "SidebarSurface", "SidebarBorder", "TrackSurface", "ThumbSurface", "ThumbHover" };
        var colors = key == "Calamity"
            ? new[] { "#322333", "#C9B9C6", "#F0C68E", "#382336", "#E1AC62", "#211923", "#705166", "#3F2B40", "#503044", "#633044", "#B88275", "#685064", "#241B2A", "#574052", "#201822", "#795468", "#B0798D" }
            : new[] { "#23365F", "#BECDE4", "#F5D890", "#142B4B", "#E1BB63", "#101F3E", "#506F9D", "#243B66", "#2D4875", "#304E85", "#8CAAD2", "#4B648C", "#152442", "#3A5278", "#111F38", "#5274A5", "#82A7D4" };
        for (var i = 0; i < names.Length; i++)
            Resources[names[i]] = key == "Minecraft" ? Application.Current.Resources[names[i]] : new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
        Resources["ConsoleSurface"] = key == "Minecraft" ? Application.Current.Resources["ConsoleSurface"] : new SolidColorBrush((Color)ColorConverter.ConvertFromString(key == "Calamity" ? "#151019" : "#0C1528"));
        Resources["ConsoleText"] = key == "Minecraft" ? Application.Current.Resources["ConsoleText"] : new SolidColorBrush((Color)ColorConverter.ConvertFromString(key == "Calamity" ? "#DECCD7" : "#C7D9ED"));
        Resources["ConsoleCaption"] = key == "Minecraft" ? Application.Current.Resources["ConsoleCaption"] : Resources["Accent"];
        Resources["ActionButtonTemplate"] = Application.Current.Resources[key == "Minecraft" ? "ActionButtonTemplate" : "SkyActionButtonTemplate"];
        Resources["ActionButtonBorder"] = key == "Minecraft" ? Application.Current.Resources["ActionButtonBorder"] : (Brush)new BrushConverter().ConvertFromString(key == "Calamity" ? "#896477" : "#7493B8")!;
        Resources["PrimaryButtonBorder"] = key == "Minecraft" ? Application.Current.Resources["PrimaryButtonBorder"] : (Brush)new BrushConverter().ConvertFromString(key == "Calamity" ? "#F8D59F" : "#FFF0BA")!;
        Resources["PrimaryButtonText"] = key == "Minecraft" ? Application.Current.Resources["PrimaryButtonText"] : (Brush)new BrushConverter().ConvertFromString("#291E10")!;
        Resources["DisabledActionSurface"] = (Brush)new BrushConverter().ConvertFromString(key == "Calamity" ? "#2C202E" : "#182A43")!;
        Resources["DisabledActionBorder"] = (Brush)new BrushConverter().ConvertFromString(key == "Calamity" ? "#594252" : "#415777")!;
        Resources["DisabledActionText"] = (Brush)new BrushConverter().ConvertFromString(key == "Calamity" ? "#9E8B9A" : "#8191AA")!;
        Resources["PanelRadius"] = new CornerRadius(key == "Minecraft" ? 0 : 6);
        GameTexture.Visibility = Visibility.Visible;
        GameTexture.Background = (Brush)FindResource(key == "Minecraft" ? "DirtBrush" : key == "Calamity" ? "CalamitySkyBrush" : "TerrariaSkyBrush");
        GameTexture.Opacity = key == "Minecraft" ? 0.26 : 1;
        SettingsActions.Background = key == "Minecraft" ? (Brush)new BrushConverter().ConvertFromString("#24271F")! : (Brush)FindResource("Surface");
        SettingsActions.BorderBrush = key == "Minecraft" ? (Brush)new BrushConverter().ConvertFromString("#536046")! : (Brush)FindResource("CardBorder");
        GameEntryOverlay.Background = key == "Minecraft" ? (Brush)new BrushConverter().ConvertFromString("#11150F")! : (Brush)FindResource("ConsoleSurface");
        EditionBadge.Foreground = key == "Minecraft" ? (Brush)new BrushConverter().ConvertFromString("#C8D2B2")! : (Brush)FindResource("Muted");
        GameWorkspace.Background = key == "Minecraft" ? Brushes.Transparent : new SolidColorBrush((Color)ColorConverter.ConvertFromString(key == "Calamity" ? "#1D1722" : "#111E36"));
        SidebarNote.Background = key == "Minecraft" ? (Brush)FindResource("DirtBrush") : (Brush)FindResource("Surface");
        LocalizedUi.Set(SidebarNoteTitle, "Text", () => key == "Minecraft" ? Tr("DESDE A PRIMEIRA CABANA.") : key == "Calamity" ? Tr("UM NOVO DESAFIO ESPERA.") : Tr("DO PRIMEIRO ABRIGO AO HARDMODE."));
        LocalizedUi.Set(HeroTitle, "Text", () => key == "Minecraft" ? Tr("CADA MUNDO TEM\nUMA HISTÓRIA.") : key == "Calamity" ? Tr("Uma nova jornada.\nUm desafio maior.") : Tr("Cada mundo tem\numa história."));
        var buttons = new[] { DashboardNav, PacksNav, NetworkNav, SettingsNav };
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].Background = i == Pages.SelectedIndex ? (Brush)new BrushConverter().ConvertFromString(GameSelectionColor)! : Brushes.Transparent;
            buttons[i].BorderBrush = i == Pages.SelectedIndex ? (Brush)FindResource("Accent") : Brushes.Transparent;
        }
    }
    private bool CanEditTerrariaMods => selected?.IsTModLoader == true && !busy && !runner.IsActive && !tunnel.IsActive && !File.Exists(SafePaths.Inside(store.ServerRoot(selected), "worlds/world.wld"));
    private void RefreshTerrariaMods()
    {
        if (TerrariaModsPanel == null || selected == null) return;
        var unlocked = !busy && !runner.IsActive && !tunnel.IsActive;
        CreateCalamityButton.IsEnabled = CreateCustomButton.IsEnabled = selected.IsTerraria && unlocked;
        ImportTmodsButton.IsEnabled = CanEditTerrariaMods;
        RemoveTmodButton.IsEnabled = CanEditTerrariaMods && TerrariaModsList.SelectedItem is TerrariaMod;
        if (!ReferenceEquals(TerrariaModsList.ItemsSource, selected.Mods)) TerrariaModsList.ItemsSource = selected.Mods;
        LocalizedUi.Set(TerrariaModsHeading, "Text", () => selected.IsTModLoader ? "MODS · " + selected.Name : Tr("ESTE PERFIL É VANILLA"));
        LocalizedUi.Set(TerrariaModsHelp, "Text", () => !selected.IsTModLoader ? Tr("Crie um perfil Calamity ou personalizado acima para instalar tModLoader. O mundo Vanilla será preservado.")
            : CanEditTerrariaMods ? Tr("Importe os mods antes do primeiro início. O nome interno e a integridade de cada .tmod serão conferidos.")
            : busy || runner.IsActive || tunnel.IsActive ? Tr("Conclua a tarefa e pare o servidor e o agente para editar os mods.")
            : Tr("Este mundo já foi criado. Crie outro perfil para trocar os mods e preservar esta aventura."));
        var calamity = selected.Mods.Any(m => m.Name == "CalamityMod"); var music = selected.Mods.Any(m => m.Name == "CalamityModMusic");
        LocalizedUi.Set(TerrariaModsStatus, "Text", () => selected.IsTModLoader && selected.TerrariaPreset == "Calamity"
            ? (calamity ? "✓ Calamity Mod" : Tr("Falta Calamity Mod")) + "  ·  " + (music ? "✓ Calamity Mod Music" : Tr("Falta Calamity Mod Music"))
            : selected.IsTModLoader ? selected.Mods.Count + Tr(" mod(s) importado(s).") : Tr("Nenhum mod é carregado em servidores Vanilla."));
    }
    private async void CreateModdedClick(object sender, RoutedEventArgs e)
    {
        if (selected?.IsTerraria != true || busy || runner.IsActive || tunnel.IsActive || sender is not Button { Tag: string preset }) return;
        var p = NewTerrariaProfile(); p.Engine = "tModLoader"; p.TerrariaPreset = preset;
        p.Name = preset == "Calamity" ? Tr("Meu Calamity ") + (profiles.Count(x => x.IsTModLoader && x.TerrariaPreset == "Calamity") + 1) : Tr("Meu Terraria com mods ") + (profiles.Count(x => x.IsTModLoader) + 1);
        p.MinecraftVersion = "v2026.08.3.0"; p.LaunchJar = "engine/tModLoader.dll";
        profiles.Add(p); store.Save(profiles); ServerList.SelectedItem = p; Navigate(3);
        await ReloadVersionsAsync();
        SetStatus(() => Tr("Novo perfil tModLoader. Configure o mundo, baixe o servidor e importe os .tmod em Mods antes de ligar."));
    }
    private async void ImportTmodsClick(object sender, RoutedEventArgs e)
    {
        if (!CanEditTerrariaMods) return;
        var dialog = new OpenFileDialog { Filter = Tr("Mods tModLoader (*.tmod)|*.tmod"), Multiselect = true, Title = Tr("Selecione os mods e suas dependências") };
        if (dialog.ShowDialog(this) != true) return;
        var profile = selected!;
        await Guard(async ct =>
        {
            var mods = await Task.Run(() => { ct.ThrowIfCancellationRequested(); return TModLoaderModule.ImportMods(profile, store.ServerRoot(profile), dialog.FileNames); }, ct);
            profile.Mods = mods; TModLoaderModule.WriteEnabled(profile, store.ServerRoot(profile)); store.Save(profiles);
            SetStatus(() => mods.Count + Tr(" mod(s) preparados neste perfil. O tModLoader verificará compatibilidade e dependências ao iniciar."));
        }, false);
    }
    private void RemoveTmodClick(object sender, RoutedEventArgs e)
    {
        if (!CanEditTerrariaMods || selected == null || TerrariaModsList.SelectedItem is not TerrariaMod mod) return;
        try
        {
            var file = SafePaths.Inside(store.ServerRoot(selected), "mods/" + mod.Name + ".tmod");
            if (File.Exists(file)) File.Delete(file);
            selected.Mods = selected.Mods.Where(m => m.Name != mod.Name).ToList();
            TModLoaderModule.WriteEnabled(selected, store.ServerRoot(selected)); store.Save(profiles); RefreshTerrariaMods();
        }
        catch (Exception ex) { SetStatus(() => Tr("Não foi possível remover o mod: ") + ex.Message); }
    }
    private void TerrariaModSelected(object sender, SelectionChangedEventArgs e)
    {
        if (RemoveTmodButton != null) RemoveTmodButton.IsEnabled = CanEditTerrariaMods && TerrariaModsList.SelectedItem is TerrariaMod;
    }
    private void CalamityWorkshopClick(object sender, RoutedEventArgs e) => Website("https://steamcommunity.com/sharedfiles/filedetails/?id=2824688072");
    internal async Task PreviewTerrariaModsFlowAsync()
    {
        if (!smoke) return;
        await PreviewTerrariaFlowAsync();
        var vanilla = selected!;
        CreateModdedClick(new Button { Tag = "Calamity" }, new RoutedEventArgs());
        if (allowSmokeCatalog) await ReloadVersionsAsync();
        if (selected?.IsTModLoader != true || selected.Id == vanilla.Id || selected.TerrariaPreset != "Calamity")
            throw new InvalidOperationException("O perfil Calamity não foi isolado do Vanilla.");
        var modded = SaveSettings(); Navigate(1);
        if (MinecraftPacksPanel.Visibility != Visibility.Collapsed || TerrariaModsPanel.Visibility != Visibility.Visible || !ImportTmodsButton.IsEnabled || appliedGameTheme != "Calamity")
            throw new InvalidOperationException("O painel de mods não está disponível ou utiliza o tema errado.");
        if (TerrariaModsStatus.Text.Contains("✓")) throw new InvalidOperationException("A interface indicou mods instalados sem importar arquivos.");
        if (!SwitchGame("Minecraft") || appliedGameTheme != "Minecraft" || GameTexture.Visibility != Visibility.Visible || !ReferenceEquals(GameTexture.Background, FindResource("DirtBrush")))
            throw new InvalidOperationException("O tema Minecraft não foi restaurado.");
        if (!SwitchGame("Terraria", modded) || appliedGameTheme != "Calamity" || GameTexture.Visibility != Visibility.Visible || !ReferenceEquals(GameTexture.Background, FindResource("CalamitySkyBrush")))
            throw new InvalidOperationException("O tema Calamity não foi restaurado.");
        if (allowSmokeCatalog) await ReloadVersionsAsync();
        if (runner.IsActive || tunnel.IsActive) throw new InvalidOperationException("A prévia iniciou processos.");
        Navigate(1);
    }
}
