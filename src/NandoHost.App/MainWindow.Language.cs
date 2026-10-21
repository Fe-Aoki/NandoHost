using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using NandoHost.Core;
using static NandoHost.Core.Localizer;

namespace NandoHost.App;

public partial class MainWindow
{
    private void LanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!uiReady || LanguageBox.SelectedValue is not string language || language == CultureCode) return;
        try
        {
            UiPreferencesStore.Save(store.Root, language);
            Localizer.SetCulture(language);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            LanguageBox.SelectedValue = CultureCode;
            MessageBox.Show(this, Tr("Não foi possível salvar o idioma. Verifique a permissão de escrita na pasta Data."), "NandoHost", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (!uiReady || closed) return;
        LanguageBox.SelectedValue = CultureCode;
        CommandPopup.IsOpen = false;
        // Refresh presentation only. Never load/save a profile or reload its fields.
        UpdateControls();
        Navigate(Pages.SelectedIndex);
        SelectHomeGame(homeGame);
        ServerList.Items.Refresh();
        VersionBox.Items.Refresh();
        SuggestionList.Items.Refresh();
        ApplyResponsiveLayout();
        if (tray != null)
        {
            tray.Text = Tr("NandoHost · servidores de jogos");
            if (tray.ContextMenuStrip is { } menu)
            {
                menu.Items[0].Text = Tr("Abrir NandoHost");
                menu.Items[1].Text = Tr("Sair com segurança");
            }
        }
    }

    internal void PreviewLanguage(string language)
    {
        if (!smoke) return;
        LanguageBox.SelectedValue = Localizer.Normalize(language);
    }

    internal string PreviewLanguageChecks()
    {
        if (!smoke) return "";
        var report = new StringBuilder();
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            report.AppendLine("PASS " + label);
        }
        var before = File.ReadAllText(Path.Combine(store.Root, "profiles.json"));
        var selectedId = selected!.Id;
        var page = Pages.SelectedIndex; var home = GameHome.Visibility;
        var commandEnabled = CommandBox.IsEnabled;
        var originalLanguage = CultureCode;
        var fields = new[] { NameBox, PortBox, PlayersBox, JavaBox, WorldNameBox, WorldSeedBox, PackQuery, PublicAddressBox, BedrockPublicBox };
        var original = fields.Select(x => x.Text).ToArray();
        var password = WorldPasswordBox.Password;
        var edition = EditionBox.SelectedIndex; var engine = EngineBox.SelectedIndex; var version = VersionBox.SelectedItem;
        try
        {
            NameBox.Text = "Meu servidor / My server";
            WorldNameBox.Text = "Meu mundo";
            WorldPasswordBox.Password = "private-language-fixture";
            PackQuery.Text = "Calamity";
            CommandBox.IsEnabled = true; CommandBox.Text = "say Olá friends"; CommandBox.CaretIndex = 6;
            commandHistory.Remember("list"); var historyCount = commandHistory.Count;
            foreach (var code in new[] { "en-US", "pt-BR", "en-US", "pt-BR" })
            {
                LanguageBox.SelectedValue = code; UpdateLayout();
                Check(CultureCode == code && UiPreferencesStore.Load(store.Root) == code, "language switch persists " + code);
                Check(NameBox.Text == "Meu servidor / My server" && WorldNameBox.Text == "Meu mundo" && WorldPasswordBox.Password == "private-language-fixture" && PackQuery.Text == "Calamity", "draft fields and password preserved " + code);
                Check(CommandBox.Text == "say Olá friends" && CommandBox.CaretIndex == 6 && commandHistory.Count == historyCount, "command, caret and history preserved " + code);
                Check(EditionBox.SelectedIndex == edition && EngineBox.SelectedIndex == engine && ReferenceEquals(VersionBox.SelectedItem, version), "edition, engine and version preserved " + code);
                Check(File.ReadAllText(Path.Combine(store.Root, "profiles.json")) == before && selected.Id == selectedId, "profiles unchanged " + code);
                Check(HomeManageButton.Content is TextBlock text && text.Text.Contains(code == "en-US" ? "Open servers" : "Abrir servidores"), "library action translated " + code);
                Check(StartButton.Content.ToString()!.Contains(code == "en-US" ? "PREPARE" : "PREPARAR"), "dynamic action translated " + code);
            }
            busy = true; LanguageBox.SelectedValue = "en-US"; UpdateLayout();
            Check(busy && !StartButton.IsEnabled && NameBox.Text == "Meu servidor / My server", "switch during task preserves lock and draft");
            busy = false;
            Check(!runner.IsActive && !tunnel.IsActive, "language changes never start server or tunnel");
            var suggestion = CommandAssist.Suggest("wh", 2).First();
            Check(suggestion.Text == "whitelist " && suggestion.Description.StartsWith("Manages who can join") && suggestion.Description.Contains("<action>"), "suggestion description translated without changing command");
        }
        finally
        {
            busy = false;
            for (var i = 0; i < fields.Length; i++) fields[i].Text = original[i];
            WorldPasswordBox.Password = password;
            CommandBox.Clear(); CommandBox.IsEnabled = commandEnabled; commandHistory.Clear();
            LanguageBox.SelectedValue = originalLanguage; Navigate(page); GameHome.Visibility = home; UpdateControls(); UpdateLayout();
        }
        return report.ToString();
    }
}
