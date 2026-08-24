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
        Assert.Equal("#C9803A", Tokens.FlyleafWarmHex);
    }

    // A paper-background app (not Flyleaf, whose ground is the warm identity hue).
    private static IconSpec PaperSpec => IconSpecs.All.First(s => s.App == "FocusPhone.Camera");
    private static IconSpec FlyleafSpec => IconSpecs.All.First(s => s.App == "FocusPhone.Launcher");

    [Fact]
    public void SpecsCoverAllApps()
    {
        Assert.Equal(7, IconSpecs.All.Count);

        // Counted by where the resources land, not by what the app is called. The
        // phase renames rebrand one head at a time — Launcher became Flyleaf, Audio
        // became Score — so a "FocusPhone.*" name prefix stops being true while the
        // four heads under FocusPhone/ still all need an icon.
        Assert.Equal(4, IconSpecs.All.Count(s => s.ResourceRoot.StartsWith("FocusPhone/")));
        Assert.Equal(IconSpecs.All.Count, IconSpecs.All.Select(s => s.ResourceRoot).Distinct().Count());
        Assert.Contains(IconSpecs.All, s => s.App == "Canonizer" && s.Name == "appicon");
        Assert.Contains(IconSpecs.All, s => s.App == "DailyLoader" && s.Name == "ic_launcher");
        Assert.Contains(IconSpecs.All, s => s.App == "Ledger" && s.Name == "appicon");
    }

    // A spec with a painted ground reads it from the repo, so a scratch root needs a
    // stand-in. Any decodable image will do — these tests are about what gets
    // emitted, not what it looks like.
    private void SeedGrounds()
    {
        foreach (var spec in IconSpecs.All.Where(s => s.GroundImage is not null))
        {
            var path = Path.Combine(root, spec.GroundImage!.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var bitmap = new SKBitmap(8, 8);
            bitmap.Erase(SKColors.Teal);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, data.ToArray());
        }
    }

    [Fact]
    public void Write_EmitsTheFullResourceSetPerApp()
    {
        SeedGrounds();
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

                // A painted ground is a per-density bitmap layer; a flat one is the
                // shape drawable, and must not also emit one.
                Assert.Equal(spec.GroundImage is not null,
                    File.Exists(Path.Combine(resourceRoot, $"mipmap-{dpi}", $"{spec.Name}_ground.png")));
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
        Assert.Contains(Tokens.FlyleafWarmHex, xml);
        Assert.DoesNotContain("gradient", xml);
        // The foreground bookplate lays a cream cover over that ground.
        using var legacy = SKBitmap.Decode(Path.Combine(
            root, spec.ResourceRoot.Replace('/', Path.DirectorySeparatorChar),
            "mipmap-xxxhdpi", $"{spec.Name}.png"));
        Assert.Equal(Tokens.FlyleafWarm, legacy.GetPixel(2, 2));
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
    public void EveryForegroundKeepsItsCornersTransparent()
    {
        SeedGrounds();
        foreach (var spec in IconSpecs.All)
        {
            using var bitmap = Foreground(spec);

            // Corners are outside the safe zone and must stay empty, or a launcher
            // mask clips artwork rather than padding.
            Assert.Equal(0, bitmap.GetPixel(4, 4).Alpha);
            Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 5, bitmap.Height - 5).Alpha);
        }
    }

    [Fact]
    public void LineArtGlyphsDrawInkAndExactlyOneTerracottaDetail()
    {
        // Score is deliberately not in this set. Its icon is its cover — a keyboard
        // on a painted ground — rather than an ink glyph on paper, so it has neither
        // an ink line to count nor an accent detail to find.
        foreach (var spec in IconSpecs.All.Where(s => s.App != "Score"))
        {
            using var bitmap = Foreground(spec);
            var (ink, accent) = CountPalettePixels(bitmap);
            Assert.True(ink > 100, $"{spec.App}: expected ink glyph pixels, found {ink}");
            Assert.True(accent > 20, $"{spec.App}: expected one terracotta detail, found {accent} accent pixels");
        }
    }

    // The thing an image model could not do, asserted: within an octave the black
    // keys group two then three, with the wide gaps at E–F and B–C. Counting the
    // dark runs along the back of the keyboard is how that shows up in pixels.
    [Fact]
    public void ScoreDrawsAMusicallyCorrectKeyboard()
    {
        using var bitmap = Foreground(IconSpecs.All.First(s => s.App == "Score"));

        // Counted as blobs, not scanned along a line. The keyboard curves, so neither
        // a row nor a column crosses it cleanly — a column through a steep stretch
        // passes through several keys at once. Connected regions of ink are the keys
        // themselves, independent of where the curve happens to put them.
        var keys = InkBlobs(bitmap);

        // Twenty-two white keys spans three octaves exactly, and an octave carries
        // five black keys — grouped two then three, with the wide gaps at E–F and
        // B–C. That grouping is the whole reason this is drawn instead of painted.
        Assert.Equal(15, keys);

        // And the keys themselves are cream, not white — paper, not plastic (§2).
        var cream = 0;
        for (var x = 0; x < bitmap.Width; x += 2)
        {
            for (var yy = 0; yy < bitmap.Height; yy += 2)
            {
                if (bitmap.GetPixel(x, yy) == Tokens.Paper)
                {
                    cream++;
                }
            }
        }

        Assert.True(cream > 500, $"expected cream keys, found {cream} paper pixels");
    }

    // Four-way flood fill over full-strength ink. The seams between white keys are
    // the same colour at half alpha and are deliberately not counted — they are not
    // keys.
    private static int InkBlobs(SKBitmap bitmap)
    {
        var seen = new bool[bitmap.Width, bitmap.Height];
        var blobs = 0;

        bool IsInk(int x, int y)
        {
            var p = bitmap.GetPixel(x, y);
            return p.Alpha > 200 && p.Red < 80 && p.Green < 80 && p.Blue < 80;
        }

        for (var x = 0; x < bitmap.Width; x++)
        {
            for (var y = 0; y < bitmap.Height; y++)
            {
                if (seen[x, y] || !IsInk(x, y))
                {
                    continue;
                }

                var size = 0;
                var stack = new Stack<(int X, int Y)>();
                stack.Push((x, y));
                seen[x, y] = true;
                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    size++;
                    foreach (var (nx, ny) in new[] { (cx - 1, cy), (cx + 1, cy), (cx, cy - 1), (cx, cy + 1) })
                    {
                        if (nx >= 0 && ny >= 0 && nx < bitmap.Width && ny < bitmap.Height
                            && !seen[nx, ny] && IsInk(nx, ny))
                        {
                            seen[nx, ny] = true;
                            stack.Push((nx, ny));
                        }
                    }
                }

                // Ignore anti-aliasing specks along an edge; a key is hundreds of pixels.
                if (size > 100)
                {
                    blobs++;
                }
            }
        }

        return blobs;
    }

    private SKBitmap Foreground(IconSpec spec)
    {
        SeedGrounds();
        AdaptiveIconRenderer.Write(root, spec);
        return SKBitmap.Decode(Path.Combine(root, spec.ResourceRoot.Replace('/', Path.DirectorySeparatorChar),
            "mipmap-xxxhdpi", $"{spec.Name}_foreground.png"));
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
