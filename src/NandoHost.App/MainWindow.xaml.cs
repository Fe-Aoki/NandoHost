using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NandoHost.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using static NandoHost.Core.Localizer;

namespace NandoHost.App;

public partial class MainWindow : Window
{
    private readonly ProfileStore store;
    private readonly Downloads downloads = new();
    private readonly ServerProcess runner = new();
    private readonly PlayitAgent tunnel = new();
    private string? claimUrl;
    private string TunnelFolder => SafePaths.Inside(store.Root, "private-playit");
    private string AgentFile => SafePaths.Inside(store.Root, "tools/playit-0.17.1.exe");
    private readonly ObservableCollection<ServerProfile> profiles;
    private readonly ObservableCollection<string> console = new();
    private readonly ConcurrentQueue<LogEntry> pendingLogs = new();
    private readonly DispatcherTimer timer;
    private readonly Forms.NotifyIcon? tray;
    private readonly System.Drawing.Icon? trayIcon;
    private readonly bool smoke;
    private ServerProfile? selected;
    private bool busy, closing, closed;
    private CancellationTokenSource? operation;
    private int pendingCount;
    private DateTime lastMetrics = DateTime.MinValue;
    private DateTime lastCpuAt = DateTime.UtcNow;
    private TimeSpan lastCpu;
    private CancellationTokenSource? versionRequest;
    private readonly CommandHistory commandHistory = new();
    private readonly HashSet<string> knownPlayers = new(StringComparer.OrdinalIgnoreCase);
    private bool editingCommand, suggestionsDismissed, previewSuggestions;

    public MainWindow(string dataFolder, bool smoke = false, bool liveCatalog = false)
    {
        this.smoke = smoke;
        allowSmokeCatalog = smoke && liveCatalog;
        Localizer.SetCulture(smoke ? "pt-BR" : UiPreferencesStore.Load(dataFolder));
        InitializeComponent();
        LanguageBox.SelectedValue = GameLanguageBox.SelectedValue = Localizer.CultureCode;
        PreviewKeyDown += EntryKeyDown;
        NativeWindowTheme.Attach(this);
        SizeChanged += ResponsiveSizeChanged;
        StateChanged += (_, _) => ApplyResponsiveLayout();
        Loaded += (_, _) => ApplyResponsiveLayout();
        CommandBox.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(CommandKeyDown), true);
        store = new ProfileStore(dataFolder);
        catalog = new ReleaseCatalog(downloads);
        profiles = new ObservableCollection<ServerProfile>(store.Load());
        if (profiles.Count == 0) { profiles.Add(new ServerProfile()); store.Save(profiles); }
        ConsoleList.ItemsSource = console;
        currentGame = profiles[0].Game; homeGame = currentGame;
        serverView = new ListCollectionView(profiles) { Filter = item => item is ServerProfile p && p.Game == currentGame };
        ServerList.ItemsSource = serverView;
        runner.Log += entry =>
        {
            pendingLogs.Enqueue(entry);
            Interlocked.Increment(ref pendingCount);
            while (Volatile.Read(ref pendingCount) > 600 && pendingLogs.TryDequeue(out _)) Interlocked.Decrement(ref pendingCount);
        };
        runner.StateChanged += _ => Dispatcher.BeginInvoke(new Action(UpdateControls));
        tunnel.Changed += () => Dispatcher.BeginInvoke(new Action(UpdateControls));
        tunnel.Notice += notice => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (closed) return;
            LocalizedUi.Set(TunnelDetailText, "Text", () => notice.Message);
            if (notice.ClaimUrl != null) { claimUrl = notice.ClaimUrl; ClaimAgentButton.IsEnabled = tunnel.IsActive; }
            if (notice.Address != null && tunnel.IsActive)
            {
                if (selected?.IsCrossplay == true && notice.LocalPort == selected.BedrockPort) BedrockPublicBox.Text = notice.Address;
                else PublicAddressBox.Text = notice.Address;
            }
        }));
        ServerList.SelectedIndex = 0;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += Tick;
        timer.Start();
        Closing += OnClosing;
        Deactivated += (_, _) => { if (!(smoke && previewSuggestions)) CommandPopup.IsOpen = false; };
        LocationChanged += (_, _) => { if (!(smoke && previewSuggestions)) CommandPopup.IsOpen = false; };
        if (!smoke)
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add(Tr("Abrir NandoHost"), null, (_, _) => Dispatcher.Invoke(Restore));
            menu.Items.Add(Tr("Sair com segurança"), null, (_, _) => Dispatcher.Invoke(Close));
            using (var resource = Application.GetResourceStream(new Uri("pack://application:,,,/NandoHost;component/Assets/Icons/nandohost-red.ico")).Stream)
            using (var icon = new System.Drawing.Icon(resource, 32, 32)) trayIcon = (System.Drawing.Icon)icon.Clone();
            tray = new Forms.NotifyIcon { Icon = trayIcon, Text = Tr("NandoHost · servidores de jogos"), ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += (_, _) => Dispatcher.Invoke(Restore);
            StateChanged += (_, _) =>
            {
                timer.Interval = WindowState == WindowState.Minimized ? TimeSpan.FromSeconds(2) : TimeSpan.FromMilliseconds(300);
                if (WindowState == WindowState.Minimized && runner.IsActive)
                {
                    Hide();
                    tray.ShowBalloonTip(2500, Tr("NandoHost continua ligado"), Tr("O servidor continua funcionando. Abra o painel pelo ícone na bandeja."), Forms.ToolTipIcon.Info);
                }
            };
        }
        runner.Message(Tr("NandoHost 0.9.1 · o servidor e seus mundos ficam neste computador."));
        runner.Message(Tr("Comece em Configurações: escolha a versão e instale o servidor."));
        runner.Message(Tr("Para amigos de fora da sua rede, configure Conexão externa."));
        if (!selected!.Installed) Navigate(3);
        uiReady = true; ApplyEditionLayout();
        LanguageState.Current.Changed += OnLanguageChanged;
        Closed += (_, _) => LanguageState.Current.Changed -= OnLanguageChanged;
        Loaded += async (_, _) => { if (GameHome.Visibility != Visibility.Visible) await ReloadVersionsAsync(); };
        ShowHome();
        UpdateControls();
    }

    private void ShowHome()
    {
        ResetGameTransition();
        CommandPopup.IsOpen = false;
        LocalizedUi.Set(MinecraftGameAction, "Text", () => runner.IsActive ? Tr("Voltar ao servidor  →") : Tr("Abrir servidores  →"));
        LocalizedUi.Set(HomeRunStatus, "Text", () => runner.IsActive ? Tr("Servidor em execução: ") + selected?.Name : Tr("Nenhum servidor em execução."));
        SelectHomeGame(runner.IsActive ? currentGame : homeGame);
        GameHome.Visibility = Visibility.Visible;
        SetStatus(() => runner.IsActive ? Tr("Seu servidor continua ligado. Abra o jogo correspondente para acompanhar o console.") : Tr("Escolha um jogo para abrir seus servidores."));
    }
    private string homeGame = "Minecraft";
    private void HomeSelectGameClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string game }) SelectHomeGame(game);
    }
    private void SelectHomeGame(string game)
    {
        homeGame = game == "Terraria" ? "Terraria" : "Minecraft";
        var minecraft = homeGame == "Minecraft";
        LocalizedUi.Set(HomeHeroTitle, "Text", () => homeGame);
        HomeHeroTitle.FontFamily = minecraft ? (FontFamily)FindResource("MinecraftFont") : (FontFamily)FindResource("TerrariaFont");
        LocalizedUi.Set(HomeHeroSubtitle, "Text", () => minecraft ? Tr("JAVA E BEDROCK") : Tr("PC · VANILLA E MODS"));
        HomeGameHero.Background = (Brush)FindResource(minecraft ? "LandscapeBrush" : "TerrariaSceneBrush");
        HomeManageButton.Visibility = HomeServerSection.Visibility = HomeRunStatus.Visibility = Visibility.Visible;
        HomeComingSoonPanel.Visibility = Visibility.Collapsed;
        LocalizedUi.Set(HomeGameSummary, "Text", () => minecraft ? Tr("Vanilla, Fabric e modpacks") : Tr("Vanilla, Calamity e mods com tModLoader"));
        RefreshGameLibrary();
        RefreshLibraryCovers();
        foreach (var button in new[] { MinecraftGameButton, TerrariaGameButton })
        {
            var chosen = (string)button.Tag == homeGame;
            button.Background = chosen ? new SolidColorBrush(Color.FromRgb(24, 43, 56)) : Brushes.Transparent;
            button.BorderBrush = chosen ? new SolidColorBrush(Color.FromRgb(222, 184, 126)) : Brushes.Transparent;
        }
    }
    private void HomeGameSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (MinecraftGameButton == null || TerrariaGameButton == null || HomeNoGamesText == null || MinecraftCover == null || TerrariaCover == null) return;
        var query = HomeGameSearch.Text.Trim();
        MinecraftGameButton.Visibility = "Minecraft".Contains(query, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        TerrariaGameButton.Visibility = "Terraria".Contains(query, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        MinecraftCover.Visibility = MinecraftGameButton.Visibility;
        TerrariaCover.Visibility = TerrariaGameButton.Visibility;
        HomeNoGamesText.Visibility = MinecraftGameButton.Visibility == Visibility.Collapsed && TerrariaGameButton.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
    }
    private void HomeClick(object sender, RoutedEventArgs e) => ShowHome();
    private async void MinecraftGameClick(object sender, RoutedEventArgs e)
    {
        if (GameEntryOverlay.Visibility == Visibility.Visible || !SwitchGame(homeGame)) return;
        GameHome.Visibility = Visibility.Collapsed;
        Navigate(0);
        BeginGameTransition();
        if (!busy && !runner.IsActive)
            SetStatus(() => selected?.Installed == true ? Tr("Visão geral do servidor.") : Tr("Use Preparar servidor para escolher a edição e instalar."));
        if (!runner.IsActive && !busy) await ReloadVersionsAsync();
    }
    private Storyboard? gameEntryStoryboard;
    private bool EntryAnimationEnabled => SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
    private void EntryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || GameEntryOverlay.Visibility != Visibility.Visible) return;
        ShowHome(); e.Handled = true;
    }
    private void ResetGameTransition()
    {
        var previous = gameEntryStoryboard;
        gameEntryStoryboard = null;
        previous?.Remove(this);
        GameEntryOverlay.Visibility = Visibility.Collapsed;
        GameEntryOverlay.Opacity = 0;
        GameWorkspace.BeginAnimation(OpacityProperty, null);
        GameSlide.BeginAnimation(TranslateTransform.XProperty, null);
        GameWorkspace.Opacity = 1;
        GameSlide.X = 0;
    }
    private void BeginGameTransition()
    {
        ResetGameTransition();
        if (!EntryAnimationEnabled) return;
        GameEntryOverlay.Visibility = Visibility.Visible;
        var storyboard = new Storyboard { FillBehavior = FillBehavior.Stop };
        void Add(DependencyObject target, DependencyProperty property, double from, double to, int begin, int duration)
        {
            var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(duration))
            {
                BeginTime = TimeSpan.FromMilliseconds(begin),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, new PropertyPath(property));
            storyboard.Children.Add(animation);
        }
        var cover = new DoubleAnimationUsingKeyFrames();
        cover.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        cover.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(280))));
        cover.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(620)), new CubicEase { EasingMode = EasingMode.EaseInOut }));
        Storyboard.SetTarget(cover, GameEntryOverlay);
        Storyboard.SetTargetProperty(cover, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(cover);
        Add(GameEntryContent, OpacityProperty, 0, 1, 0, 150);
        Add(GameEntryScale, ScaleTransform.ScaleXProperty, 0.93, 1, 0, 460);
        Add(GameEntryScale, ScaleTransform.ScaleYProperty, 0.93, 1, 0, 460);
        Add(GameWorkspace, OpacityProperty, 0, 1, 280, 340);
        Add(GameSlide, TranslateTransform.XProperty, 18, 0, 280, 340);
        storyboard.Completed += (_, _) => { if (gameEntryStoryboard == storyboard) ResetGameTransition(); };
        gameEntryStoryboard = storyboard;
        storyboard.Begin(this, HandoffBehavior.SnapshotAndReplace, true);
    }
    internal async Task PreviewGameEntryAsync(string? previewTarget = null)
    {
        if (!smoke) return;
        var before = File.ReadAllText(Path.Combine(store.Root, "profiles.json"));
        ShowHome(); Navigate(3);
        if (previewTarget != null) { UpdateLayout(); RenderPreview(previewTarget + ".home.png"); }
        MinecraftGameClick(this, new RoutedEventArgs());
        if (GameHome.Visibility != Visibility.Collapsed || Pages.SelectedIndex != 0)
            throw new InvalidOperationException("Entrar no jogo não abriu a Visão geral.");
        if (EntryAnimationEnabled && GameEntryOverlay.Visibility != Visibility.Visible)
            throw new InvalidOperationException("A abertura animada não apareceu.");
        for (var frame = 0; frame < 10; frame++)
        {
            await Task.Delay(75);
            if (previewTarget != null)
            {
                var directory = previewTarget + ".frames";
                Directory.CreateDirectory(directory);
                RenderPreview(Path.Combine(directory, frame.ToString("D2") + ".png"));
            }
        }
        UpdateLayout();
        if (GameEntryOverlay.Visibility != Visibility.Collapsed || gameEntryStoryboard != null)
            throw new InvalidOperationException("A abertura não liberou o painel.");
        // Returning to the library during an intro must cancel it without leaving a mask.
        ShowHome(); MinecraftGameClick(this, new RoutedEventArgs());
        await Task.Delay(40);
        if (EntryAnimationEnabled)
            RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        else ShowHome();
        if (GameEntryOverlay.Visibility != Visibility.Collapsed || gameEntryStoryboard != null || GameHome.Visibility != Visibility.Visible)
            throw new InvalidOperationException("Voltar à biblioteca não cancelou a abertura.");
        MinecraftGameClick(this, new RoutedEventArgs());
        await Task.Delay(760);
        if (Pages.SelectedIndex != 0 || GameEntryOverlay.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Reentrar após cancelar não abriu a Visão geral.");
        if (Math.Abs(GameWorkspace.Opacity - 1) > 0.001 || Math.Abs(GameSlide.X) > 0.001)
            throw new InvalidOperationException("A transição não terminou no estado final.");
        if (WindowSurface.BorderThickness != new Thickness(0) || GameBrandIcon.Source != FindResource("MinecraftGameImage"))
            throw new InvalidOperationException("Moldura externa ou identidade do jogo incorreta.");
        if (NativeWindowTheme.BorderColor(this) is int nativeBorder && nativeBorder != -2)
            throw new InvalidOperationException("A moldura nativa não foi suprimida.");
        if (DashboardNav.FocusVisualStyle != null)
            throw new InvalidOperationException("O contorno pontilhado de foco continua ativo.");
        if (before != File.ReadAllText(Path.Combine(store.Root, "profiles.json")) || runner.IsActive || tunnel.IsActive)
            throw new InvalidOperationException("A entrada no jogo alterou os dados ou iniciou processos.");
    }
    private void HomeServerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var target = profiles.FirstOrDefault(p => p.Id == id);
        if (target == null) return;
        if (target.Id != selected?.Id && (busy || runner.IsActive || tunnel.IsActive))
        {
            SetStatus(() => Tr("Conclua a tarefa e pare o servidor e o agente antes de trocar de perfil."));
            return;
        }
        homeGame = target.Game;
        if (!SwitchGame(target.Game, target)) return;
        MinecraftGameClick(sender, e);
    }
    internal void PreviewHomeChecks()
    {
        if (!smoke) return;
        var before = File.ReadAllText(Path.Combine(store.Root, "profiles.json"));
        var previous = selected?.Id;
        for (var i = 0; i < 3; i++)
        {
            ShowHome();
            if (GameHome.Visibility != Visibility.Visible) throw new InvalidOperationException("A seleção de jogos não abriu.");
            MinecraftGameClick(this, new RoutedEventArgs());
            if (GameHome.Visibility != Visibility.Collapsed || Pages.SelectedIndex != 0)
                throw new InvalidOperationException("Minecraft não abriu a Visão geral.");
        }
        if (selected?.Id != previous || before != File.ReadAllText(Path.Combine(store.Root, "profiles.json")) || runner.IsActive || tunnel.IsActive)
            throw new InvalidOperationException("A navegação de jogos alterou perfis ou iniciou processos.");
        var temporary = new ServerProfile { Name = "Outro perfil de teste", Port = 25566 };
        profiles.Add(temporary);
        try
        {
            ShowHome(); busy = true;
            HomeServerClick(new Button { Tag = temporary.Id }, new RoutedEventArgs());
            if (selected?.Id != previous || GameHome.Visibility != Visibility.Visible)
                throw new InvalidOperationException("O atalho trocou o perfil durante uma tarefa.");
            busy = false;
            HomeServerClick(new Button { Tag = temporary.Id }, new RoutedEventArgs());
            if (selected?.Id != temporary.Id || GameHome.Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("O atalho não abriu o outro perfil.");
        }
        finally
        {
            busy = false;
            ServerList.SelectedItem = profiles.First(p => p.Id == previous);
            profiles.Remove(temporary);
        }
        if (before != File.ReadAllText(Path.Combine(store.Root, "profiles.json")))
            throw new InvalidOperationException("Os atalhos gravaram alterações indevidas nos perfis.");
        HomeServerClick(new Button { Tag = previous }, new RoutedEventArgs());
        if (GameHome.Visibility != Visibility.Collapsed || selected?.Id != previous)
            throw new InvalidOperationException("O atalho não abriu o servidor existente.");
        ShowHome();
        UpdateLayout();
        SelectHomeGame("Terraria");
        if (HomeManageButton.Visibility != Visibility.Visible || HomeComingSoonPanel.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("Terraria não está disponível na biblioteca.");
        HomeGameSearch.Text = "terra";
        if (MinecraftGameButton.Visibility != Visibility.Collapsed || TerrariaGameButton.Visibility != Visibility.Visible)
            throw new InvalidOperationException("A busca na biblioteca não filtrou os jogos.");
        HomeGameSearch.Text = "zzzz";
        if (HomeNoGamesText.Visibility != Visibility.Visible)
            throw new InvalidOperationException("A biblioteca não explicou a busca sem resultados.");
        HomeGameSearch.Clear(); SelectHomeGame("Minecraft"); UpdateLayout();
        if (HomeBrandName.ActualHeight < 20 || HomeGamePanel.ActualWidth < 600 || HomeServerShortcuts.Items.Count == 0)
            throw new InvalidOperationException("A biblioteca não apresentou os elementos principais.");
    }
    private void Restore() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void BannerViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement viewport) return;
        var clip = new RectangleGeometry(new Rect(0, 0, Math.Max(0, e.NewSize.Width), Math.Max(0, e.NewSize.Height)));
        clip.Freeze(); viewport.Clip = clip;
    }
    internal void PreviewPage(int page) { ResetGameTransition(); Tick(null, EventArgs.Empty); if (page == 4) { ShowHome(); return; } GameHome.Visibility = Visibility.Collapsed; Navigate(Math.Clamp(page, 0, 3)); }
    internal void PreviewInteractions(bool disabledList, bool suggestions)
    {
        if (!smoke) return;
        PreparePrivateTunnelFolder();
        var acl = System.IO.FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(TunnelFolder));
        if (!acl.AreAccessRulesProtected || acl.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier)).Count != 1)
            throw new InvalidOperationException("A pasta de credenciais não está restrita ao usuário atual.");
        if (disabledList) ServerList.IsEnabled = false;
        ServerList.ApplyTemplate();
        if (ServerList.Template.FindName("ListSurface", ServerList) is not Border surface || surface.Background != ServerList.Background)
            throw new InvalidOperationException("O fundo da lista de servidores não segue o tema.");
        if (!suggestions) return;
        previewSuggestions = true;
        CommandBox.IsEnabled = true;
        CommandBox.Focus();
        CommandBox.Text = "whi"; CommandBox.CaretIndex = 3;
        RefreshSuggestions(true); PreviewPress(Key.Tab);
        if (CommandBox.Text != "whitelist ") throw new InvalidOperationException($"Falha ao completar o comando: texto={CommandBox.Text}, popup={CommandPopup.IsOpen}, opções={SuggestionList.Items.Count}.");
        knownPlayers.Add("Nando");
        RefreshSuggestions(true); PreviewPress(Key.Tab);
        if (CommandBox.Text != "whitelist add ") throw new InvalidOperationException("Falha ao completar o subcomando.");
        RefreshSuggestions(true); PreviewPress(Key.Tab);
        if (CommandBox.Text != "whitelist add Nando") throw new InvalidOperationException("Falha ao completar o jogador.");
        commandHistory.Remember("list"); commandHistory.Remember("say teste");
        CommandBox.Text = "say rascunho"; CommandBox.CaretIndex = CommandBox.Text.Length;
        PreviewPress(Key.Up); PreviewPress(Key.Up);
        if (CommandBox.Text != "list") throw new InvalidOperationException("Falha ao navegar no histórico.");
        PreviewPress(Key.Down); PreviewPress(Key.Down);
        if (CommandBox.Text != "say rascunho") throw new InvalidOperationException("O rascunho do comando foi perdido.");
        commandHistory.Clear();
        CommandBox.Text = "whitelist "; CommandBox.CaretIndex = CommandBox.Text.Length;
        RefreshSuggestions(true);
        PreviewPress(Key.Down);
        if ((SuggestionList.SelectedItem as CommandCompletion)?.Label != "whitelist remove") throw new InvalidOperationException("Falha ao selecionar uma sugestão.");
        PreviewPress(Key.Escape);
        if (CommandPopup.IsOpen) throw new InvalidOperationException("Escape não fechou as sugestões.");
        suggestionsDismissed = false; RefreshSuggestions(true);
        runner.Message("Prévia de sugestões · nenhum comando foi enviado."); Tick(null, EventArgs.Empty);
        SuggestionsPanel.UpdateLayout();
    }
    private void PreviewPress(Key key) => CommandBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    internal void RenderPreview(string target)
    {
        UpdateLayout();
        if (smoke) File.WriteAllText(target + ".ui", $"popup={CommandPopup.IsOpen}; items={SuggestionList.Items.Count}; inputEnabled={CommandBox.IsEnabled}; focus={CommandBox.IsKeyboardFocused}; selection={CommandBox.SelectionLength}; caret={CommandBox.CaretIndex}; panel={SuggestionsPanel.ActualWidth}x{SuggestionsPanel.ActualHeight}");
        if (smoke && previewSuggestions && !CommandPopup.IsOpen) throw new InvalidOperationException("As sugestões não ficaram visíveis na prévia.");
        var windowBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        windowBitmap.Render(this);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawImage(windowBitmap, new Rect(0, 0, ActualWidth, ActualHeight));
            if (CommandPopup.IsOpen)
            {
                SuggestionsPanel.UpdateLayout();
                var popup = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(SuggestionsPanel.ActualWidth), (int)Math.Ceiling(SuggestionsPanel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                popup.Render(SuggestionsPanel);
                var anchor = CommandBox.TransformToAncestor(this).Transform(new Point(0, 0));
                drawing.DrawImage(popup, new Rect(anchor.X, anchor.Y - SuggestionsPanel.ActualHeight - 5, SuggestionsPanel.ActualWidth, SuggestionsPanel.ActualHeight));
            }
        }
        var result = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        result.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(result));
        using var file = File.Create(target); encoder.Save(file);
    }
    private void Tick(object? sender, EventArgs e)
    {
        var changed = false;
        var count = 0;
        while (count++ < 100 && pendingLogs.TryDequeue(out var entry))
        {
            Interlocked.Decrement(ref pendingCount);
            var joined = Regex.Match(entry.Text, @"(?<![A-Za-z0-9_.])(?<name>\.?[A-Za-z0-9_]{1,16}) joined the game\b");
            if (joined.Success) knownPlayers.Add(joined.Groups["name"].Value);
            console.Add(entry.ToString()); changed = true;
        }
        while (console.Count > 600) console.RemoveAt(0);
        if (changed && AutoScrollCheck.IsChecked == true && console.Count > 0 && IsVisible && Pages.SelectedIndex == 0)
            ConsoleList.ScrollIntoView(console[^1]);
        var now = DateTime.UtcNow;
        if ((now - lastMetrics).TotalSeconds < 2) return;
        lastMetrics = now;
        using (var app = Process.GetCurrentProcess()) { var appMemoryMb = app.WorkingSet64 / 1048576d; LocalizedUi.Set(AppMemoryText, "Text", () => TrFormat("Painel: {0:0} MB", appMemoryMb)); }
        var metrics = runner.Metrics();
        if (metrics is { } m)
        {
            LocalizedUi.Set(MemoryText, "Text", () => $"{m.MemoryMb:0} MB");
            var percent = (m.Cpu - lastCpu).TotalMilliseconds / Math.Max(1, (now - lastCpuAt).TotalMilliseconds) / Environment.ProcessorCount * 100;
            LocalizedUi.Set(CpuText, "Text", () => $"{Math.Clamp(percent, 0, 100):0.0} %");
            lastCpu = m.Cpu;
        }
        else { LocalizedUi.Set(MemoryText, "Text", () => "— MB"); LocalizedUi.Set(CpuText, "Text", () => "— %"); lastCpu = TimeSpan.Zero; }
        lastCpuAt = now;
    }

    private void Navigate(int index)
    {

        Pages.SelectedIndex = index;
        var titles = new[] { selected?.Name ?? Tr("Visão geral"), Tr("Modpacks, sem complicação."), Tr("Convide quem está longe."), Tr("Do seu jeito.") };
        var subtitles = new[] { Tr("Seu servidor, com controle em tempo real."), Tr("Modpacks Java/Fabric. A importação cria um perfil Java separado."), Tr("Compartilhe seu mundo com amigos de qualquer lugar."), Tr("Equilibre o servidor e o ") + (selected?.Game ?? Tr("jogo")) + Tr(" no mesmo computador.") };
        var eyebrows = new[] { Tr("PAINEL DO SERVIDOR"), Tr("BIBLIOTECA DE MODPACKS"), Tr("CONEXÃO EXTERNA"), Tr("CONFIGURAÇÕES DO SERVIDOR") };
        if (selected?.IsTerraria == true) { titles[1] = Tr("Uma aventura com mods."); subtitles[1] = Tr("Calamity e mods personalizados com tModLoader para PC."); eyebrows[1] = Tr("MODS DO TERRARIA"); }
        LocalizedUi.Set(PageTitle, "Text", () => titles[index]); LocalizedUi.Set(PageSubtitle, "Text", () => subtitles[index]); LocalizedUi.Set(PageEyebrow, "Text", () => eyebrows[index]);
        var buttons = new[] { DashboardNav, PacksNav, NetworkNav, SettingsNav };
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].Background = (Brush)new BrushConverter().ConvertFromString(i == index ? GameSelectionColor : "Transparent")!;
            buttons[i].BorderBrush = i == index ? (Brush)FindResource("Accent") : Brushes.Transparent;
        }
    }
    private void NavigateClick(object sender, RoutedEventArgs e) => Navigate(int.Parse((string)((Button)sender).Tag));
    private void ServerSelected(object sender, SelectionChangedEventArgs e)
    {
        if (ServerList.SelectedItem is not ServerProfile p) return;
        if (selected?.Id != p.Id) { commandHistory.Clear(); knownPlayers.Clear(); CommandBox.Clear(); }
        selected = p;
        loadingProfile = true;
        editionVersionsRequest?.Cancel();
        NameBox.Text = p.Name;
        EditionBox.SelectedIndex = p.IsBedrock ? 1 : p.IsCrossplay ? 2 : 0;
        VersionBox.ItemsSource = new[] { new ServerVersion(p.MinecraftVersion, p.BedrockPreview) }; VersionBox.SelectedIndex = 0;
        BedrockPortBox.Text = p.BedrockPort.ToString();
        EngineBox.SelectedIndex = p.IsBedrock ? 2 : p.Engine == "Fabric" ? 1 : 0;
        RamBox.Text = p.MemoryMb.ToString(); PortBox.Text = p.Port.ToString();
        ViewBox.Text = p.ViewDistance.ToString(); SimulationBox.Text = p.SimulationDistance.ToString();
        PlayersBox.Text = p.MaxPlayers.ToString(); JavaBox.Text = p.JavaPath;
        WhitelistCheck.IsChecked = p.Whitelist; EulaCheck.IsChecked = p.EulaAccepted;
        LoadTerrariaFields(p);
        PublicAddressBox.Text = p.PublicAddress; BedrockPublicBox.Text = p.BedrockPublicAddress;
        LocalizedUi.Set(LocalAddress, "Text", () => "localhost:" + p.Port);
        if (Pages.SelectedIndex == 0) LocalizedUi.Set(PageTitle, "Text", () => p.Name);
        loadingProfile = false; ApplyEditionLayout();
        UpdateControls();
        if (uiReady) _ = ReloadVersionsAsync();
    }
    private void UpdateControls()
    {
        if (selected == null) return;
        var active = runner.IsActive;
        var installed = selected.Installed && File.Exists(Path.Combine(store.ServerRoot(selected), selected.LaunchJar));
        StartButton.IsEnabled = !busy && !active;
        LocalizedUi.Set(StartButton, "Content", () => !installed ? Tr("⚙  PREPARAR SERVIDOR") : !selected.IsTerraria && !selected.EulaAccepted ? Tr("☷  REVISAR EULA") : Tr("▶  LIGAR SERVIDOR"));
        LocalizedUi.Set(StartButton, "ToolTip", () => !installed ? Tr("Escolha a edição e instale o servidor em Configurações.") : !selected.IsTerraria && !selected.EulaAccepted ? Tr("Leia e aceite a EULA, se concordar, antes de executar.") : Tr("Inicia o servidor local. O acesso externo depende da conexão configurada."));
        StopButton.IsEnabled = !busy && runner.State is ServerState.Running or ServerState.Starting;
        ForceButton.Visibility = runner.State == ServerState.Stopping ? Visibility.Visible : Visibility.Collapsed;
        ConfigFields.IsEnabled = !busy && !active && !tunnel.IsActive;
        EditionFields.IsEnabled = ConfigFields.IsEnabled;
        CrossplayPortPanel.IsEnabled = ConfigFields.IsEnabled;
        JavaFields.IsEnabled = ConfigFields.IsEnabled;
        EditionBox.IsEnabled = !selected.Installed;
        VersionBox.IsEnabled = !versionLoading && !selected.Installed;
        EngineBox.IsEnabled = !selected.Installed && DraftEdition == "Java";
        SaveButton.IsEnabled = InstallButton.IsEnabled = !busy && !active && !tunnel.IsActive && !versionLoading;
        JavaBox.IsEnabled = !busy && !active;
        WhitelistCheck.IsEnabled = ConfigFields.IsEnabled;
        EulaCheck.IsEnabled = ConfigFields.IsEnabled;
        CheckJavaButton.IsEnabled = !busy && !active;
        LocalizedUi.Set(SettingsLockText, "Text", () => busy ? Tr("Tarefa em andamento. Aguarde ou use Cancelar tarefa.") : active ? Tr("Pare o servidor para alterar as configurações.") : tunnel.IsActive ? Tr("Pare o agente em Conexão externa para alterar o perfil ou a instalação.") : "");
        SettingsLockText.Visibility = SettingsLockText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        LocalizedUi.Set(SaveButton, "ToolTip", () => SettingsLockText.Text.Length > 0 ? SettingsLockText.Text : Tr("Salva as opções deste perfil."));
        InstallButton.IsEnabled &= !(selected.IsTModLoader && selected.Installed);
        LocalizedUi.Set(InstallButton, "Content", () => selected.Installed ? Tr("Reinstalar servidor") : Tr("Baixar e instalar"));
        LocalizedUi.Set(InstallButton, "ToolTip", () => SettingsLockText.Text.Length > 0 ? SettingsLockText.Text : selected.Installed ? Tr("Reinstala a versão fixada deste perfil.") : Tr("Baixa os arquivos da edição e versão selecionadas."));
        ServerList.IsEnabled = !busy && !active && !tunnel.IsActive;
        foreach (var button in new[] { NewButton, ImportMrButton, ImportCfButton, BackupButton }) button.IsEnabled = !busy && !active && !tunnel.IsActive;
        BackupButton.IsEnabled = !busy && !active && !tunnel.IsActive && installed;
        LocalizedUi.Set(BackupButton, "ToolTip", () => !installed ? Tr("Instale o servidor antes de criar um backup.") : active ? Tr("Pare o servidor para salvar um backup consistente.") : Tr("Cria um ZIP do perfil e abre a pasta do backup."));
        DownloadAgentButton.IsEnabled = !busy && !tunnel.IsActive;
        StartAgentButton.IsEnabled = !busy && !tunnel.IsActive && File.Exists(AgentFile);
        StopAgentButton.IsEnabled = !busy && tunnel.IsActive;
        ClaimAgentButton.IsEnabled = tunnel.IsActive && claimUrl != null;
        LocalizedUi.Set(TunnelStateText, "Text", () => tunnel.IsActive ? Tr("●  Agente em execução") : Tr("●  Agente parado"));
        LocalizedUi.Set(EditionBadge, "Text", () => selected.EditionLabel.ToUpperInvariant());
        BedrockPublicPanel.Visibility = selected.IsCrossplay ? Visibility.Visible : Visibility.Collapsed;
        LocalJavaPanel.Visibility = selected.IsBedrock ? Visibility.Collapsed : Visibility.Visible;
        LocalBedrockPanel.Visibility = selected.IsBedrock || selected.IsCrossplay ? Visibility.Visible : Visibility.Collapsed;
        LocalizedUi.Set(LocalBedrockAddress, "Text", () => Tr("127.0.0.1 · porta ") + (selected.IsCrossplay ? selected.BedrockPort : selected.Port));
        LocalizedUi.Set(PublicAddressLabel, "Text", () => selected.IsBedrock ? Tr("Endereço externo Bedrock") : Tr("Endereço externo Java"));
        BedrockLocalHint.Visibility = selected.IsBedrock || selected.IsCrossplay ? Visibility.Visible : Visibility.Collapsed;
        LocalizedUi.Set(TunnelTargetText, "Text", () => selected.IsBedrock ? TrFormat("Bedrock → este agente → 127.0.0.1:{0}. Confira transport e server-udp-ports no server.properties.", selected.Port)
            : selected.IsCrossplay ? $"Java: 127.0.0.1:{selected.Port} (TCP). Bedrock/Geyser: 127.0.0.1:{selected.BedrockPort} (TCP + UDP / NetherNet)."
            : TrFormat("Minecraft Java → este agente → 127.0.0.1:{0} (TCP).", selected.Port));
        LocalizedUi.Set(TunnelPlanText, "Text", () => selected.IsBedrock || selected.IsCrossplay
            ? Tr("Confira a versão e o transporte do Bedrock. O playit lista NetherNet como Premium; o NandoHost não contrata planos. Para não gastar, use uma rota direta compatível quando disponível. Não há garantia de acesso externo gratuito com todas as versões.")
            : Tr("Escolha o túnel Minecraft Java gratuito no site. O NandoHost não contrata planos. O agente continua ligado até você pará-lo ou fechar o aplicativo."));
        SearchButton.IsEnabled = !busy;
        VersionsButton.IsEnabled = !busy && !active && !tunnel.IsActive && !versionLoading && !selected.Installed;
        InstallPackButton.IsEnabled = !busy && !active && !tunnel.IsActive && PackVersions.SelectedItem != null;
        LocalizedUi.Set(InstallPackButton, "ToolTip", () => tunnel.IsActive ? Tr("Pare o agente antes de criar um perfil de modpack.") : active ? Tr("Pare o servidor antes de instalar outro perfil.") : Tr("Selecione um modpack e uma versão. A instalação cria um perfil Java separado."));
        PackResults.IsEnabled = !busy; PackVersions.IsEnabled = !busy;
        CommandBox.IsEnabled = runner.State == ServerState.Running;
        SendButton.IsEnabled = runner.State == ServerState.Running;
        if (!CommandBox.IsEnabled) CommandPopup.IsOpen = false;
        LocalizedUi.Set(InstalledText, "Text", () => installed ? selected.IsBedrock ? Tr("Bedrock nativo instalado") : TrFormat("{0} instalado · Java {1}+", selected.Engine, selected.RequiredJava) : Tr("Ainda não instalado"));
        LocalizedUi.Set(MemoryDetail, "Text", () => selected.IsNative ? Tr("Uso nativo; sem limite de RAM da JVM") : Tr("Limite configurado: ") + selected.MemoryMb + " MB");
        LocalizedUi.Set(StateText, "Text", () => runner.State switch { ServerState.Starting => Tr("Iniciando…"), ServerState.Running => Tr("Ligado"), ServerState.Stopping => Tr("Salvando…"), _ => Tr("Desligado") });
        StateText.Foreground = runner.State == ServerState.Running ? (Brush)FindResource("Accent") : Brushes.White;
        LocalizedUi.Set(StateDetail, "Text", () => runner.State switch { ServerState.Running => Tr("Entre por localhost:") + selected.Port, ServerState.Starting => Tr("Aguarde a preparação do mundo"), ServerState.Stopping => Tr("Encerrando com segurança"), _ => installed ? selected.IsTerraria || selected.EulaAccepted ? Tr("Pronto para ligar") : Tr("Revise a EULA para continuar") : Tr("Prepare o servidor em Configurações") });
        if (GameHome.Visibility == Visibility.Visible)
        {
            LocalizedUi.Set(HomeRunStatus, "Text", () => active ? Tr("Servidor em execução: ") + selected.Name : Tr("Nenhum servidor em execução."));
            RefreshGameLibrary();
        }
        ApplyGameLayout();
    }
    private ServerProfile SaveSettings()
    {
        if (runner.IsActive) throw new InvalidOperationException(Tr("Pare o servidor antes de alterar as configurações."));
        if (selected == null) throw new InvalidOperationException(Tr("Selecione um servidor."));
        if (selected.IsTerraria) return SaveTerrariaSettings();
        var p = JsonSerializer.Deserialize<ServerProfile>(JsonSerializer.Serialize(selected))!;
        if (versionLoading || VersionBox.SelectedItem is not ServerVersion version) throw new InvalidOperationException(Tr("Aguarde o catálogo e selecione uma versão disponível."));
        p.Name = NameBox.Text.Trim(); p.MinecraftVersion = version.Id; p.BedrockPreview = version.Preview;
        p.Edition = DraftEdition;
        p.Engine = p.IsBedrock ? "Bedrock" : p.IsCrossplay || EngineBox.SelectedIndex == 1 ? "Fabric" : "Vanilla";
        if (!int.TryParse(BedrockPortBox.Text, out var bedrockPort)) throw new InvalidDataException(Tr("Informe uma porta Bedrock válida."));
        p.BedrockPort = bedrockPort;
        p.JavaPath = JavaBox.Text.Trim(); p.Whitelist = WhitelistCheck.IsChecked == true; p.EulaAccepted = EulaCheck.IsChecked == true;
        if (!int.TryParse(RamBox.Text, out var ram) || !int.TryParse(PortBox.Text, out var port) || !int.TryParse(ViewBox.Text, out var view) ||
            !int.TryParse(SimulationBox.Text, out var simulation) || !int.TryParse(PlayersBox.Text, out var players)) throw new InvalidDataException(Tr("Preencha os campos numéricos com números inteiros."));
        p.MemoryMb = ram; p.Port = port; p.ViewDistance = view; p.SimulationDistance = simulation; p.MaxPlayers = players;
        if (selected.Installed && (p.Edition != selected.Edition || p.Engine != selected.Engine || p.MinecraftVersion != selected.MinecraftVersion || p.BedrockPreview != selected.BedrockPreview))
            throw new InvalidOperationException(Tr("Para trocar a versão ou o tipo de um servidor instalado, crie outro perfil. Isso preserva seu mundo atual."));
        if (!p.Installed && (p.Edition != selected.Edition || p.MinecraftVersion != selected.MinecraftVersion || p.Engine != selected.Engine))
        {
            p.LoaderVersion = ""; p.NativeExecutableSha256 = "";
            p.LaunchJar = p.IsBedrock ? "bedrock_server.exe" : "server.jar";
        }
        ProfileStore.Validate(p);
        var index = profiles.IndexOf(selected);
        var replacement = profiles.ToList(); replacement[index] = p;
        store.WriteProperties(p); store.Save(replacement);
        profiles[index] = p; selected = p; ServerList.SelectedItem = p;
        return p;
    }
    private async Task Guard(Func<CancellationToken, Task> action, bool cancellable = true)
    {
        if (busy) return;
        busy = true; operation = new CancellationTokenSource();
        CancelButton.Visibility = cancellable ? Visibility.Visible : Visibility.Collapsed;
        UpdateControls();
        try { await action(operation.Token); }
        catch (OperationCanceledException) { SetStatus(() => Tr("Tarefa cancelada. Você pode tentar novamente.")); }
        catch (Exception ex)
        {
            SetStatus(() => "Falha: " + ex.Message); runner.Message(ex.Message, true);
            MessageBox.Show(this, ex.Message, "NandoHost", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            busy = false; operation.Dispose(); operation = null; CancelButton.Visibility = Visibility.Collapsed;
            UpdateControls();
        }
    }
    private void SetStatus(string text) => SetStatus(() => text);
    private void SetStatus(Func<string> text) { LocalizedUi.Set(StatusText, "Text", () => text()); LocalizedUi.Set(StatusText, "ToolTip", () => text()); }
    private IProgress<string> Progress() => new Progress<string>(message => { SetStatus(() => message); runner.Message(message); });
    private void NewServerClick(object sender, RoutedEventArgs e)
    {
        if (currentGame == "Terraria")
        {
            var profile = NewTerrariaProfile(); profiles.Add(profile); store.Save(profiles); ServerList.SelectedItem = profile;
            Navigate(3); SetStatus(() => Tr("Configure o novo mundo Terraria e instale o servidor.")); return;
        }
        var edition = DraftEdition;
        var choice = VersionBox.Items.OfType<ServerVersion>().FirstOrDefault(v => !v.Preview);
        var p = new ServerProfile { Name = Tr("Novo servidor ") + (profiles.Count + 1), Edition = edition,
            Engine = edition == "Bedrock" ? "Bedrock" : edition == "Crossplay" ? "Fabric" : "Vanilla",
            Port = NextPort(edition), BedrockPort = NextPort("Bedrock"), MinecraftVersion = choice?.Id ?? "1.21.1",
            JavaPath = selected?.JavaPath ?? "java" };
        profiles.Add(p); store.Save(profiles); ServerList.SelectedItem = p; Navigate(3);
        SetStatus(() => Tr("Configure e instale seu novo servidor."));
    }
    private int NextPort(string edition = "Java")
    {
        var used = profiles.SelectMany(s => s.IsBedrock ? new[] { s.Port, s.Port + 1 } : s.IsCrossplay ? new[] { s.Port, s.BedrockPort } : new[] { s.Port }).ToHashSet();
        var port = edition == "Terraria" ? 7777 : edition == "Bedrock" ? 19132 : 25565;
        while ((used.Contains(port) || edition == "Bedrock" && used.Contains(port + 1)) && port < 65534) port++;
        return port;
    }
    private async void SaveClick(object sender, RoutedEventArgs e) => await Guard(_ => { SaveSettings(); SetStatus(() => Tr("Configurações salvas.")); return Task.CompletedTask; });
    private async void InstallClick(object sender, RoutedEventArgs e) => await Guard(async ct =>
    {
        var p = SaveSettings();
        var folder = store.ServerRoot(p);
        if (File.Exists(Path.Combine(folder, p.LaunchJar)))
        {
            SetStatus(() => Tr("Criando backup antes de reinstalar…"));
            await store.BackupAsync(p, ct);
        }
        try { await new Installer(downloads).InstallAsync(p, folder, Progress(), ct); }
        finally { store.Save(profiles); }
        store.WriteProperties(p); store.Save(profiles); Navigate(0);
        SetStatus(() => p.IsTerraria ? Tr("Terraria instalado. O primeiro início criará o mundo configurado.") : TrFormat("{0} instalado. Confira a EULA e os requisitos antes de ligar.", p.EditionLabel));
    });
    private async void StartClick(object sender, RoutedEventArgs e)
    {
        if (selected == null || busy || runner.IsActive) return;
        if (!selected.Installed || !File.Exists(Path.Combine(store.ServerRoot(selected), selected.LaunchJar)))
        {
            Navigate(3); SettingsScroll.ScrollToTop();
            SetStatus(() => Tr("Escolha as opções e a versão, depois use Baixar e instalar.")); return;
        }
        if (!selected.IsTerraria && !selected.EulaAccepted)
        {
            Navigate(3); UpdateLayout(); EulaCheck.BringIntoView(); EulaCheck.Focus();
            SetStatus(() => Tr("Leia a EULA. Se concordar, marque a aceitação e salve as configurações.")); return;
        }
        await Guard(async ct =>
        {
            var p = SaveSettings();
            SetStatus(() => p.IsNative ? Tr("Verificando arquivos e iniciando o servidor nativo…") : Tr("Verificando Java e iniciando o servidor…"));
            await runner.StartAsync(p, store.ServerRoot(p), ct);
            lastCpu = TimeSpan.Zero; lastCpuAt = DateTime.UtcNow;
            Navigate(0); SetStatus(() => Tr("Processo iniciado. Aguarde o estado Ligado no painel; isso não confirma acesso externo."));
        });
    }
    private async void StopClick(object sender, RoutedEventArgs e) => await Guard(async _ =>
    {
        SetStatus(() => Tr("Salvando o mundo e parando o servidor…"));
        var stopped = await runner.StopAsync();
        SetStatus(() => stopped ? Tr("Servidor encerrado.") : Tr("Ainda encerrando. Aguarde ou use Encerrar à força."));
    }, false);
    private void ForceClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, Tr("Encerrar à força pode perder alterações recentes do mundo. Continuar?"), Tr("Encerrar servidor"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes) runner.ForceStop();
    }
    private async void BackupClick(object sender, RoutedEventArgs e) => await Guard(async ct =>
    {
        if (runner.IsActive) throw new InvalidOperationException(Tr("Pare o servidor antes do backup."));
        SetStatus(() => Tr("Criando backup…"));
        var file = await store.BackupAsync(selected!, ct);
        SetStatus(() => Tr("Backup salvo: ") + Path.GetFileName(file)); runner.Message(Tr("Backup: ") + file);
        OpenFolder(Path.GetDirectoryName(file)!);
    });
    private void FolderClick(object sender, RoutedEventArgs e)
    {
        if (selected == null) return;
        var path = store.ServerRoot(selected); Directory.CreateDirectory(path); OpenFolder(path);
    }
    private static void OpenFolder(string path) => Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });
    private async void SendClick(object sender, RoutedEventArgs e) => await SendCommand();
    private async Task SendCommand()
    {
        var command = CommandBox.Text;
        try
        {
            await runner.SendAsync(command);
            if (!(selected?.IsTerraria == true && Regex.IsMatch(command.TrimStart().TrimStart('/'), @"^password(?:\s|$)", RegexOptions.IgnoreCase))) commandHistory.Remember(command);
            var name = Regex.Match(command.Trim().TrimStart('/'), @"^(?:whitelist\s+(?:add|remove)|op|deop|kick|ban|pardon)\s+(?<name>[A-Za-z0-9_]{1,16})(?:\s|$)");
            if (name.Success) knownPlayers.Add(name.Groups["name"].Value);
            CommandPopup.IsOpen = false; suggestionsDismissed = true;
            editingCommand = true; CommandBox.Clear(); editingCommand = false;
            CommandBox.Focus();
        }
        catch (Exception ex) { SetStatus(() => ex.Message); }
    }
    private void RefreshSuggestions(bool force = false)
    {
        if (CommandPopup == null || editingCommand) return;
        if (!CommandBox.IsEnabled || (!force && ((!CommandBox.IsKeyboardFocused && !(smoke && previewSuggestions)) || suggestionsDismissed)) || CommandBox.SelectionLength > 0)
        { CommandPopup.IsOpen = false; return; }
        var matches = CommandAssist.Suggest(CommandBox.Text, CommandBox.CaretIndex, knownPlayers, selected?.IsTerraria == true ? "Terraria" : selected?.Edition ?? "Java")
            .Where(c => c.Text != CommandBox.Text || c.Caret != CommandBox.CaretIndex).ToList();
        SuggestionList.ItemsSource = matches;
        SuggestionList.SelectedIndex = matches.Count > 0 ? 0 : -1;
        CommandPopup.IsOpen = matches.Count > 0 && (force || CommandBox.Text.Length > 0);
    }
    private void CommandTextChanged(object sender, TextChangedEventArgs e)
    {
        if (editingCommand || CommandPopup == null) return;
        commandHistory.Reset(); suggestionsDismissed = false; RefreshSuggestions();
    }
    private void CommandSelectionChanged(object sender, RoutedEventArgs e) => RefreshSuggestions();
    private void CommandLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (smoke && previewSuggestions) return;
        if (!SuggestionList.IsMouseOver && !SuggestionList.IsKeyboardFocusWithin) CommandPopup.IsOpen = false;
    }
    private void ApplyCompletion()
    {
        if (SuggestionList.SelectedItem is not CommandCompletion completion) return;
        editingCommand = true;
        CommandBox.Text = completion.Text; CommandBox.CaretIndex = completion.Caret;
        editingCommand = false; commandHistory.Reset(); suggestionsDismissed = false;
        CommandBox.Focus(); RefreshSuggestions();
    }
    private void SuggestionClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(SuggestionList, source) is ListBoxItem)
        { ApplyCompletion(); e.Handled = true; }
    }
    private async void CommandKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CommandPopup.IsOpen = false; suggestionsDismissed = true; e.Handled = true; return; }
        if (e.Key == Key.Space && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        { suggestionsDismissed = false; RefreshSuggestions(true); e.Handled = true; return; }
        if (e.Key == Key.Tab)
        {
            if (CommandBox.SelectionLength != 0) return;
            if (!CommandPopup.IsOpen) { suggestionsDismissed = false; RefreshSuggestions(true); }
            if (SuggestionList.SelectedItem is CommandCompletion) { ApplyCompletion(); e.Handled = true; }
            return;
        }
        if (e.Key is Key.Up or Key.Down)
        {
            if (CommandPopup.IsOpen && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                var count = SuggestionList.Items.Count;
                SuggestionList.SelectedIndex = (SuggestionList.SelectedIndex + (e.Key == Key.Up ? count - 1 : 1)) % count;
                SuggestionList.ScrollIntoView(SuggestionList.SelectedItem); e.Handled = true; return;
            }
            if (commandHistory.Count > 0 && (e.Key == Key.Up || commandHistory.IsNavigating))
            {
                CommandPopup.IsOpen = false; suggestionsDismissed = true;
                var value = e.Key == Key.Up ? commandHistory.Previous(CommandBox.Text) : commandHistory.Next();
                editingCommand = true; CommandBox.Text = value; CommandBox.CaretIndex = value.Length; editingCommand = false;
                e.Handled = true;
            }
            return;
        }
        if (e.Key == Key.Enter) { e.Handled = true; await SendCommand(); }
    }
    private void ClearConsoleClick(object sender, RoutedEventArgs e) => console.Clear();
    private async void VersionsClick(object sender, RoutedEventArgs e) => await ReloadVersionsAsync(true);
    private async void SearchClick(object sender, RoutedEventArgs e) => await SearchPacks();
    private async void SearchKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; await SearchPacks(); } }
    private async Task SearchPacks() => await Guard(async ct =>
    {
        versionRequest?.Cancel(); PackVersions.ItemsSource = null; PackResults.ItemsSource = null;
        PackEmptyState.Visibility = Visibility.Visible; LocalizedUi.Set(PackEmptyTitle, "Text", () => Tr("Buscando modpacks…"));
        LocalizedUi.Set(PackEmptyDetail, "Text", () => Tr("Consultando o catálogo Modrinth."));
        SetStatus(() => Tr("Buscando no Modrinth…"));
        System.Collections.Generic.IReadOnlyList<PackHit> hits;
        try { hits = await new Modpacks(downloads).SearchAsync(PackQuery.Text.Trim(), ct); }
        catch
        {
            LocalizedUi.Set(PackEmptyTitle, "Text", () => Tr("A busca não terminou"));
            LocalizedUi.Set(PackEmptyDetail, "Text", () => Tr("Confira a conexão e tente novamente. Seus perfis foram preservados.")); throw;
        }
        PackResults.ItemsSource = hits;
        PackEmptyState.Visibility = hits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LocalizedUi.Set(PackEmptyTitle, "Text", () => Tr("Nenhum modpack encontrado"));
        LocalizedUi.Set(PackEmptyDetail, "Text", () => Tr("Tente outro nome ou importe um pacote que você já baixou."));
        SetStatus(() => TrFormat("{0} modpacks encontrados. Escolha um para ver as versões.", hits.Count));
    });
    private async void PackSelected(object sender, SelectionChangedEventArgs e)
    {
        versionRequest?.Cancel();
        if (PackResults.SelectedItem is not PackHit hit) return;
        var request = new CancellationTokenSource(); versionRequest = request;
        PackVersions.ItemsSource = null; InstallPackButton.IsEnabled = false;
        try
        {
            SetStatus(() => Tr("Carregando versões do modpack…"));
            var versions = await new Modpacks(downloads).VersionsAsync(hit.Id, request.Token);
            if (request.IsCancellationRequested) return;
            PackVersions.ItemsSource = versions; PackVersions.SelectedIndex = versions.Count > 0 ? 0 : -1;
            UpdateControls(); SetStatus(() => Tr("Escolha a versão. Pacotes Fabric têm instalação automática nesta prévia."));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetStatus(() => ex.Message); }
        finally { if (versionRequest == request) versionRequest = null; request.Dispose(); }
    }
    private async void InstallPackClick(object sender, RoutedEventArgs e) => await Guard(async ct =>
    {
        if (PackVersions.SelectedItem is not PackVersion version) return;
        var cache = SafePaths.Inside(store.Root, "cache/" + SafePaths.Token(version.Id) + ".mrpack");
        SetStatus(() => Tr("Baixando pacote do Modrinth…"));
        try
        {
            await new Modpacks(downloads).DownloadAsync(version.Id, cache, ct);
            await ImportMrAsync(cache, ct);
        }
        finally { if (File.Exists(cache)) File.Delete(cache); }
    });
    private async void ImportMrClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = Tr("Modpack Modrinth (*.mrpack)|*.mrpack"), CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        await Guard(ct => ImportMrAsync(dialog.FileName, ct));
    }
    private async Task ImportMrAsync(string file, CancellationToken ct)
    {
        if (runner.IsActive || tunnel.IsActive) throw new InvalidOperationException(Tr("Pare o servidor e o agente antes de instalar um modpack."));
        var info = Modpacks.Inspect(file);
        var p = new ServerProfile { Name = info.Name.Length > 64 ? info.Name[..64] : info.Name, PackName = info.Name, MinecraftVersion = info.Minecraft, Engine = info.Engine, LoaderVersion = info.Loader, Port = NextPort(), JavaPath = selected?.JavaPath ?? "java" };
        ProfileStore.Validate(p);
        var staging = SafePaths.Inside(store.Root, "staging/" + p.Id);
        try
        {
            SetStatus(() => Tr("Preparando modpack em uma pasta temporária…"));
            await new Modpacks(downloads).ImportAsync(file, staging, Progress(), ct);
            await new Installer(downloads).InstallAsync(p, staging, Progress(), ct);
            Directory.CreateDirectory(Path.GetDirectoryName(store.ServerRoot(p))!);
            Directory.Move(staging, store.ServerRoot(p));
            profiles.Add(p); store.WriteProperties(p); store.Save(profiles); ServerList.SelectedItem = p;
            Navigate(3); SetStatus(() => Tr("Modpack instalado. Confira a RAM, o Java e a EULA antes de ligar."));
        }
        finally { DeleteStaging(staging); }
    }
    private void DeleteStaging(string path)
    {
        // Only fresh GUID folders under our staging directory can be removed here.
        var stagingRoot = Path.GetFullPath(Path.Combine(store.Root, "staging")) + Path.DirectorySeparatorChar;
        var absolute = Path.GetFullPath(path);
        if (!absolute.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase) || !Guid.TryParseExact(Path.GetFileName(absolute), "N", out _))
            throw new InvalidOperationException(Tr("Pasta temporária inválida."));
        SafePaths.RejectLink(absolute);
        if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
    }
    private async void ImportCurseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "CurseForge Server Pack (*.zip)|*.zip", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        await Guard(async ct =>
        {
            var p = new ServerProfile { Name = Path.GetFileNameWithoutExtension(dialog.FileName), PackName = Path.GetFileName(dialog.FileName), Port = NextPort(), Engine = "Fabric", JavaPath = selected?.JavaPath ?? "java" };
            if (p.Name.Length > 64) p.Name = p.Name[..64];
            ProfileStore.Validate(p);
            var staging = SafePaths.Inside(store.Root, "staging/" + p.Id);
            try
            {
                await Modpacks.ImportCurseServerZipAsync(dialog.FileName, staging, Progress(), ct);
                Directory.CreateDirectory(Path.GetDirectoryName(store.ServerRoot(p))!);
                Directory.Move(staging, store.ServerRoot(p)); profiles.Add(p); store.Save(profiles); ServerList.SelectedItem = p;
                Navigate(3); SetStatus(() => Tr("Arquivos importados. Informe a versão Minecraft e use Fabric. Pacotes Forge/NeoForge ainda não são executados nesta prévia."));
            }
            finally { DeleteStaging(staging); }
        });
    }
    private void BrowseJavaClick(object sender, RoutedEventArgs e)
    {
        if (busy || runner.IsActive) return;
        var dialog = new OpenFileDialog { Filter = "Java (java.exe)|java.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) JavaBox.Text = dialog.FileName;
    }
    private void SaveAddressClick(object sender, RoutedEventArgs e)
    {
        if (selected == null) return;
        var address = PublicAddressBox.Text.Trim();
        if (address.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\')) { SetStatus(() => Tr("Use somente o endereço do servidor, sem http:// ou espaços.")); return; }
        selected.PublicAddress = address; store.Save(profiles); SetStatus(() => Tr("Endereço salvo. A conexão externa ainda precisa estar configurada no túnel ou roteador."));
    }
    private void CopyLocalClick(object sender, RoutedEventArgs e) { Clipboard.SetText(LocalAddress.Text); SetStatus(() => Tr("Endereço local copiado.")); }
    private void CopyPublicClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PublicAddressBox.Text)) { SetStatus(() => Tr("Informe o endereço público primeiro.")); return; }
        Clipboard.SetText(PublicAddressBox.Text.Trim()); SetStatus(() => Tr("Endereço copiado."));
    }
    private static void Website(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    private void EulaWebsiteClick(object sender, RoutedEventArgs e) => Website("https://www.minecraft.net/en-us/eula");
    private void CurseWebsiteClick(object sender, RoutedEventArgs e) => Website("https://www.curseforge.com/minecraft/search?class=modpacks");
    private async void DownloadAgentClick(object sender, RoutedEventArgs e) => await Guard(async ct =>
    {
        SetStatus(() => Tr("Baixando o agente portátil oficial e verificando SHA-256…"));
        await downloads.FileAsync(PlayitAgent.DownloadUrl, AgentFile, PlayitAgent.Hash, "SHA256", 40 * 1024 * 1024, ct);
        LocalizedUi.Set(TunnelDetailText, "Text", () => Tr("Agente verificado. Clique em Iniciar agente para vincular sua conta."));
        SetStatus(() => Tr("Agente oficial pronto. Nenhum serviço do Windows foi instalado."));
    });
    private async void StartAgentClick(object sender, RoutedEventArgs e) => await Guard(async ct =>
    {
        PreparePrivateTunnelFolder();
        claimUrl = null;
        LocalizedUi.Set(TunnelDetailText, "Text", () => Tr("Iniciando. Aguarde o botão de vinculação ou configure seu túnel no site."));
        await tunnel.StartAsync(AgentFile, TunnelFolder, selected!.Port, ct, selected.IsCrossplay ? selected.BedrockPort : null);
        SetStatus(() => Tr("Agente iniciado. Vincule sua conta e configure as entradas da edição selecionada no site."));
    });
    private async void StopAgentClick(object sender, RoutedEventArgs e) => await Guard(async _ =>
    {
        await tunnel.StopAsync(); claimUrl = null;
        SetStatus(() => Tr("Agente parado. O servidor local pode continuar funcionando."));
    }, false);
    private void ClaimAgentClick(object sender, RoutedEventArgs e)
    {
        if (tunnel.IsActive && claimUrl != null) Website(claimUrl);
    }
    private void TunnelDashboardClick(object sender, RoutedEventArgs e) => Website("https://playit.gg/account/tunnels");
    private void PreparePrivateTunnelFolder()
    {
        SafePaths.RejectLink(TunnelFolder);
        Directory.CreateDirectory(TunnelFolder);
        var owner = System.Security.Principal.WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException(Tr("Não foi possível proteger a credencial do agente."));
        var acl = new System.Security.AccessControl.DirectorySecurity();
        acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(owner,
            System.Security.AccessControl.FileSystemRights.FullControl,
            System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
            System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        System.IO.FileSystemAclExtensions.SetAccessControl(new DirectoryInfo(TunnelFolder), acl);
        var secret = SafePaths.Inside(TunnelFolder, "playit.toml");
        if (File.Exists(secret))
        {
            var fileAcl = new System.Security.AccessControl.FileSecurity();
            fileAcl.SetAccessRuleProtection(true, false);
            fileAcl.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(owner,
                System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
            System.IO.FileSystemAclExtensions.SetAccessControl(new FileInfo(secret), fileAcl);
        }
    }
    private void CancelClick(object sender, RoutedEventArgs e) => operation?.Cancel();
    private void TitleDrag(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) MaximizeClick(sender, e); else if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (closed) return;
        if (busy) { e.Cancel = true; operation?.Cancel(); SetStatus(() => Tr("Aguarde a tarefa terminar antes de fechar.")); return; }
        if (runner.IsActive)
        {
            e.Cancel = true;
            if (closing) return;
            var choice = MessageBox.Show(this, Tr("Deseja manter o servidor ligado na bandeja?\n\nSim: continuar hospedando.\nNão: salvar, parar e sair."), Tr("Fechar NandoHost"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Yes) { WindowState = WindowState.Minimized; return; }
            if (choice != MessageBoxResult.No) return;
            closing = true;
            await Guard(async _ =>
            {
                SetStatus(() => Tr("Salvando o mundo antes de sair…"));
                if (!await runner.StopAsync()) return;
                for (var i = 0; i < 50 && runner.IsActive; i++) await Task.Delay(100);
            }, false);
            closing = false;
            if (runner.IsActive) return;
            Close(); return;
        }
        await tunnel.StopAsync();
        editionVersionsRequest?.Cancel(); versionRequest?.Cancel(); timer.Stop(); tray?.Dispose(); trayIcon?.Dispose(); runner.Dispose(); tunnel.Dispose(); downloads.Dispose(); closed = true;
    }
}
