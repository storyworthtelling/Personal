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

    // A paper-background app: none of the seven shipped apps take this path any more — every
    // FocusPhone head stands on its own identity ground, Foreword on its sunrise, and Folio and
    // Index joined them when their covers landed. Rather than let AdaptiveIconRenderer's
    // flat-paper branch lose its test coverage entirely, this is a synthetic spec built only for
    // these tests — not one IconSpecs.All registers — that exercises it directly, drawing the
    // folded-paper glyph as a stand-in probe (the retired Ledger glyph used to serve).
    private static IconSpec PaperSpec => new(
        "FlatPaperProbe", "IconStudio.Tests/FlatPaperProbe", "appicon", false, Glyphs.FoldedPaper);
    private static IconSpec FlyleafSpec => IconSpecs.All.First(s => s.App == "FocusPhone.Launcher");
    private static IconSpec ForewordSpec => IconSpecs.All.First(s => s.App == "Foreword");

    [Fact]
    public void SpecsCoverAllApps()
    {
        Assert.Equal(8, IconSpecs.All.Count);

        // Counted by where the resources land, not by what the app is called. The
        // phase renames rebrand one head at a time — Launcher became Flyleaf, Audio
        // became Score — so a "FocusPhone.*" name prefix stops being true while the
        // four heads under FocusPhone/ still all need an icon.
        Assert.Equal(4, IconSpecs.All.Count(s => s.ResourceRoot.StartsWith("FocusPhone/")));
        Assert.Equal(IconSpecs.All.Count, IconSpecs.All.Select(s => s.ResourceRoot).Distinct().Count());
        Assert.Contains(IconSpecs.All, s => s.App == "Folio" && s.Name == "appicon");
        Assert.Contains(IconSpecs.All, s => s.App == "Foreword" && s.Name == "ic_launcher");
        Assert.Contains(IconSpecs.All, s => s.App == "Index" && s.Name == "appicon");
        Assert.Contains(IconSpecs.All, s => s.App == "Pulp" && s.Name == "appicon");
    }

    // D266/D267: Pulp's icon is the den painting filling the mask — no keyline, no amber border —
    // set at the shared full-bleed inset so the round mask still holds the screen and the popcorn
    // together; the shelf cover is the same painting on the amber cloth.
    [Fact]
    public void PulpIsTheDenPaintingFullBleedWithNoKeyline()
    {
        var pulp = Assert.Single(IconSpecs.All, s => s.App == "Pulp");
        Assert.Equal("Pulp/Pulp.Android/Resources", pulp.ResourceRoot);
        Assert.Equal("Branding/pulp-cover.jpg", pulp.PlateImage);
        Assert.Equal(Glyphs.FullBleedInset, pulp.PlateInset);
        Assert.Equal(Tokens.PulpClothHex, pulp.BackgroundHex);
        Assert.Equal("FocusPhone/Flyleaf.Android/Resources/drawable/cover_pulp.png", pulp.CoverImage);

        // Nothing drawn over the art: the glyph leaves a blank canvas blank.
        using var bitmap = new SKBitmap(64, 64);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            pulp.Glyph(canvas, 64);
        }
        Assert.All(bitmap.Pixels, pixel => Assert.Equal(0, pixel.Alpha));
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
    /// Every cover icon sets its art into the same wide window, and none of them draws framing
    /// outside it.
    ///
    /// <para>This used to be Foreword's exception, tested against Post as the control: Foreword
    /// took a 0.12 inset and no brackets so its engraving could be big, while Post kept a 0.325
    /// window inside mitred corners. The owner chose Foreword's shape for all of them, so the
    /// control is gone and the exception is the rule — which is why the two tests that named Post
    /// as the small-window case are this one test now.</para>
    ///
    /// <para>The reason survives the change. At this inset the corners are far outside an adaptive
    /// icon's round-mask safe zone, so brackets there would be sliced off — leaving broken framing
    /// rather than none, which reads as a rendering fault. The frame goes so the picture can be
    /// big.</para>
    /// </summary>
    [Fact]
    public void EveryCoverIconIsFullBleedAndFramesNothingOutsideItsWindow()
    {
        // Index is a named exception (D119): its corner flourishes are drawn outside the window
        // on purpose — see IndexFlourishesAreOutsideTheWindowAndMirrorOnBothAxes below, which
        // covers exactly what this generic assertion cannot for it. Every other cover keeps the
        // keyline-only shape this test still holds them to.
        var covers = IconSpecs.All.Where(spec => spec.PlateImage is not null && spec.App != "Index").ToList();

        Assert.NotEmpty(covers);
        foreach (var spec in covers)
        {
            Assert.Equal(Glyphs.FullBleedInset, spec.PlateInset);

            using var glyph = GlyphOnly(spec);
            Assert.Equal(0, InkOutsideWindow(glyph, spec.PlateInset));
        }
    }

    /// <summary>
    /// Index's own version of the test above: its flourishes must sit OUTSIDE the window (that
    /// is the whole point of D119) but must not cross into it, and each corner must be the
    /// top-left's reflection rather than an independent shape or a rotation.
    /// </summary>
    [Fact]
    public void IndexFlourishesAreOutsideTheWindowAndMirrorOnBothAxes()
    {
        var spec = IconSpecs.All.Single(s => s.App == "Index");
        using var glyph = GlyphOnly(spec);

        // The flourishes are there: ink outside the window, where the generic test above
        // requires none for every other cover.
        Assert.True(InkOutsideWindow(glyph, spec.PlateInset) > 0,
            "expected Index's corner flourishes outside the window; found none");

        // But the window itself stays clear — the flourish never reaches in far enough to
        // touch the art (D119: "the window inset to clear them").
        var edge = (int)(glyph.Width * spec.PlateInset);
        Assert.Equal(0, glyph.GetPixel(edge + 4, edge + 4).Alpha);
        Assert.Equal(0, glyph.GetPixel(glyph.Width - edge - 5, edge + 4).Alpha);
        Assert.Equal(0, glyph.GetPixel(edge + 4, glyph.Height - edge - 5).Alpha);
        Assert.Equal(0, glyph.GetPixel(glyph.Width - edge - 5, glyph.Height - edge - 5).Alpha);

        // Reflection, not rotation: compared over the outer margin only, the same tolerance
        // and reasoning PlateFramingMirrorsOnBothAxes uses for Post.
        const int Tolerance = 16;
        var mismatches = 0;
        for (var y = 0; y < glyph.Height / 2; y++)
        {
            for (var x = 0; x < glyph.Width / 2; x++)
            {
                if (x > edge && y > edge)
                {
                    continue; // inside the window
                }
                var here = glyph.GetPixel(x, y);
                if (Differs(here, glyph.GetPixel(glyph.Width - 1 - x, y), Tolerance)
                    || Differs(here, glyph.GetPixel(x, glyph.Height - 1 - y), Tolerance))
                {
                    mismatches++;
                }
            }
        }

        Assert.True(mismatches < 50,
            $"{mismatches} pixels break the flourish's mirror lines — a corner has drifted or rotated.");
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
        // Score, Post, Plate, Foreword, Folio and Index are deliberately not in this set. Each
        // one's icon IS its cover — a keyboard on a painted ground, a mailbox painting set into
        // an indigo plate, a lens set into a mauve one, an engraved doorstep set into a sunrise
        // one, a coin staircase set into a bookplate-green one — rather than an ink glyph on
        // paper, so none has an ink line to count or a terracotta detail to find. Their covers
        // are checked by their own tests instead.
        //
        // Plate joined them when its cover landed, and the pixel this test stopped finding is
        // the point: it was counting the terracotta dot on Glyphs.Lens, the pre-rebrand Camera
        // icon in an accent P4 retired. DESIGN.md §12 says a cover carries "no single accent
        // detail", so a cover that still had one would be the bug. Index joined them the same
        // way: its keyline and flourish are cream, not ink, and carry no terracotta at all. Pulp
        // draws nothing over its painting, so it has no line at all.
        foreach (var spec in IconSpecs.All.Where(
            s => s.App is not ("Score" or "Post" or "Plate" or "Foreword" or "Folio" or "Index" or "Pulp")))
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

    /// <summary>
    /// A cover icon's art has to reach the edge of the ROUND mask, which is the tighter of the two
    /// an Android launcher may apply.
    ///
    /// <para>This used to assert the opposite thing about the same geometry: that the mitred corner
    /// brackets fell <i>inside</i> the mask, because framing which looks correctly placed on a
    /// square icon can be cut away entirely on a round one. The brackets are gone now, so what is
    /// left to protect is the reason they went — the picture is meant to fill the circle. The mask
    /// keeps the central 72 of 108, so anything inside a radius of 0.333 is on screen, and at a
    /// 0.12 inset the art covers all of it.</para>
    ///
    /// <para>Widening the window back would put the identity cloth inside that circle and make each
    /// icon a small painting on a coloured ground again — visibly, but with nothing failing.</para>
    /// </summary>
    [Fact]
    public void ACoverIconsArtFillsTheRoundMask()
    {
        foreach (var spec in IconSpecs.All.Where(spec => spec.PlateImage is not null))
        {
            // The art's own rect, and the circle a round launcher keeps, both as fractions.
            var window = spec.PlateInset;
            const float maskRadius = 0.333f;

            // The window's nearest edge to the centre must be at least the mask's radius away, or
            // the ground shows between the art and the edge of the icon.
            Assert.True(0.5f - window >= maskRadius,
                $"{spec.App}: its art stops {0.5f - window:F3} from the centre while the round mask "
                + $"reaches {maskRadius}, so the identity cloth shows inside the circle.");
        }
    }

    /// <summary>
    /// A shelf cover is WINDOW ONLY: the painting and nothing else, for every spec that has one.
    ///
    /// <para>The shelf owns the cloth, the fine rule, the title band and the corner brackets, so
    /// any cloth, keyline or flourish baked into the cover PNG is a second copy of them at a
    /// different scale. Index's cover used to run the icon's own construction to pick its corner
    /// flourishes up and doubled the shelf's. The flourishes stay on the launcher icon, where the
    /// round mask crops them; they have no business in the cover.</para>
    ///
    /// <para>The art is seeded as flat teal, so anything that is not teal in the written cover was
    /// drawn over the painting.</para>
    /// </summary>
    [Fact]
    public void EveryShelfCoverIsTheBarePaintingWithNothingDrawnOverIt()
    {
        var covers = IconSpecs.All
            .Where(spec => spec.CoverImage is not null && spec.PlateImage is not null)
            .ToList();
        Assert.Contains(covers, spec => spec.App == "Index");

        foreach (var spec in covers)
        {
            SeedGrounds();
            AdaptiveIconRenderer.Write(root, spec);

            using var cover = SKBitmap.Decode(Path.Combine(root, spec.CoverImage!.Replace('/', Path.DirectorySeparatorChar)));
            var stray = 0;
            for (var y = 0; y < cover.Height; y++)
            {
                for (var x = 0; x < cover.Width; x++)
                {
                    if (cover.GetPixel(x, y) != SKColors.Teal)
                    {
                        stray++;
                    }
                }
            }

            Assert.True(stray == 0,
                $"{spec.App}'s shelf cover has {stray} pixels that are not the painting — the shelf "
                + "draws its own rule, band and brackets, so anything baked in doubles them.");
        }
    }

    private static string RepoRootPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tools", "IconStudio")))
        {
            dir = dir.Parent;
        }
        return dir!.FullName;
    }

    // A 40 x 20 painting whose left half is red and right half blue, so which source rectangle
    // was drawn can be read off the written cover.
    private void SeedHalvesPainting(string relative, int width = 40, int height = 20)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var bitmap = new SKBitmap(width, height);
        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                bitmap.SetPixel(x, y, x < width / 2 ? SKColors.Red : SKColors.Blue);
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static IconSpec CropProbe(SourceCrop? crop) => new(
        "CropProbe", "CropProbe/Resources", "appicon", false, (_, _) => { },
        PlateImage: "Branding/crop-probe.png", CoverImage: "CropProbe/cover.png", Crop: crop);

    [Fact]
    public void CropReplacesTheCentreSquare()
    {
        SeedHalvesPainting("Branding/crop-probe.png");

        // Centre square of 40 x 20 is x 10..30 (half red, half blue). The crop is the right half.
        AdaptiveIconRenderer.Write(root, CropProbe(new SourceCrop(20, 0, 20, 20)));
        using var cropped = SKBitmap.Decode(Path.Combine(root, "CropProbe", "cover.png"));
        Assert.Equal(SKColors.Blue, cropped.GetPixel(2, cropped.Height / 2));
        Assert.Equal(SKColors.Blue, cropped.GetPixel(cropped.Width - 3, cropped.Height / 2));

        AdaptiveIconRenderer.Write(root, CropProbe(null));
        using var centred = SKBitmap.Decode(Path.Combine(root, "CropProbe", "cover.png"));
        Assert.Equal(SKColors.Red, centred.GetPixel(2, centred.Height / 2));
    }

    [Theory]
    [InlineData(0, 0, 20, 10)]    // not square
    [InlineData(30, 0, 20, 20)]   // runs past the right edge
    [InlineData(0, 5, 20, 20)]    // runs past the bottom edge
    [InlineData(-1, 0, 20, 20)]   // starts left of the image
    [InlineData(0, 0, 0, 0)]      // empty
    public void CropMustBeSquareAndInsideTheImage(int x, int y, int width, int height)
    {
        SeedHalvesPainting("Branding/crop-probe.png");

        var error = Assert.Throws<InvalidOperationException>(
            () => AdaptiveIconRenderer.Write(root, CropProbe(new SourceCrop(x, y, width, height))));

        Assert.Contains("CropProbe", error.Message);
        Assert.Contains($"{x},{y} {width}x{height}", error.Message);
    }

    // Regenerating every existing spec (none sets a Crop) must reproduce what is committed, byte
    // for byte, from the committed Branding art. Compared file by file against the repository.
    [Fact]
    public void SpecsWithoutACropAreUnchanged()
    {
        var repo = RepoRootPath();
        Assert.All(IconSpecs.All, spec => Assert.Null(spec.Crop));

        CopyDirectory(Path.Combine(repo, "Branding"), Path.Combine(root, "Branding"));
        CopyDirectory(Path.Combine(repo, "Fonts"), Path.Combine(root, "Fonts"));

        var compared = 0;
        foreach (var spec in IconSpecs.All)
        {
            foreach (var written in AdaptiveIconRenderer.Write(root, spec))
            {
                var committed = Path.Combine(repo, Path.GetRelativePath(root, written));
                Assert.True(File.Exists(committed), $"{spec.App}: {committed} is not committed.");
                Assert.True(File.ReadAllBytes(committed).AsSpan().SequenceEqual(File.ReadAllBytes(written)),
                    $"{spec.App}: regenerating changed {Path.GetRelativePath(root, written)}.");
                compared++;
            }
        }
        Assert.True(compared > 100, $"only {compared} files compared; the guard is not looking at the resources.");
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
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
