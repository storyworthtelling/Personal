using Codex.Domain.Music;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Infrastructure.Documents;

namespace Constitution.Tests.Documents;

/// <summary>C-2.3.5: "Still sore?" at the top of every page until it is left blank, and a calm doctor note, once.</summary>
public class InjuryPageTests
{
    private static string PageText(InkPageLayout page)
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        return WorkoutCoverTests.TextOf(document.GetPage(1));
    }

    [Fact]
    public void StillSoreOnEveryPage()
    {
        var pages = WorkoutDocument.Build(WorkoutSamples.InjuryDay());

        Assert.Equal(4, pages.Count);
        foreach (var page in pages)
        {
            var tick = page.Map.ById(WorkoutIds.StillSore);
            var band = Assert.Single(page.Printed, i => i.Role == PrintedRole.Band);

            Assert.Equal(InkTargetKind.LargeTick, tick.Kind);
            Assert.True(band.Bounds.Encloses(tick.Bounds));
            Assert.Equal(100f, band.Bounds.Top);
            Assert.Equal(36f, band.Bounds.Left);
            Assert.Contains("Still sore?", PageText(page));
            Assert.Contains("Left knee, since Monday. Leave blank if it’s better.", PageText(page));
            Assert.Equal(tick.Bounds.Top, pages[0].Map.ById(WorkoutIds.StillSore).Bounds.Top);
        }
    }

    [Fact]
    public void TheBandIsAboveEverythingButTheHeader()
    {
        foreach (var page in WorkoutDocument.Build(WorkoutSamples.InjuryDay()).Skip(1))
        {
            var band = page.Printed.Single(i => i.Role == PrintedRole.Band).Bounds;
            var below = page.Printed.Where(i => i.Role is PrintedRole.Dose or PrintedRole.Heading or PrintedRole.Voice or PrintedRole.Choice);

            Assert.All(below, i => Assert.True(i.Bounds.Top >= band.Bottom - 0.01f, $"{i.Name} sits inside the band"));
        }
    }

    [Fact]
    public void NothingSoreMeansNoBandAndNoQuestion()
    {
        foreach (var page in WorkoutDocument.Build(WorkoutSamples.Workout()))
        {
            Assert.Null(page.Map.TryGet(WorkoutIds.StillSore));
            Assert.DoesNotContain(page.Printed, i => i.Role == PrintedRole.Band);
            Assert.DoesNotContain("Still sore?", PageText(page));
        }
    }

    [Fact]
    public void TheStillSoreTickIsPaintedWhiteOnTheBand()
    {
        var page = WorkoutDocument.Build(WorkoutSamples.InjuryDay())[1];
        var tick = page.Map.ById(WorkoutIds.StillSore).Bounds;
        var band = page.Printed.Single(i => i.Role == PrintedRole.Band).Bounds;
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        var paths = PdfReadBack.Paths(document.GetPage(1));

        Assert.Contains(paths, p => p.Filled && PdfReadBack.Same(p.Bounds, band));
        Assert.Contains(paths, p => p.Filled && !p.Stroked && PdfReadBack.Same(p.Bounds, tick));
        Assert.Contains(paths, p => p.Stroked && PdfReadBack.Same(p.Bounds, tick) && PdfReadBack.SameColour(p.Stroke, PdfReadBack.Rgb(Storyworthtelling.Paper.PaperColorTokens.InkMuted)));
    }

    [Fact]
    public void DoctorNotePrintsOnceInNeutralType()
    {
        var pages = WorkoutDocument.Build(WorkoutSamples.InjuryDay(doctor: true));
        var carrying = pages.Where(p => PageText(p).Contains("doctor", StringComparison.Ordinal)).ToList();

        var page = Assert.Single(carrying);
        Assert.Contains("Sore for ten days now.", PageText(page));
        Assert.Contains("If it isn’t easing, it may be worth having a doctor look at it. This note shows once.", PageText(page));

        var note = page.Printed.Where(i => i.Text is not null && (i.Text.Contains("Sore for", StringComparison.Ordinal) || i.Text.Contains("doctor", StringComparison.Ordinal))).ToList();
        Assert.NotEmpty(note);
        Assert.All(note, i => Assert.True(i.Role is PrintedRole.Strong or PrintedRole.Small or PrintedRole.Body, $"{i.Name} is set in {i.Role}"));
        Assert.Contains(page.Printed, i => i.Role == PrintedRole.Rule && i.Bounds.Height == 0f && i.Bounds.Width == 270f);
    }

    [Fact]
    public void NoDoctorNoteUntilItIsDue()
    {
        var pages = WorkoutDocument.Build(WorkoutSamples.InjuryDay(doctor: false));

        Assert.All(pages, p => Assert.DoesNotContain("doctor", PageText(p)));
    }

    [Fact]
    public void TheDoctorNoteSitsAfterTheVoiceAndAboveTheRating()
    {
        var page = WorkoutDocument.Build(WorkoutSamples.InjuryDay(doctor: true))[1];
        var voice = page.Printed.Where(i => i.Role == PrintedRole.Voice).Max(i => i.Bounds.Bottom);
        var note = page.Printed.Where(i => i.Text?.Contains("doctor", StringComparison.Ordinal) == true || i.Text?.StartsWith("Sore for", StringComparison.Ordinal) == true).ToList();

        Assert.All(note, i => Assert.True(i.Bounds.Top >= voice - 6f));
        Assert.True(note.Max(i => i.Bounds.Bottom) < page.Map.ById("feel-1").Bounds.Top - 8f);
        Assert.Empty(page.Violations());
    }

    [Fact]
    public void TheSubstitutionIsInPlainWords()
    {
        var page = WorkoutDocument.Build(WorkoutSamples.InjuryDay())[1];

        Assert.Contains("In place of reverse lunges: no load through the knee.", PageText(page));
        Assert.Contains("WORKING AROUND YOUR LEFT KNEE", PageText(page));
    }

    [Fact]
    public void TheTempoDropsToEasyOnAnInjuryDay()
    {
        var pages = WorkoutDocument.Build(WorkoutSamples.InjuryDay());

        foreach (var page in pages.Skip(1))
        {
            var score = page.Printed.Single(i => i.Text?.StartsWith("Score · ", StringComparison.Ordinal) == true).Text!;
            Assert.EndsWith("· Easy", score);
            var bpm = int.Parse(score.Split(' ')[2]);
            Assert.Equal(TempoBand.Easy, TempoBands.Of(bpm));
        }

        Assert.Contains("Score · 110 bpm · Easy", PageText(pages[1]));
    }

    [Fact]
    public void AnEasyTempoAlreadyGivenIsLeftAlone()
    {
        var pages = WorkoutDocument.Build(WorkoutSamples.InjuryDay());

        Assert.Contains(pages[3].Printed, i => i.Text == "Score · 118 bpm · Easy");
    }

    [Fact]
    public void TheStillSoreRuleCanNameTheTick()
    {
        var page = WorkoutDocument.Build(WorkoutSamples.InjuryDay())[1];
        var rule = new StillSoreRule("the sore left knee", WorkoutIds.StillSore);

        Assert.NotNull(page.Map.TryGet(rule.BoxId));
        Assert.NotNull(page.Map.TryGet(WorkoutIds.Done));
    }

    [Fact]
    public void EveryInjuryPageIsFitToPrintAndStaysInTheColumn()
    {
        var pages = WorkoutDocument.Build(WorkoutSamples.InjuryDay(doctor: true));
        Assert.All(pages, p => Assert.Empty(p.Violations()));

        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(pages));
        foreach (var page in document.GetPages().Skip(1))
        {
            Assert.DoesNotContain(page.Letters.Where(l => l.GlyphRectangle.Top < 560), l => l.GlyphRectangle.Left < 314 && l.GlyphRectangle.Right > 314);
        }
    }

    [Fact]
    public void ASorenessNeedsAPlaceAndADay()
    {
        var blank = WorkoutSamples.Sore() with { Place = " " };
        var none = WorkoutSamples.Sore(days: 0);

        Assert.Throws<ArgumentException>(() => WorkoutDocument.Build(WorkoutSamples.Workout(blank)));
        Assert.Throws<ArgumentException>(() => WorkoutDocument.Build(WorkoutSamples.Workout(none)));
    }
}
