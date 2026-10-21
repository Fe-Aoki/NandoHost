using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NandoHost.Core;

namespace NandoHost.App;

public partial class MainWindow
{
    private void JavaPathChanged(object sender, TextChangedEventArgs e)
    {
        if (!uiReady) return;
        JavaStatusText.Text = selected?.Installed == true && !selected.IsBedrock
            ? $"Este perfil exige Java {selected.RequiredJava} ou superior. Use Verificar Java para conferir o caminho."
            : "Use Verificar Java para detectar a versão deste executável.";
    }
    private async Task VerifyJavaAsync(CancellationToken ct)
    {
        var major = await ServerProcess.CheckJavaAsync(JavaBox.Text.Trim(), ct);
        JavaStatusText.Text = selected?.Installed == true && !selected.IsBedrock
            ? major >= selected.RequiredJava ? $"Java {major} detectado. Atende ao mínimo de Java {selected.RequiredJava} deste perfil."
                : $"Java {major} detectado. Este perfil exige Java {selected.RequiredJava} ou superior; selecione outro java.exe."
            : $"Java {major} detectado. O instalador informará o requisito da versão de Minecraft escolhida.";
        SetStatus(JavaStatusText.Text);
    }
    private async void CheckJavaClick(object sender, RoutedEventArgs e) => await Guard(VerifyJavaAsync);
    private void CopyBedrockHostClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText("127.0.0.1"); SetStatus("Endereço local copiado. Informe a porta no campo separado do jogo.");
    }
    private void CopyBedrockPortClick(object sender, RoutedEventArgs e)
    {
        if (selected == null) return;
        Clipboard.SetText((selected.IsCrossplay ? selected.BedrockPort : selected.Port).ToString()); SetStatus("Porta local copiada.");
    }
    internal async Task<string> PreviewReviewChecksAsync(bool installed)
    {
        if (!smoke) return "";
        var report = new StringBuilder();
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            report.AppendLine("PASS " + label);
        }
        if (installed)
        {
            var p = selected!;
            p.Installed = true; p.EulaAccepted = false;
            p.LaunchJar = p.IsBedrock ? "bedrock_server.exe" : "server.jar";
            var folder = store.ServerRoot(p); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, p.LaunchJar);
            File.WriteAllText(path, "UI fixture only: never run this file.");
            if (p.IsBedrock) p.NativeExecutableSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            ServerSelected(ServerList, null!);
            using var blockedDownloads = new Downloads(new NoCatalogNetwork());
            var previous = catalog; catalog = new ReleaseCatalog(blockedDownloads);
            try
            {
                versionLoading = true;
                await ReloadVersionsAsync();
                Check(!versionLoading && VersionStatusText.Text.Contains("preservada"), "installed profile bypasses online catalog");
            }
            finally { catalog = previous; }
            Check(!EditionBox.IsEnabled && !VersionBox.IsEnabled && !EngineBox.IsEnabled, "installed identity selectors are locked");
            SaveSettings();
            Check(selected!.Installed && SaveButton.IsEnabled, "installed configuration can be saved offline");
            Navigate(0); UpdateLayout(); StartClick(this, new RoutedEventArgs());
            UpdateLayout();
            var eulaAt = EulaCheck.TransformToAncestor(SettingsScroll).Transform(new Point(0, 0));
            Check(EulaCheck.IsVisible && eulaAt.Y >= 0 && eulaAt.Y + EulaCheck.ActualHeight <= SettingsScroll.ActualHeight, "EULA primary action scrolls acceptance into view");
            Check(Pages.SelectedIndex == 3 && EulaCheck.IsChecked != true && !runner.IsActive, "EULA review never accepts or starts the server");
        }
        else
        {
            StartClick(this, new RoutedEventArgs());
            Check(Pages.SelectedIndex == 3 && !runner.IsActive, "new profile primary action opens setup");
        }
        var beforeSave = selected!;
        var persisted = File.ReadAllText(Path.Combine(store.Root, "profiles.json"));
        var eulaFile = Path.Combine(store.ServerRoot(beforeSave), "eula.txt");
        var previousEula = File.Exists(eulaFile) ? File.ReadAllText(eulaFile) : "eula=false";
        if (File.Exists(eulaFile)) File.Delete(eulaFile);
        Directory.CreateDirectory(eulaFile);
        try
        {
            NameBox.Text = "Unsaved name during IO failure";
            var rejected = false;
            try { SaveSettings(); } catch (IOException) { rejected = true; } catch (UnauthorizedAccessException) { rejected = true; }
            Check(rejected && ReferenceEquals(selected, beforeSave) && File.ReadAllText(Path.Combine(store.Root, "profiles.json")) == persisted,
                "failed property write does not commit the profile or replace the selected item");
        }
        finally
        {
            Directory.Delete(eulaFile); File.WriteAllText(eulaFile, previousEula); NameBox.Text = beforeSave.Name;
        }
        Navigate(3); SettingsScroll.ScrollToTop(); UpdateLayout();
        void ActionsVisible()
        {
            var at = SettingsActions.TransformToAncestor(this).Transform(new Point(0, 0));
            Check(SettingsActions.IsVisible && at.Y >= 0 && at.Y + SettingsActions.ActualHeight <= ActualHeight - 38,
                "settings actions remain visible within window");
        }
        ActionsVisible(); SettingsScroll.ScrollToBottom(); UpdateLayout(); ActionsVisible();
        Check(!string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(EditionBox)), "edition selector has accessible name");
        Navigate(1); UpdateLayout();
        Check(PackEmptyState.IsVisible && !InstallPackButton.IsEnabled, "modpack empty state explains next step");
        Navigate(2); UpdateLayout();
        Check(LocalJavaPanel.IsVisible == !selected!.IsBedrock && LocalBedrockPanel.IsVisible == (selected.IsBedrock || selected.IsCrossplay),
            "local addresses match edition and crossplay");
        Check(!TunnelDetailText.Text.Contains("Nenhuma assinatura"), "tunnel setup does not promise free access for every transport");
        if (!selected.IsBedrock) { await VerifyJavaAsync(CancellationToken.None); Check(JavaStatusText.Text.Contains("detectado"), "Java diagnostic detects runtime without starting Minecraft"); }
        Check(!runner.IsActive && !tunnel.IsActive, "review does not start game servers or tunnels");
        SettingsScroll.ScrollToTop();
        return report.ToString();
    }
    private sealed class NoCatalogNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new InvalidOperationException("Installed profile attempted a catalog request.");
    }
}
