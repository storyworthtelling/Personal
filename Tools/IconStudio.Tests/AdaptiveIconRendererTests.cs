using IconStudio;
using SkiaSharp;

namespace IconStudio.Tests;

public class AdaptiveIconRendererTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "iconstudio-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PaletteMatchesTheDesignBible()
    {
        Assert.Equal("#FAF8F2", Tokens.PaperHex);
        Assert.Equal("#33322B", Tokens.InkHex);
        Assert.Equal("#C2542F", Tokens.AccentHex);
        Assert.Equal("#C9803A", Tokens.IdentityWarmHex);
    }

    // A paper-background app (not Flyleaf, whose ground is the warm identity hue).
    private static IconSpec PaperSpec => IconSpecs.All.First(s => s.App == "FocusPhone.Camera");
    private static IconSpec FlyleafSpec => IconSpecs.All.First(s => s.App == "FocusPhone.Launcher");

    [Fact]
    public void SpecsCoverAllApps()
    {
        Assert.Equal(7, IconSpecs.All.Count);
        Assert.Equal(4, IconSpecs.All.Count(s => s.App.StartsWith("FocusPhone.")));
        Assert.Contains(IconSpecs.All, s => s.App == "Canonizer" && s.Name == "appicon");
        Assert.Contains(IconSpecs.All, s => s.App == "DailyLoader" && s.Name == "ic_launcher");
        Assert.Contains(IconSpecs.All, s => s.App == "Ledger" && s.Name == "appicon");
    }

    [Fact]
    public void Write_EmitsTheFullResourceSetPerApp()
    {
        foreach (var spec in IconSpecs.All)
        {
            var files = AdaptiveIconRenderer.Write(root, spec);
            var resourceRoot = Path.Combine(root, spec.ResourceRoot.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(Path.Combine(resourceRoot, "mipmap-anydpi-v26", $"{spec.Name}.xml")));
            Assert.True(File.Exists(Path.Combine(resourceRoot, "mipmap-anydpi-v26", $"{spec.Name}_round.xml")));
            Assert.True(File.Exists(Path.Combine(resourceRoot, "drawable", $"{spec.Name}_background.xml")));
            foreach (var dpi in new[] { "mdpi", "hdpi", "xhdpi", "xxhdpi", "xxxhdpi" })
            {
                Assert.True(File.Exists(Path.Combine(resourceRoot, $"mipmap-{dpi}", $"{spec.Name}_foreground.png")));
                Assert.True(File.Exists(Path.Combine(resourceRoot, $"mipmap-{dpi}", $"{spec.Name}.png")));
            }
            Assert.Equal(spec.WritesColorsXml,
                File.Exists(Path.Combine(resourceRoot, "values", "colors.xml")));
            Assert.All(files, f => Assert.True(File.Exists(f)));
        }
    }

    [Fact]
    public void BackgroundXml_IsFlatPaper()
    {
        var spec = PaperSpec;
        AdaptiveIconRenderer.Write(root, spec);
        var xml = File.ReadAllText(Path.Combine(
            root, spec.ResourceRoot.Replace('/', Path.DirectorySeparatorChar),
            "drawable", $"{spec.Name}_background.xml"));
        Assert.Contains(Tokens.PaperHex, xml);
        Assert.DoesNotContain("gradient", xml);
    }

    [Fact]
    public void FlyleafBackground_IsTheWarmIdentityGround()
    {
        var spec = FlyleafSpec;
        AdaptiveIconRenderer.Write(root, spec);
        var xml = File.ReadAllText(Path.Combine(
            root, spec.ResourceRoot.Replace('/', Path.DirectorySeparatorChar),
            "drawable", $"{spec.Name}_background.xml"));
        Assert.Contains(Tokens.IdentityWarmHex, xml);
        Assert.DoesNotContain("gradient", xml);
        // The foreground bookplate lays a cream cover over that ground.
        using var legacy = SKBitmap.Decode(Path.Combine(
            root, spec.ResourceRoot.Replace('/', Path.DirectorySeparatorChar),
            "mipmap-xxxhdpi", $"{spec.Name}.png"));
        Assert.Equal(Tokens.IdentityWarm, legacy.GetPixel(2, 2));
    }

    [Theory]
    [InlineData("mdpi", 108, 48)]
    [InlineData("xxxhdpi", 432, 192)]
    public void Pngs_HaveDensityCorrectDimensions(string dpi, int foregroundPx, int legacyPx)
    {
        var spec = IconSpecs.All[0];
        AdaptiveIconRenderer.Write(root, spec);
        var resourceRoot = Path.Combine(root, spec.ResourceRoot.Replace('/', Path.DirectorySeparatorChar));

        using var foreground = SKBitmap.Decode(Path.Combine(resourceRoot, $"mipmap-{dpi}", $"{spec.Name}_foreground.png"));
        Assert.Equal(foregroundPx, foreground.Width);
        Assert.Equal(foregroundPx, foreground.Height);

        using var legacy = SKBitmap.Decode(Path.Combine(resourceRoot, $"mipmap-{dpi}", $"{spec.Name}.png"));
        Assert.Equal(legacyPx, legacy.Width);
        Assert.Equal(legacyPx, legacy.Height);
    }

    [Fact]
    public void Foregrounds_KeepCornersTransparent_AndDrawInkPlusAccent()
    {
        foreach (var spec in IconSpecs.All)
        {
            AdaptiveIconRenderer.Write(root, spec);
            var path = Path.Combine(root, spec.ResourceRoot.Replace('/', Path.DirectorySeparatorChar),
                "mipmap-xxxhdpi", $"{spec.Name}_foreground.png");
            using var bitmap = SKBitmap.Decode(path);

            // Corners are outside the safe zone and must stay empty.
            Assert.Equal(0, bitmap.GetPixel(4, 4).Alpha);
            Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 5, bitmap.Height - 5).Alpha);

            var (ink, accent) = CountPalettePixels(bitmap);
            Assert.True(ink > 100, $"{spec.App}: expected ink glyph pixels, found {ink}");
            Assert.True(accent > 20, $"{spec.App}: expected one terracotta detail, found {accent} accent pixels");
        }
    }

    [Fact]
    public void LegacyIcons_SitOnPaper()
    {
        var spec = PaperSpec;
        AdaptiveIconRenderer.Write(root, spec);
        var path = Path.Combine(root, spec.ResourceRoot.Replace('/', Path.DirectorySeparatorChar),
            "mipmap-xxxhdpi", $"{spec.Name}.png");
        using var bitmap = SKBitmap.Decode(path);
        Assert.Equal(Tokens.Paper, bitmap.GetPixel(2, 2));
    }

    [Fact]
    public void Output_IsDeterministic()
    {
        var spec = IconSpecs.All[0];
        var first = Path.Combine(root, "first");
        var second = Path.Combine(root, "second");
        AdaptiveIconRenderer.Write(first, spec);
        AdaptiveIconRenderer.Write(second, spec);

        foreach (var file in Directory.EnumerateFiles(first, "*", SearchOption.AllDirectories))
        {
            var twin = Path.Combine(second, Path.GetRelativePath(first, file));
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(twin));
        }
    }

    private static (int Ink, int Accent) CountPalettePixels(SKBitmap bitmap)
    {
        var ink = 0;
        var accent = 0;
        for (var x = 0; x < bitmap.Width; x += 2)
        {
            for (var y = 0; y < bitmap.Height; y += 2)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel == Tokens.Ink)
                {
                    ink++;
                }
                else if (pixel == Tokens.Accent)
                {
                    accent++;
                }
            }
        }
        return (ink, accent);
    }
}
