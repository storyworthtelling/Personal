using Constitution.Domain.Documents;
using Constitution.Infrastructure.Documents;
using SkiaSharp;
using Storyworthtelling.Paper;

namespace Constitution.Tests.Documents;

/// <summary>
/// C-2.1.3: nothing carries meaning by colour. A greyscale rM2 loses only the one coloured line, and
/// everything that is a rating, a state or a change is also written in words. The colour check walks
/// the content stream's colour operators rather than trusting the palette calls that were meant to
/// produce them.
/// </summary>
public class DocumentGreyscaleTests
{
    private static readonly string[] NeutralHexes =
    [
        PaperColorTokens.Paper, PaperColorTokens.PaperBright, PaperColorTokens.PaperDeep, PaperColorTokens.PaperShade,
        PaperColorTokens.Hairline, PaperColorTokens.Ink, PaperColorTokens.InkSoft, PaperColorTokens.InkMuted,
    ];

    private static (double R, double G, double B) HeaderRule => PdfReadBack.Rgb(AppIdentityTokens.ConstitutionDocument);

    private static bool IsNeutral((double R, double G, double B) colour) =>
        NeutralHexes.Any(hex => PdfReadBack.SameColour(colour, PdfReadBack.Rgb(hex)))
        || (colour.R > 0.99 && colour.G > 0.99 && colour.B > 0.99);

    private static byte[] Render() => ConstitutionDocumentRenderer.Render(DocumentSamples.Pages());

    [Fact]
    public void TargetsPrintInMutedInk()
    {
        var layouts = DocumentSamples.Pages();
        using var document = PdfReadBack.Open(Render());
        var muted = PdfReadBack.Rgb(PaperColorTokens.InkMuted);
        var checkedTargets = 0;

        for (var i = 0; i < layouts.Count; i++)
        {
            var paths = PdfReadBack.Paths(document.GetPage(i + 1));
            foreach (var target in layouts[i].Map.Targets.Where(t => t.Kind is InkTargetKind.LargeTick or InkTargetKind.StandardTick or InkTargetKind.NumberBox or InkTargetKind.NoBecause))
            {
                var outline = paths.Where(p => p.Stroked && PdfReadBack.Same(p.Bounds, target.Bounds)).ToList();

                Assert.NotEmpty(outline);
                Assert.All(outline, p => Assert.True(PdfReadBack.SameColour(p.Stroke, muted), $"{target.Id} is not drawn in the muted ink."));
                checkedTargets++;
            }

            foreach (var rule in layouts[i].Printed.Where(item => item.Role == PrintedRole.Rule))
            {
                var line = paths.Where(p => p.Stroked && PdfReadBack.Same(p.Bounds, rule.Bounds)).ToList();

                Assert.NotEmpty(line);
                Assert.All(line, p => Assert.True(PdfReadBack.SameColour(p.Stroke, muted), $"{rule.Name} is not drawn in the muted ink."));
            }
        }

        Assert.True(checkedTargets >= 12, $"Only {checkedTargets} targets were checked; the walk is not reaching the page.");
    }

    [Fact]
    public void TheReasonFieldIsShadedAndStillMuted()
    {
        var layout = DocumentSamples.MealPage();
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([layout]));
        var paths = PdfReadBack.Paths(document.GetPage(1));

        foreach (var field in layout.Map.Targets.Where(t => t.Kind == InkTargetKind.NoBecause))
        {
            Assert.Contains(paths, p => p.Filled && PdfReadBack.Same(p.Bounds, field.Bounds));
        }
    }

    [Fact]
    public void OnlyTheHeaderRuleIsColoured()
    {
        using var document = PdfReadBack.Open(Render());
        var headerStrokes = 0;

        foreach (var page in document.GetPages())
        {
            var painted = PdfReadBack.PaintedColours(page);
            Assert.NotEmpty(painted);

            var coloured = painted.Where(c => !IsNeutral((c.R, c.G, c.B))).ToList();
            Assert.All(coloured, c =>
            {
                Assert.Equal("stroke", c.Use);
                Assert.True(PdfReadBack.SameColour((c.R, c.G, c.B), HeaderRule), $"An unexpected colour was painted as a {c.Use}.");
            });

            var rules = PdfReadBack.Paths(page).Where(p => p.Stroked && PdfReadBack.SameColour(p.Stroke, HeaderRule)).ToList();
            var rule = Assert.Single(rules);
            Assert.Equal(ConstitutionPage.HeaderRuleTop, rule.Bounds.Top, 0.05f);
            Assert.Equal(0f, rule.Bounds.Height, 0.05f);
            headerStrokes += coloured.Count;
        }

        Assert.Equal(document.NumberOfPages, headerStrokes);
    }

    [Fact]
    public void TheColourWalkSeesAPlantedColour()
    {
        using var stream = new MemoryStream();
        using (var skia = SKDocument.CreatePdf(stream))
        {
            var canvas = skia.BeginPage(ConstitutionPage.Width, ConstitutionPage.Height);
            using var rule = new SKPaint { Color = AppIdentityTokens.ConstitutionDocument, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
            using var plum = new SKPaint { Color = SKColor.Parse(PaperColorTokens.Plum), Style = SKPaintStyle.Stroke, StrokeWidth = 1f };
            canvas.DrawLine(36, 64, 400, 64, rule);
            canvas.DrawRect(40, 100, 18, 18, plum);
            skia.EndPage();
            skia.Close();
        }

        using var document = PdfReadBack.Open(stream.ToArray());
        var painted = PdfReadBack.PaintedColours(document.GetPage(1));
        var coloured = painted.Where(c => !IsNeutral((c.R, c.G, c.B))).ToList();

        Assert.Equal(2, coloured.Count);
        Assert.Contains(coloured, c => !PdfReadBack.SameColour((c.R, c.G, c.B), HeaderRule));
    }

    [Fact]
    public void TextIsSetInInkNeverInAHue()
    {
        using var document = PdfReadBack.Open(Render());

        foreach (var page in document.GetPages())
        {
            var text = PdfReadBack.PaintedColours(page).Where(c => c.Use == "text").ToList();

            Assert.NotEmpty(text);
            Assert.All(text, c => Assert.True(IsNeutral((c.R, c.G, c.B))));
        }
    }

    [Fact]
    public void MutedInkStandsOffFromThePen()
    {
        // The pen draws in black. On greyscale the print has to be clearly lighter than that and
        // clearly darker than the ground, so the pen's mark always stands out from the print.
        var muted = SKColor.Parse(PaperColorTokens.InkMuted);
        var ground = SKColor.Parse(PaperColorTokens.Rm2Ground);

        Assert.True(Luma(muted) > 0.35, "Muted ink is too close to the pen's black.");
        Assert.True(Luma(ground) - Luma(muted) > 0.25, "Muted ink is too close to the ground.");
    }

    [Fact]
    public void OneRenderServesBothTablets()
    {
        var first = Render();
        var second = Render();

        Assert.Equal(first, second);

        // No door through which a device could change the bytes: nothing public takes a colour profile.
        var parameters = typeof(ConstitutionDocumentRenderer)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .ToList();

        Assert.NotEmpty(parameters);
        Assert.DoesNotContain(typeof(DeviceColorProfile), parameters);
        Assert.DoesNotContain(typeof(UiColorProfile), parameters);
        Assert.DoesNotContain(typeof(bool), parameters);
    }

    [Fact]
    public void ThePagesPaintNoGroundOfTheirOwn()
    {
        // The ground is the one exempted colour (sheets stay white), and not painting one is what lets
        // a single file serve a warm colour panel and a grey one.
        using var document = PdfReadBack.Open(Render());

        foreach (var page in document.GetPages())
        {
            var fills = PdfReadBack.Paths(page).Where(p => p.Filled && !p.Stroked).ToList();
            Assert.DoesNotContain(fills, p => p.Bounds.Width >= ConstitutionPage.Width - 1f && p.Bounds.Height >= ConstitutionPage.Height - 1f);
        }
    }

    [Fact]
    public void EveryTargetIsAlsoWrittenInWords()
    {
        var layouts = DocumentSamples.Pages();
        using var document = PdfReadBack.Open(Render());

        for (var i = 0; i < layouts.Count; i++)
        {
            var text = document.GetPage(i + 1).Text;
            Assert.Empty(layouts[i].Violations().Where(v => v.Kind == InkViolationKind.Unnamed));

            foreach (var item in layouts[i].Printed.Where(p => p.Text is not null && p.OwnerId is not null))
            {
                Assert.Contains(item.Text!, text, StringComparison.Ordinal);
            }
        }

        var exercise = document.GetPage(1).Text;
        foreach (var word in new[] { "easy", "right", "hard", "Done", "Notes", "Up 2 reps on last week" })
        {
            Assert.Contains(word, exercise, StringComparison.Ordinal);
        }
    }

    private static double Luma(SKColor c) => (0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue) / 255.0;
}
