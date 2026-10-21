using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using NandoHost.Core;
using static NandoHost.Core.Localizer;

namespace NandoHost.App;

public partial class MainWindow
{
    private ReleaseCatalog catalog = null!;
    private bool uiReady, loadingProfile, versionLoading;
    private readonly bool allowSmokeCatalog;
    private CancellationTokenSource? editionVersionsRequest;
    private string DraftEdition => selected?.IsTerraria == true ? "PC" : EditionBox.SelectedIndex switch { 1 => "Bedrock", 2 => "Crossplay", _ => "Java" };
    private void ApplyEditionLayout()
    {
        var bedrock = DraftEdition == "Bedrock";
        var crossplay = DraftEdition == "Crossplay";
        JavaFields.Visibility = bedrock ? Visibility.Collapsed : Visibility.Visible;
        CrossplayPortPanel.Visibility = crossplay ? Visibility.Visible : Visibility.Collapsed;
        RamBox.Visibility = bedrock ? Visibility.Collapsed : Visibility.Visible;
        LocalizedUi.Set(RamCaption, "Text", () => bedrock ? Tr("RAM: automática no servidor nativo") : Tr("RAM máxima (MB)"));
        LocalizedUi.Set(PortCaption, "Text", () => bedrock ? Tr("Porta Bedrock (confira o transporte)") : Tr("Porta Java (TCP)"));
        if (bedrock) EngineBox.SelectedIndex = 2;
        else if (crossplay) EngineBox.SelectedIndex = 1;
        else if (EngineBox.SelectedIndex == 2) EngineBox.SelectedIndex = 0;
        EngineBox.IsEnabled = !bedrock && !crossplay && selected?.Installed != true;
        LocalizedUi.Set(EditionHint, "Text", () => DraftEdition switch
        {
            "Bedrock" => Tr("Servidor dedicado oficial para Windows. Sem Java. O catálogo oferece a versão estável atual e, quando disponível, Preview. O pacote atual usa NetherNet; não há histórico oficial completo para download."),
            "Crossplay" => Tr("Fabric + Geyser + Floodgate + Fabric API. Apenas versões compatíveis com o Geyser atual. Geyser publica builds de desenvolvimento; clientes Bedrock também precisam de uma versão suportada."),
            _ => Tr("Vanilla ou Fabric, com versões de lançamento do catálogo oficial. O Java necessário depende da versão escolhida.")
        });
        LocalizedUi.Set(WhitelistHelp, "Text", () => bedrock
            ? Tr("No console: allowlist add \"Seu Gamertag\". A autenticação Xbox permanece ativada.")
            : crossplay ? Tr("No console: whitelist add NomeJava. Para Bedrock, use o nome com o prefixo do Floodgate (normalmente .). Autenticação Java permanece ativada; assinatura obrigatória de chat é desativada para compatibilidade.")
            : Tr("No console: whitelist add NomeDoJogador. A autenticação oficial permanece ativada."));
        LocalizedUi.Set(WhitelistCheck, "Content", () => bedrock ? Tr("Permitir apenas contas da allowlist") : Tr("Permitir apenas jogadores da whitelist"));
        ApplyGameLayout();
    }
    private async void EditionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!uiReady || loadingProfile) return;
        var bedrock = DraftEdition == "Bedrock";
        if (bedrock && PortBox.Text == "25565") PortBox.Text = NextPort("Bedrock").ToString();
        else if (!bedrock && PortBox.Text == "19132") PortBox.Text = NextPort().ToString();
        ApplyEditionLayout();
        await ReloadVersionsAsync();
    }
    private void EngineSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!uiReady || loadingProfile) return;
        if (DraftEdition == "Java" && EngineBox.SelectedIndex == 2) EngineBox.SelectedIndex = 0;
    }
    private async Task ReloadVersionsAsync(bool force = false)
    {
        if (!uiReady || smoke && !allowSmokeCatalog) return;
        editionVersionsRequest?.Cancel();
        if (selected?.Installed == true && selected.Edition == DraftEdition)
        {
            editionVersionsRequest = null; versionLoading = false;
            LocalizedUi.Set(VersionStatusText, "Text", () => Tr("Versão preservada neste perfil. Para outra edição ou versão, use Novo servidor."));
            UpdateControls(); return;
        }
        var request = new CancellationTokenSource(); editionVersionsRequest = request;
        var edition = DraftEdition; var profileId = selected?.Id;
        var current = (VersionBox.SelectedItem as ServerVersion)?.Id;
        var currentPreview = (VersionBox.SelectedItem as ServerVersion)?.Preview ?? false;
        if (selected?.Edition != edition) current = null;
        versionLoading = true; LocalizedUi.Set(VersionStatusText, "Text", () => Tr("Carregando versões oficiais…")); UpdateControls();
        try
        {
            if (force) catalog.Refresh();
            var choices = selected?.IsTModLoader == true ? await new TModLoaderModule(downloads).VersionsAsync(request.Token) : selected?.IsTerraria == true ? await new TerrariaModule(downloads).VersionsAsync(request.Token) : await catalog.VersionsAsync(edition, request.Token);
            request.Token.ThrowIfCancellationRequested();
            if (selected?.Id != profileId || DraftEdition != edition) return;
            var chosen = choices.FirstOrDefault(v => v.Id == current && v.Preview == currentPreview);
            if (chosen == null && selected?.Installed == true && selected.Edition == edition)
            {
                chosen = new(selected.MinecraftVersion, selected.BedrockPreview);
                choices.Insert(0, chosen);
            }
            VersionBox.ItemsSource = choices;
            VersionBox.SelectedItem = chosen ?? choices.FirstOrDefault(v => !v.Preview) ?? choices[0];
            LocalizedUi.Set(VersionStatusText, "Text", () => edition == "Bedrock" ? TrFormat("{0} opção(ões) oficiais para Windows. Preview exige cliente Preview.", choices.Count)
                : TrFormat("{0} versão(ões) disponíveis. Abra a lista para escolher.", choices.Count));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!request.IsCancellationRequested)
            {
                LocalizedUi.Set(VersionStatusText, "Text", () => Tr("Não foi possível atualizar: ") + ex.Message + Tr(" Use Atualizar versões para tentar novamente."));
                SetStatus(() => VersionStatusText.Text);
                // An installed profile remains usable offline; a new edition cannot use a stale list.
                if (selected?.Edition != edition) VersionBox.ItemsSource = null;
            }
        }
        finally
        {
            if (editionVersionsRequest == request)
            {
                editionVersionsRequest = null; versionLoading = false; UpdateControls();
            }
            request.Dispose();
        }
    }
    internal async Task PreviewEditionFlowAsync(string edition)
    {
        if (!smoke || !allowSmokeCatalog) return;
        EditionBox.SelectedIndex = edition switch { "Bedrock" => 1, "Crossplay" => 2, _ => 0 };
        await ReloadVersionsAsync();
        if (VersionBox.Items.Count == 0) throw new InvalidOperationException("A seleção de edição não carregou versões.");
        if (edition == "Java")
        {
            var legacy = VersionBox.Items.OfType<ServerVersion>().FirstOrDefault(v => v.Id == "1.20.1")
                ?? throw new InvalidOperationException("A lista Java não oferece versões anteriores.");
            VersionBox.SelectedItem = legacy;
        }
        var profile = SaveSettings();
        if (profile.Edition != edition || profile.EulaAccepted || runner.IsActive || tunnel.IsActive)
            throw new InvalidOperationException("A seleção alterou o modo ou iniciou processos indevidos.");
        if (edition == "Bedrock" && (JavaFields.Visibility != Visibility.Collapsed || RamBox.Visibility != Visibility.Collapsed || profile.Engine != "Bedrock"))
            throw new InvalidOperationException("A interface Bedrock ainda usa opções Java.");
        if (edition == "Crossplay" && (profile.Engine != "Fabric" || CrossplayPortPanel.Visibility != Visibility.Visible))
            throw new InvalidOperationException("O modo conjunto não foi configurado.");
        NewServerClick(this, new RoutedEventArgs());
        if (selected?.Edition != edition || selected.Id == profile.Id || selected.Port == profile.Port)
            throw new InvalidOperationException("O novo perfil não preservou a edição ou não separou os dados.");
        ServerList.SelectedItem = profile;
        Navigate(3); UpdateLayout();
    }
    private void SaveBedrockAddressClick(object sender, RoutedEventArgs e)
    {
        if (selected == null) return;
        var address = BedrockPublicBox.Text.Trim();
        if (address.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\')) { SetStatus(() => Tr("Use somente endereço e porta, sem espaços ou http://.")); return; }
        selected.BedrockPublicAddress = address; store.Save(profiles); SetStatus(() => Tr("Endereço Bedrock salvo. Salvar não verifica acesso externo."));
    }
    private void CopyBedrockAddressClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(BedrockPublicBox.Text)) { SetStatus(() => Tr("Informe o endereço Bedrock primeiro.")); return; }
        Clipboard.SetText(BedrockPublicBox.Text.Trim()); SetStatus(() => Tr("Endereço Bedrock copiado."));
    }
}
