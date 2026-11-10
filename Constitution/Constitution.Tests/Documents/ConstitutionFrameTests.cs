using Constitution.Domain.Documents;
using Constitution.Infrastructure.Documents;
using Constitution.Tests.DigestPages;
using Storyworthtelling.Paper;
using Storyworthtelling.Paper.Binder;
using UglyToad.PdfPig.Annotations;

namespace Constitution.Tests.Documents;

/// <summary>
/// Constitution's pages wear the frame every tablet document shares (owner's decision): the title signed
/// "Constitution" in the owner's hand over the identity rule, a binder tab per section as a real link, and the folio
/// led by Folio's maker's mark — drawn at the size Foreword's sheets draw it, on a page whose pen targets have not
/// moved a point.
/// </summary>
public class ConstitutionFrameTests
{
    private static bool Upright(UglyToad.PdfPig.Content.Letter letter) => Math.Abs(letter.EndBaseLine.Y - letter.StartBaseLine.Y) < 0.01;

    private static string Vertical(UglyToad.PdfPig.Content.Page page) =>
        string.Concat(page.Letters.Where(l => !Upright(l)).Select(l => l.Value));

    [Fact]
    public void AWorkoutHasATabForItsPlanAndOneForItsExercises()
    {
        var pages = WorkoutDocument.Build(WorkoutSamples.Workout());

        Assert.Equal("Workout", pages[0].Section);
        Assert.All(pages.Skip(1), page => Assert.Equal("Exercises", page.Section));

        using var pdf = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(pages));
        foreach (var page in pdf.GetPages())
        {
            Assert.Equal(2, page.GetAnnotations().Count(a => a.Type == AnnotationType.Link));
            Assert.Contains("Workout", Vertical(page));
            Assert.Contains("Exercises", Vertical(page));
        }
    }

    [Fact]
    public void TheDigestsTabsAreItsSections()
    {
        var sections = SaturdayDigest.Build(DigestFixture.Model()).Select(page => page.Section).Distinct().ToList();

        Assert.Equal(["Workouts", "Body", "Meals", "Next week"], sections);
    }

    [Fact]
    public void EachTabLandsOnItsSectionsFirstPage()
    {
        var pages = WorkoutDocument.Build(WorkoutSamples.Workout());
        using var pdf = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(pages));

        var targets = pdf.GetPage(3).GetAnnotations()
            .Where(a => a.Type == AnnotationType.Link)
            .OrderByDescending(a => a.Rectangle.Top)
            .Select(a => Assert.IsAssignableFrom<UglyToad.PdfPig.Actions.AbstractGoToAction>(a.Action).Destination.PageNumber)
            .ToList();
        Assert.Equal([1, 2], targets);
    }

    [Fact]
    public void ThePageIsSignedAndItsFolioCarriesTheMark()
    {
        var pages = WorkoutDocument.Build(WorkoutSamples.Workout());
        using var pdf = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(pages));
        var page = pdf.GetPage(2);
        var scale = ConstitutionSheet.Frame.Scale;

        Assert.Contains("Constitution", string.Concat(page.Letters.Where(l => (l.FontName ?? "").Contains("Pinyon", StringComparison.Ordinal)).Select(l => l.Value)));
        Assert.Contains($"2 of {pages.Count}", string.Join(" ", page.GetWords().Select(w => w.Text)));
        Assert.Empty(page.GetImages());
        Assert.Contains(page.Paths, path => path.GetBoundingRectangle() is { } r
            && Math.Abs(r.Left - SheetFrame.Margin * scale) < 3 && r.Top < (ConstitutionPage.Height - SheetFrame.ContentBottom(ConstitutionSheet.Frame) * scale)
            && r.Width is > 4 and < 12);
    }

    [Fact]
    public void TheFrameIsDrawnAtForewordsSizeAndClearsThePenTargets()
    {
        // The frame's board is Letter on the glass, as Foreword's is, so the header, tabs and folio are one size on every
        // tablet document. Constitution's own body sits under its rule, left of its tabs and above its folio.
        Assert.Equal(TabletSheet.Fill(612f, 792f), ConstitutionSheet.Frame);
        var scale = ConstitutionSheet.Frame.Scale;
        var body = ConstitutionPage.Body;

        Assert.True((SheetFrame.Margin + SheetFrame.HeaderHeight + SheetFrame.RuleGap + SheetFrame.RuleHeight) * scale < body.Top);
        Assert.True(body.Left + body.Width < (ConstitutionSheet.Frame.DesignWidth - BinderStrip.TabWidth - BinderStrip.Hairline) * scale);
        Assert.True(body.Top + body.Height < SheetFrame.FolioTop(ConstitutionSheet.Frame) * scale);
    }

    [Fact]
    public void TheFrameAddsOnlyConstitutionsOwnColoursToAnInkPage()
    {
        // Ink and paper as before, the device's warm ground, and Constitution's cerulean family: the signature and rule
        // in the documents' muted cerulean, an inactive tab's cloth, an open tab's deep ink. Nothing else is in colour.
        string[] allowed =
        [
            PaperColorTokens.Paper, PaperColorTokens.PaperBright, PaperColorTokens.PaperDeep, PaperColorTokens.PaperShade,
            PaperColorTokens.Hairline, PaperColorTokens.Ink, PaperColorTokens.InkSoft, PaperColorTokens.InkMuted,
            AppIdentityTokens.ConstitutionDocumentHex, AppIdentityTokens.ConstitutionGroundHex, AppIdentityTokens.ConstitutionDeepHex,
        ];
        var frame = new[] { SheetFrame.Ground, SheetFrame.Ink, SheetFrame.InkMuted, SheetFrame.InkSoft, SheetFrame.Hairline }.Select(PdfReadBack.Rgb);
        using var pdf = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(WorkoutDocument.Build(WorkoutSamples.Workout())));

        foreach (var page in pdf.GetPages())
        {
            var stray = PdfReadBack.PaintedColours(page)
                .Where(c => !allowed.Any(hex => PdfReadBack.SameColour((c.R, c.G, c.B), PdfReadBack.Rgb(hex)))
                    && !frame.Any(rgb => PdfReadBack.SameColour((c.R, c.G, c.B), rgb))
                    && !(c.R > 0.99 && c.G > 0.99 && c.B > 0.99))
                .ToList();
            Assert.Empty(stray);
        }
    }

    [Fact]
    public void ADocumentWithMoreSectionsThanTheStripHoldsIsRefused()
    {
        var pages = Enumerable.Range(1, 12).Select(i => InkPageLayout.Create($"Page {i}", [], [], section: $"S{i}")).ToList();

        var refused = Assert.Throws<InvalidOperationException>(() => ConstitutionDocumentRenderer.Render(pages));
        Assert.Contains("tabs", refused.Message, StringComparison.Ordinal);
    }
}
