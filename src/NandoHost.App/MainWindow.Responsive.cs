using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using static NandoHost.Core.Localizer;

namespace NandoHost.App;

public partial class MainWindow
{
    private void ResponsiveSizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();

    private void ApplyResponsiveLayout()
    {
        if (!uiReady || WindowState == WindowState.Minimized) return;
        var compact = ActualHeight < 850;
        var narrow = ActualWidth < 1150;
        var stackLibrary = ActualWidth < 1200;
        MinecraftCover.Height = TerrariaCover.Height = compact ? 180 : 220;
        HomeGameHero.Height = compact ? 270 : 320;
        HomeHeroTitle.FontSize = compact ? 34 : 40;
        LibraryHeroContent.Margin = compact ? new Thickness(25, 18, 0, 18) : new Thickness(34, 28, 0, 26);
        HomeGamePanel.Margin = compact ? new Thickness(20, 18, 20, 22) : new Thickness(26, 24, 26, 26);
        LibraryTitleArea.Margin = new Thickness(0, 0, 0, 15);
        HomeCoverShelf.Margin = new Thickness(0, 0, -16, 0);
        Grid.SetRow(HomeServerSection, stackLibrary ? 1 : 0);
        Grid.SetColumn(HomeServerSection, stackLibrary ? 0 : 1);
        Grid.SetColumnSpan(HomeServerSection, stackLibrary ? 2 : 1);
        Grid.SetColumnSpan(LibraryShelf, stackLibrary ? 2 : 1);
        LibraryShelf.Margin = new Thickness(0, 0, stackLibrary ? 0 : 22, 0);
        HomeServerSection.Margin = new Thickness(0, stackLibrary ? 22 : 0, 0, 0);
        SidebarColumn.Width = new GridLength(narrow ? 210 : 228);
        SidebarContent.Margin = narrow ? new Thickness(16, 20, 16, 12) : new Thickness(20, 28, 20, 16);
        WorkspaceContent.Margin = narrow ? new Thickness(18, 18, 18, 16) : new Thickness(30, 25, 30, 22);
        PageHeader.Margin = new Thickness(0, 0, 0, compact ? 12 : 20);
        SidebarNote.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        HeroRow.Height = new GridLength(compact ? 142 : 190);
        HeroContent.Margin = compact ? new Thickness(24, 12, 0, 12) : new Thickness(28, 22, 0, 20);
        HeroEyebrow.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        HeroTitle.FontSize = compact ? 25 : 28;
        HeroTitle.LineHeight = compact ? 30 : 38;
        HeroCopy.Margin = new Thickness(0, compact ? 9 : 12, 0, 0);
        var stackActions = WorkspaceContent.ActualWidth < 680;
        Grid.SetRow(SecondaryActions, stackActions ? 1 : 0);
        Grid.SetColumn(SecondaryActions, stackActions ? 0 : 1);
        Grid.SetColumnSpan(SecondaryActions, stackActions ? 2 : 1);
        SecondaryActions.HorizontalAlignment = stackActions ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        SecondaryActions.Margin = new Thickness(0, stackActions ? 8 : 0, 0, 0);
        var minimum = HeroRow.Height.Value + Math.Max(stackActions ? 130 : 78, ActionRow.DesiredSize.Height)
            + Math.Max(116, MetricsCards.DesiredSize.Height) + ConsolePanel.MinHeight + 4;
        OverviewContent.Height = Math.Max(minimum, OverviewScroll.ActualHeight);
        LocalizedUi.Set(MaximizeButton, "Content", () => WindowState == WindowState.Maximized ? "❐" : "□");
        LocalizedUi.Set(MaximizeButton, "ToolTip", () => WindowState == WindowState.Maximized ? Tr("Restaurar janela") : Tr("Maximizar"));
    }

    internal string PreviewResizeChecks()
    {
        if (!smoke) return "";
        var report = new StringBuilder();
        var originalWidth = Width; var originalHeight = Height; var originalState = WindowState;
        var page = Pages.SelectedIndex;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            report.AppendLine("PASS " + label);
        }
        void Settle() { ApplyResponsiveLayout(); UpdateLayout(); ApplyResponsiveLayout(); UpdateLayout(); }
        try
        {
            WindowState = WindowState.Normal;
            foreach (var size in new[] { new Size(960, 640), new Size(1100, 800), new Size(1280, 920), new Size(1600, 900) })
            {
                Width = size.Width; Height = size.Height; Navigate(0); Settle();
                Check(ConsolePanel.ActualHeight >= 200, $"console remains usable at {size}: {ConsolePanel.ActualHeight}");
                Check(SecondaryActions.ActualWidth <= WorkspaceContent.ActualWidth, $"actions fit at {size}");
                OverviewScroll.ScrollToBottom(); UpdateLayout();
                var command = CommandBox.TransformToAncestor(this).Transform(new Point());
                Check(command.Y >= 0 && command.Y + CommandBox.ActualHeight <= ActualHeight - 38, $"command reachable by scrolling at {size}");
                Navigate(3); Settle();
                var actions = SettingsActions.TransformToAncestor(this).Transform(new Point());
                Check(actions.Y >= 0 && actions.Y + SettingsActions.ActualHeight <= ActualHeight - 38, $"settings actions visible at {size}");
            }
            WindowState = WindowState.Maximized; Settle();
            Check(MaximizeButton.ToolTip?.ToString() == "Restaurar janela", "maximized window has restore action");
            Check(NativeWindowTheme.MaximizedWithinWorkArea(this), "maximized window stays within monitor work area and taskbar");
            WindowState = WindowState.Minimized;
            WindowState = WindowState.Normal; Settle();
            Check(!runner.IsActive && !tunnel.IsActive, "resize, minimize and restore never start server or tunnel");
        }
        finally
        {
            WindowState = WindowState.Normal; Width = originalWidth; Height = originalHeight;
            WindowState = originalState; Navigate(page); Settle(); OverviewScroll.ScrollToTop(); UpdateLayout();
        }
        return report.ToString();
    }
}
