using System;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace NandoHost.App;

public partial class MainWindow
{
    private void RefreshLibraryCovers()
    {
        if (MinecraftCover == null || TerrariaCover == null) return;
        string Count(string game)
        {
            var count = profiles.Count(p => p.Game == game);
            return count == 1 ? "1 servidor" : count + " servidores";
        }
        MinecraftCoverCount.Text = Count("Minecraft"); TerrariaCoverCount.Text = Count("Terraria");
        HomeServersEmpty.Visibility = profiles.Any(p => p.Game == homeGame) ? Visibility.Collapsed : Visibility.Visible;
        foreach (var card in new[] { MinecraftCover, TerrariaCover })
            card.BorderBrush = (Brush)new BrushConverter().ConvertFromString((string)card.Tag == homeGame ? "#D7B27A" : "#30444F")!;
    }

    private Border? CoverArt(Button card) => card == MinecraftCover ? MinecraftCoverArt : card == TerrariaCover ? TerrariaCoverArt : null;

    private static void Animate(Animatable target, DependencyProperty property, double value)
    {
        // Finite animations only; respect the Windows animation preference.
        target.BeginAnimation(property, new DoubleAnimation(value, TimeSpan.FromMilliseconds(SystemParameters.ClientAreaAnimation ? 150 : 0))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }

    private void SetCoverHover(Button card, bool hover)
    {
        if (CoverArt(card)?.RenderTransform is not TransformGroup group) return;
        var scale = (ScaleTransform)group.Children[0];
        var offset = (TranslateTransform)group.Children[1];
        var zoom = hover && SystemParameters.ClientAreaAnimation ? 1.035 : 1;
        Animate(scale, ScaleTransform.ScaleXProperty, zoom); Animate(scale, ScaleTransform.ScaleYProperty, zoom);
        if (!hover) { offset.X = 0; offset.Y = 0; }
    }

    private void GameCoverHover(object sender, MouseEventArgs e)
    {
        if (sender is Button card) SetCoverHover(card, e.RoutedEvent == Mouse.MouseEnterEvent);
    }

    private void MoveCover(Button card, Point point)
    {
        if (!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast || card.ActualWidth <= 0 || card.ActualHeight <= 0
            || CoverArt(card)?.RenderTransform is not TransformGroup group) return;
        var offset = (TranslateTransform)group.Children[1];
        offset.X = Math.Clamp((point.X / card.ActualWidth - .5) * 8, -4, 4);
        offset.Y = Math.Clamp((point.Y / card.ActualHeight - .5) * 6, -3, 3);
    }

    private void GameCoverMove(object sender, MouseEventArgs e)
    {
        if (sender is Button card) MoveCover(card, e.GetPosition(card));
    }

    private void CoverOpen(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: string game }) return;
        SelectHomeGame(game); MinecraftGameClick(sender, e); e.Handled = true;
    }

    private static Image? FindLibraryIcon(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is Image icon) return icon;
            var found = FindLibraryIcon(child); if (found != null) return found;
        }
        return null;
    }

    private void LibraryIconHover(object sender, MouseEventArgs e)
    {
        if (sender is not Button button || FindLibraryIcon(button) is not Image icon) return;
        if (icon.RenderTransform is not ScaleTransform) icon.RenderTransform = new ScaleTransform(1, 1);
        var scale = (ScaleTransform)icon.RenderTransform;
        var zoom = e.RoutedEvent == Mouse.MouseEnterEvent && SystemParameters.ClientAreaAnimation ? 1.13 : 1;
        Animate(scale, ScaleTransform.ScaleXProperty, zoom); Animate(scale, ScaleTransform.ScaleYProperty, zoom);
    }

    internal string PreviewLibraryChecks(bool hover)
    {
        if (!smoke) return "";
        var report = new StringBuilder(); var game = homeGame;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            report.AppendLine("PASS " + label);
        }
        ShowHome(); UpdateLayout();
        Check(MinecraftCover.ActualWidth >= 250 && TerrariaCover.ActualWidth >= 250, "game covers fit minimum window");
        HomeSelectGameClick(TerrariaCover, new RoutedEventArgs());
        Check(homeGame == "Terraria" && HomeHeroTitle.Text == "Terraria", "cover selection updates game details");
        HomeGameSearch.Text = "terra";
        Check(MinecraftCover.Visibility == Visibility.Collapsed && TerrariaCover.Visibility == Visibility.Visible, "search filters covers and sidebar");
        HomeGameSearch.Text = "zzzz";
        Check(HomeNoGamesText.Visibility == Visibility.Visible && MinecraftCover.Visibility == Visibility.Collapsed && TerrariaCover.Visibility == Visibility.Collapsed, "empty search is explained");
        HomeGameSearch.Clear(); SelectHomeGame(game); UpdateLayout();
        Check(MinecraftCoverCount.Text.StartsWith(profiles.Count(p => !p.IsTerraria).ToString()), "cover counts reflect real profiles");
        var art = (TransformGroup)TerrariaCoverArt.RenderTransform;
        SetCoverHover(TerrariaCover, true); MoveCover(TerrariaCover, new Point(TerrariaCover.ActualWidth, TerrariaCover.ActualHeight));
        var offset = (TranslateTransform)art.Children[1];
        Check(Math.Abs(offset.X) <= 4 && Math.Abs(offset.Y) <= 3, "mouse parallax is bounded");
        SetCoverHover(TerrariaCover, false);
        Check(offset.X == 0 && offset.Y == 0, "mouse leave resets artwork position");
        var previousBusy = busy; busy = true;
        try { RefreshGameLibrary(); Check(HomeManageButton.IsEnabled == (selected?.Game == homeGame), "game entry respects task lock"); }
        finally { busy = previousBusy; RefreshGameLibrary(); }
        Check(!runner.IsActive && !tunnel.IsActive, "library does not start server or tunnel");
        if (hover) { SetCoverHover(TerrariaCover, true); MoveCover(TerrariaCover, new Point(TerrariaCover.ActualWidth, 0)); }
        return report.ToString();
    }
}
