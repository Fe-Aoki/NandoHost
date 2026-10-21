using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static NandoHost.Core.Localizer;

namespace NandoHost.App;

public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeArt.Register(Resources);
        DispatcherUnhandledException += (_, args) =>
        {
            var smokeIndex = Array.IndexOf(e.Args, "--smoke");
            if (smokeIndex >= 0 && e.Args.Length > smokeIndex + 1)
            {
                File.WriteAllText(e.Args[smokeIndex + 1] + ".error", args.Exception.ToString());
                args.Handled = true; Shutdown(1); return;
            }
            MessageBox.Show(args.Exception.Message, "NandoHost", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };
        var smoke = Array.IndexOf(e.Args, "--smoke");
        var root = smoke >= 0 ? Path.Combine(Path.GetTempPath(), "NandoHost-smoke-" + Guid.NewGuid().ToString("N")) : Path.Combine(AppContext.BaseDirectory, "Data");
        if (smoke < 0)
        {
            var identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant())))[..20];
            instance = new Mutex(true, "Local\\NandoHost-" + identity, out var created);
            if (!created)
            {
                MessageBox.Show(Tr("O NandoHost já está aberto nesta pasta. Procure o ícone na bandeja do Windows."), "NandoHost", MessageBoxButton.OK, MessageBoxImage.Information);
                instance.Dispose(); instance = null; Shutdown(); return;
            }
        }
        var window = new MainWindow(root, smoke >= 0, Array.IndexOf(e.Args, "--live-catalog") >= 0);
        MainWindow = window;
        window.Show();
        if (smoke >= 0)
        {
            if (Array.IndexOf(e.Args, "--compact") >= 0) { window.Width = window.MinWidth; window.Height = window.MinHeight; }
            var sizeArg = Array.IndexOf(e.Args, "--size");
            if (sizeArg >= 0 && e.Args.Length > sizeArg + 2) { window.Width = double.Parse(e.Args[sizeArg + 1], System.Globalization.CultureInfo.InvariantCulture); window.Height = double.Parse(e.Args[sizeArg + 2], System.Globalization.CultureInfo.InvariantCulture); }
            if (Array.IndexOf(e.Args, "--maximized") >= 0) window.WindowState = WindowState.Maximized;
            if (e.Args.Length > smoke + 2 && int.TryParse(e.Args[smoke + 2], out var page)) window.PreviewPage(page);
            var target = e.Args.Length > smoke + 1 ? e.Args[smoke + 1] : Path.Combine(root, "preview.png");
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(async () =>
            {
                var editionArg = Array.IndexOf(e.Args, "--edition");
                if (editionArg >= 0 && e.Args.Length > editionArg + 1) await window.PreviewEditionFlowAsync(e.Args[editionArg + 1]);
                if (Array.IndexOf(e.Args, "--review") >= 0)
                    File.WriteAllText(target + ".review.txt", await window.PreviewReviewChecksAsync(Array.IndexOf(e.Args, "--review-installed") >= 0));
                if (e.Args.Length > smoke + 2 && int.TryParse(e.Args[smoke + 2], out var renderPage)) window.PreviewPage(renderPage);
                if (Array.IndexOf(e.Args, "--game-entry") >= 0)
                {
                    await window.PreviewGameEntryAsync(Array.IndexOf(e.Args, "--record-entry") >= 0 ? target : null);
                    File.WriteAllText(target + ".entry.txt", "PASS: game entry opens overview even before installation; 620ms branded intro settles; cancel and reentry verified; respects reduced-animation settings; Minecraft brand image; no outer client border or dotted nav focus; native border suppressed where supported; profile JSON unchanged; no game or tunnel started. OS animations=" + SystemParameters.ClientAreaAnimation + "; high contrast=" + SystemParameters.HighContrast);
                }
                if (Array.IndexOf(e.Args, "--terraria") >= 0)
                {
                    await window.PreviewTerrariaFlowAsync();
                    if (e.Args.Length > smoke + 2 && int.TryParse(e.Args[smoke + 2], out var terraPage)) window.PreviewPage(terraPage);
                    File.WriteAllText(target + ".terraria.txt", "PASS: Terraria game selection; profile isolation and Minecraft preservation; world settings; masked password outside profile JSON; no game or tunnel started.");
                }
                var headingFont = (FontFamily)FindResource("MinecraftFont");
                if (Array.IndexOf(e.Args, "--calamity") >= 0)
                {
                    await window.PreviewTerrariaModsFlowAsync();
                    if (e.Args.Length > smoke + 2 && int.TryParse(e.Args[smoke + 2], out var modPage)) window.PreviewPage(modPage);
                    File.WriteAllText(target + ".mods.txt", "PASS: separate Vanilla and Calamity profiles; tModLoader settings; mod import panel; missing dependencies shown honestly; theme switching restores Minecraft; no server or tunnel started.");
                }
                var terraFace = new Typeface((FontFamily)FindResource("TerrariaFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
                if (!terraFace.TryGetGlyphTypeface(out var terraGlyph) || !terraGlyph.FontUri.OriginalString.Contains("Underdog", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A fonte Terraria não foi incorporada.");
                foreach (var ch in "ãçêóÁÍ") if (!terraGlyph.CharacterToGlyphMap.ContainsKey(ch)) throw new InvalidOperationException("A fonte Terraria não contém os acentos.");
                var face = new Typeface(headingFont, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                if (!face.TryGetGlyphTypeface(out var glyph) || !glyph.FontUri.OriginalString.Contains("NandoHost", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A fonte incorporada dos títulos não foi carregada.");
                foreach (var character in "ãçêóÁÍ")
                    if (!glyph.CharacterToGlyphMap.ContainsKey(character)) throw new InvalidOperationException("A fonte não contém os acentos necessários.");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
                if (Array.IndexOf(e.Args, "--home-check") >= 0) window.PreviewHomeChecks();
                window.PreviewInteractions(Array.IndexOf(e.Args, "--disabled-list") >= 0, Array.IndexOf(e.Args, "--suggestions") >= 0);
                var inset = NativeWindowTheme.ClientTopInset(window);
                if (inset != 0) throw new InvalidOperationException("Ainda há uma faixa nativa acima do conteúdo: " + inset + " pixels.");
                if (Array.IndexOf(e.Args, "--resize-check") >= 0)
                    File.WriteAllText(target + ".resize.txt", window.PreviewResizeChecks());
                if (Array.IndexOf(e.Args, "--library-check") >= 0)
                    File.WriteAllText(target + ".library.txt", window.PreviewLibraryChecks(Array.IndexOf(e.Args, "--hover-cover") >= 0));
                if (Array.IndexOf(e.Args, "--language-check") >= 0)
                    File.WriteAllText(target + ".language.txt", window.PreviewLanguageChecks());
                var languageArg = Array.IndexOf(e.Args, "--language");
                if (languageArg >= 0 && e.Args.Length > languageArg + 1) window.PreviewLanguage(e.Args[languageArg + 1]);
                window.UpdateLayout();
                window.RenderPreview(target);
                File.WriteAllText(target + ".ok", "WPF loaded; native top inset=0 px; layout rendered; embedded font and Portuguese accents verified. Font: " + glyph.FontUri);
                window.Close();
            }));
        }
    }
    protected override void OnExit(ExitEventArgs e) { if (instance != null) { instance.ReleaseMutex(); instance.Dispose(); } base.OnExit(e); }
}
