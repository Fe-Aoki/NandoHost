using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NandoHost.App;

// Static, shared UI artwork: frozen vector icons/texture and a bundled banner
// decoded once at a bounded resolution. No web view or animation loop.
internal static class ThemeArt
{
    internal static void Register(ResourceDictionary resources)
    {
        resources["DirtBrush"] = Dirt();
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/NandoHost;component/Assets/Images/minecraft-valley.png");
        image.DecodePixelWidth = 1600;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit(); image.Freeze();
        var landscape = new ImageBrush(image) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right, AlignmentY = AlignmentY.Center };
        landscape.Freeze();
        resources["LandscapeBrush"] = landscape;
        resources["GrassBlockBrush"] = GrassBlock();
        resources["MinecraftGameImage"] = LoadBrandImage("minecraft-game.png", 260);
        resources["TerrariaGameImage"] = LoadBrandImage("terraria-tree.png", 260);
        resources["TerrariaSceneBrush"] = Scene("terraria-forest.png");
        resources["CalamitySceneBrush"] = Scene("calamity-dusk.png");
        resources["TerrariaSkyBrush"] = Sky("terraria-sky.png");
        resources["CalamitySkyBrush"] = Sky("calamity-sky.png");
    }

    private static DrawingBrush Sky(string name)
    {
        var image = LoadBrandImage(name, 1600);
        var bounds = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
        var shade = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(0, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop((Color)ColorConverter.ConvertFromString("#8A07101F"), 0),
                new GradientStop((Color)ColorConverter.ConvertFromString("#4007101F"), 0.4),
                new GradientStop((Color)ColorConverter.ConvertFromString("#9507101F"), 1)
            }
        };
        shade.Freeze();
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.DrawImage(image, bounds);
            dc.DrawRectangle(shade, null, bounds);
        }
        drawing.Freeze();
        var brush = new DrawingBrush(drawing)
        {
            Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Center, AlignmentX = AlignmentX.Center
        };
        brush.Freeze(); return brush;
    }

    private static ImageBrush Scene(string name)
    {
        var brush = new ImageBrush(LoadBrandImage(name, 1600)) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right, AlignmentY = AlignmentY.Center };
        brush.Freeze(); return brush;
    }

    private static BitmapImage LoadBrandImage(string name, int width)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://application:,,,/NandoHost;component/Assets/Images/" + name);
        image.DecodePixelWidth = width;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit(); image.Freeze();
        return image;
    }
    private static SolidColorBrush Color(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze(); return brush;
    }
    private static DrawingBrush Finish(DrawingGroup drawing, Rect bounds)
    {
        drawing.Freeze();
        var brush = new DrawingBrush(drawing) { Viewbox = bounds, ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.UniformToFill };
        brush.Freeze(); return brush;
    }
    private static DrawingBrush Dirt()
    {
        var drawing = new DrawingGroup();
        var palette = new[] { "#31251F", "#372A22", "#3D2E25", "#423127", "#48362B", "#302721", "#4B392D", "#3E3228" };
        var random = new Random(1986);
        using (var dc = drawing.Open())
        {
            for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                    dc.DrawRectangle(Color(palette[random.Next(palette.Length)]), null, new Rect(x * 3, y * 3, 3, 3));
            dc.DrawRectangle(Color("#625348"), null, new Rect(9, 12, 6, 3));
            dc.DrawRectangle(Color("#55483D"), null, new Rect(33, 30, 3, 6));
        }
        drawing.Freeze();
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 48, 48), ViewportUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill };
        brush.Freeze(); return brush;
    }

    private static void Polygon(DrawingContext dc, string color, params double[] xy)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(new Point(xy[0], xy[1]), true, true);
            for (var i = 2; i < xy.Length; i += 2) path.LineTo(new Point(xy[i], xy[i + 1]), true, false);
        }
        geometry.Freeze(); dc.DrawGeometry(Color(color), null, geometry);
    }
    private static void Block(DrawingContext dc, double x, double y, double size)
    {
        Polygon(dc, "#82A448", x, y, x + size, y - size / 2, x + size * 2, y, x + size, y + size / 2);
        Polygon(dc, "#65503A", x, y, x + size, y + size / 2, x + size, y + size * 1.5, x, y + size);
        Polygon(dc, "#433C29", x + size, y + size / 2, x + size * 2, y, x + size * 2, y + size, x + size, y + size * 1.5);
        Polygon(dc, "#587B35", x, y, x + size, y + size / 2, x + size, y + size * .7, x, y + size * .2);
        Polygon(dc, "#3D5E2D", x + size, y + size / 2, x + size * 2, y, x + size * 2, y + size * .2, x + size, y + size * .7);
        dc.DrawRectangle(Color("#8D6C46"), null, new Rect(x + size * .2, y + size * .5, size * .2, size * .15));
        dc.DrawRectangle(Color("#3F3529"), null, new Rect(x + size * .58, y + size * .87, size * .2, size * .18));
    }
    private static DrawingBrush GrassBlock()
    {
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open()) Block(dc, 0, 16, 31);
        return Finish(drawing, new Rect(0, 0, 62, 63));
    }
}
