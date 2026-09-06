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
        Assert.Equal("#CA662D", Tokens.ForewordSunriseHex);
    }

    // A paper-background app. Not one of the four FocusPhone heads: every one of them now
    // stands on its own identity ground, Plate included since its cover landed, so picking a
    // head here would test the flat-paper path against a spec that no longer uses it.
    private static IconSpec PaperSpec => IconSpecs.All.First(s => s.App == "Canonizer");
    private static IconSpec FlyleafSpec => IconSpecs.All.First(s => s.App == "FocusPhone.Launcher");
    private static IconSpec ForewordSpec => IconSpecs.All.First(s => s.App == "Foreword");

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
        Assert.Contains(IconSpecs.All, s => s.App == "Foreword" && s.Name == "ic_launcher");
        Assert.Contains(IconSpecs.All, s => s.App == "Ledger" && s.Name == "appicon");
    }

    // A spec with painted artwork reads it from the repo, so a scratch root needs a
    // stand-in — for the ground layer and for the plate set into the window alike. Any
    // decodable image will do; these tests are about what gets emitted, not what it
    // looks like.
    private void SeedGrounds()
    {
        var artwork = IconSpecs.All
            .SelectMany(spec => new[] { spec.GroundImage, spec.PlateImage })
            .OfType<string>()
            .Distinct();

        foreach (var relative in artwork)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var bitmap = new SKBitmap(8, 8);
            bitmap.Erase(SKColors.Teal);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(path, data.ToArray());
        }
    }

    // §12 requires a plate's framing to have both mirror lines: each corner is a
    // REFLECTION of the top-left, never a rotation, because a rotated asymmetric corner
    // produces a pinwheel with no mirror line and reads wrong.
    //
    // What this asserts is the mirror lines themselves, which is the visible property.
    // It does NOT distinguish reflection from rotation for Post specifically, and cannot:
    // a mitred bracket with equal arms is symmetric about its own diagonal, so the two
    // give pixel-identical results. Verified by forcing a rotation, which this test
    // passes. The distinction bites for an asymmetric motif — the default quarter-curve —
    // and this catches the thing that actually goes wrong either way: framing that has
    // drifted off-centre and no longer mirrors at all.
    //
    // Checked outside the illustration window, since the painting inside it is not
    // symmetric and is not meant to be.
    [Fact]
    public void PlateFramingMirrorsOnBothAxes()
    {
        var spec = IconSpecs.All.Single(s => s.App == "Post");
        using var foreground = Foreground(spec);

        // Compared with a tolerance rather than exactly. Anti-aliasing rounds a stroke's
        // edge alpha differently on the two sides of a mirror line — a unit or two, on
        // hundreds of pixels — which is invisible and is not what this test is about. A
        // rotated corner puts cream where there should be nothing, a difference of 250.
        const int Tolerance = 16;
        var window = foreground.Width * spec.PlateInset;
        var mismatches = 0;
        for (var y = 0; y < foreground.Height / 2; y++)
        {
            for (var x = 0; x < foreground.Width / 2; x++)
            {
                if (x > window && y > window)
                {
                    continue; // inside the window: the art, which has no mirror line
                }
                var here = foreground.GetPixel(x, y);
                if (Differs(here, foreground.GetPixel(foreground.Width - 1 - x, y), Tolerance)
                    || Differs(here, foreground.GetPixel(x, foreground.Height - 1 - y), Tolerance))
                {
                    mismatches++;
                }
            }
        }

        Assert.True(mismatches < 50,
            $"{mismatches} pixels outside the window break the plate's mirror lines — the framing has drifted off-centre.");
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

    [Fact]
    public void ForewordBackground_IsTheSunriseIdentityGround()
    {
        var spec = ForewordSpec;
        SeedGrounds();
        AdaptiveIconRenderer.Write(root, spec);
        var xml = File.ReadAllText(Path.Combine(
            root, spec.ResourceRoot.Replace('/', Path.DirectorySeparatorChar),
            "drawable", $"{spec.Name}_background.xml"));

        Assert.Contains(Tokens.ForewordSunriseHex, xml);
        Assert.DoesNotContain("gradient", xml);
    }

    /// <summary>
    /// Foreword's cover sits in a far larger window than Post's, and that is the whole
    /// difference between the two icons. The engraving is the subject: at Post's 0.325 inset
    /// the doorstep, the newspaper and the door all land inside a third of the tile and read
    /// as a smudge at 48dp.
    /// </summary>
    [Fact]
    public void ForewordSetsItsCoverIntoAMuchLargerWindowThanPost()
    {
        var post = IconSpecs.All.Single(s => s.App == "Post");

        Assert.Equal(0.12f, ForewordSpec.PlateInset);
        Assert.True(ForewordSpec.PlateInset < post.PlateInset / 2f,
            "Foreword's window has drifted back toward Post's, which is what made the engraving illegible.");
    }

    /// <summary>
    /// And why it has no framing. At a 0.12 inset the corners are far outside an adaptive
    /// icon's round-mask safe zone, so brackets there would be sliced off — leaving the plate
    /// with broken framing rather than none, which reads as a rendering bug. The frame goes so
    /// the picture can be big.
    /// </summary>
    [Fact]
    public void ForewordsPlateDrawsNoFramingOutsideItsWindow()
    {
        using var foreword = GlyphOnly(ForewordSpec);
        using var post = GlyphOnly(IconSpecs.All.Single(s => s.App == "Post"));

        Assert.True(InkOutsideWindow(post, IconSpecs.All.Single(s => s.App == "Post").PlateInset) > 0,
            "Post is the control here: it is supposed to have framing outside its window.");
        Assert.Equal(0, InkOutsideWindow(foreword, ForewordSpec.PlateInset));
    }

    // The glyph layer alone, with no ground and no plate art beneath it.
    private static SKBitmap GlyphOnly(IconSpec spec)
    {
        var bitmap = new SKBitmap(432, 432, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            spec.Glyph(canvas, 432f);
        }
        return bitmap;
    }

    // Anything drawn beyond the window's own keyline — a frame, brackets, a rule.
    private static int InkOutsideWindow(SKBitmap glyph, float inset)
    {
        // A pixel of slack around the keyline itself, which is drawn ON the window edge.
        var edge = glyph.Width * inset;
        var slack = glyph.Width * 0.02f;
        var count = 0;
        for (var y = 0; y < glyph.Height; y++)
        {
            for (var x = 0; x < glyph.Width; x++)
            {
                var outside = x < edge - slack || y < edge - slack
                    || x > glyph.Width - edge + slack || y > glyph.Height - edge + slack;
                if (outside && glyph.GetPixel(x, y).Alpha > 8)
                {
                    count++;
                }
            }
        }
        return count;
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
        // Score, Post, Plate and Foreword are deliberately not in this set. Each one's icon IS
        // its cover — a keyboard on a painted ground, a mailbox painting set into an indigo
        // plate, a lens set into a mauve one, an engraved doorstep set into a sunrise one —
        // rather than an ink glyph on paper, so none has an ink line to count or a terracotta
        // detail to find. Their covers are checked by their own tests instead.
        //
        // Plate joined them when its cover landed, and the pixel this test stopped finding is
        // the point: it was counting the terracotta dot on Glyphs.Lens, the pre-rebrand Camera
        // icon in an accent P4 retired. DESIGN.md §12 says a cover carries "no single accent
        // detail", so a cover that still had one would be the bug.
        foreach (var spec in IconSpecs.All.Where(s => s.App is not ("Score" or "Post" or "Plate" or "Foreword")))
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

    // A plate's corner framing has to survive a ROUND mask, which is the tighter of the two
    // an Android launcher may apply. A corner sits at sqrt(2) x (0.5 - inset) from the
    // centre while the round mask keeps only radius 0.333 of the layer, so framing that
    // looks correctly placed on a square icon can be cut away entirely on a round one —
    // leaving the painting in a window with nothing around it, and no error anywhere.
    [Fact]
    public void PlateFramingSurvivesARoundMask()
    {
        var spec = IconSpecs.All.Single(s => s.App == "Post");
        using var foreground = Foreground(spec);

        var size = foreground.Width;
        var centre = size / 2f;
        var maskRadius = size * (72f / 108f) / 2f;
        var window = size * spec.PlateInset;

        // Only the four CORNER squares count. An earlier version of this counted anything
        // outside the window, which meant it was really counting the window keyline —
        // that runs through the middle of each edge, is always well inside the mask, and
        // survives however far out the corners have drifted. It passed happily with the
        // corners entirely outside the circle.
        var framingInsideTheMask = 0;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var inACorner = (x < window || x > size - window) && (y < window || y > size - window);
                if (!inACorner)
                {
                    continue;
                }
                var distance = Math.Sqrt(((x - centre) * (x - centre)) + ((y - centre) * (y - centre)));
                if (distance <= maskRadius && foreground.GetPixel(x, y).Alpha > 200)
                {
                    framingInsideTheMask++;
                }
            }
        }

        Assert.True(framingInsideTheMask > 200,
            $"Only {framingInsideTheMask} corner pixels fall inside a round mask — the plate's corners "
            + "are outside it and would be cut away completely on a launcher that draws round icons.");
    }

    private static bool Differs(SKColor a, SKColor b, int tolerance) =>
        Math.Abs(a.Red - b.Red) > tolerance
        || Math.Abs(a.Green - b.Green) > tolerance
        || Math.Abs(a.Blue - b.Blue) > tolerance
        || Math.Abs(a.Alpha - b.Alpha) > tolerance;

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
