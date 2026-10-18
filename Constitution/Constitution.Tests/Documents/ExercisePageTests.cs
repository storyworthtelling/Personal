using Codex.Domain.Music;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Infrastructure.Documents;
using Storyworthtelling.Paper;

namespace Constitution.Tests.Documents;

/// <summary>C-2.3.2: one exercise per page, with the rating and the tick in the same place every time.</summary>
public class ExercisePageTests
{
    private static InkPageLayout Squat(bool figure = true) =>
        WorkoutDocument.Build(WorkoutSamples.Workout(exercises: [WorkoutSamples.Squat(figure)]))[1];

    private static string PageText(InkPageLayout page)
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        return WorkoutCoverTests.TextOf(document.GetPage(1));
    }

    private static string ScoreLine(InkPageLayout page) =>
        page.Printed.Single(item => item.Text?.StartsWith("Score · ", StringComparison.Ordinal) == true).Text!;

    [Fact]
    public void ScoreLineNamesTheTempoBandsBand()
    {
        for (var bpm = TempoBands.Slowest; bpm <= TempoBands.Fastest; bpm++)
        {
            var model = WorkoutSamples.Squat() with { ScoreBpm = bpm };
            var page = WorkoutDocument.Build(WorkoutSamples.Workout(exercises: [model]))[1];
            var band = TempoBands.Names[(int)TempoBands.Of(bpm)];

            Assert.Equal($"Score · {bpm} bpm · {band}", ScoreLine(page));
        }
    }

    [Fact]
    public void ThePrintedScoreLineIsThePageText()
    {
        Assert.Contains("Score · 132 bpm · Steady", PageText(Squat()));
        Assert.Contains("SET SCORE TO", PageText(Squat()));
    }

    [Fact]
    public void ThereIsExactlyOneScoreSetting()
    {
        Assert.Single(Squat().Printed, item => item.Text?.Contains("bpm", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void RatingAndTickSitAtTheFixedFoot()
    {
        var pages = WorkoutSamples.AllPages().Where(p => p.Map.TryGet(WorkoutIds.Rating + "-1") is not null).ToList();
        var withAndWithoutFigure = new[] { Squat(true), Squat(false) };

        Assert.True(pages.Count >= 10);
        foreach (var page in pages.Concat(withAndWithoutFigure))
        {
            var done = page.Map.ById(WorkoutIds.Done);
            Assert.Equal(InkTargetKind.LargeTick, done.Kind);
            Assert.Equal(new InkRect(36f, 526f, 28f, 28f), done.Bounds);

            var easy = page.Map.ById("feel-1");
            var hard = page.Map.ById("feel-3");
            Assert.Equal(474f, easy.Bounds.Top);
            Assert.Equal(84f, easy.Bounds.Width);
            Assert.Equal(36f, easy.Bounds.Left);
            Assert.Equal(204f, hard.Bounds.Left);
            Assert.True(easy.Bounds.Bottom < done.Bounds.Top - InkTargets.MinimumClearance);
        }
    }

    [Fact]
    public void TheRatingIsJustAboveTheTickAndBelowTheEncouragement()
    {
        var page = Squat();
        var voice = page.Printed.Where(i => i.Role == PrintedRole.Voice).Max(i => i.Bounds.Bottom);

        Assert.True(voice < page.Map.ById("feel-1").Bounds.Top - InkTargets.MinimumClearance);
        Assert.True(page.Map.ById("feel-3").Bounds.Bottom < page.Map.ById(WorkoutIds.Done).Bounds.Top);
    }

    [Fact]
    public void WeightsArePounds()
    {
        var text = PageText(Squat());

        Assert.Contains("25 lb kettlebell", text);
        Assert.DoesNotContain("kg", text);
        Assert.DoesNotContain("kilo", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Five more pounds today.", text);
    }

    [Fact]
    public void AWeightMustBeAPositiveNumberOfPounds()
    {
        var model = WorkoutSamples.Squat() with { Load = new ExerciseLoad(0, "kettlebell") };

        Assert.Throws<ArgumentException>(() => WorkoutDocument.Build(WorkoutSamples.Workout(exercises: [model])));
    }

    [Fact]
    public void AnExerciseWithNoWeightPrintsItsWordsOnly()
    {
        var page = WorkoutDocument.Build(WorkoutSamples.Workout(exercises: [WorkoutSamples.Row()]))[1];

        Assert.Contains("medium band", PageText(page));
        Assert.DoesNotContain(" lb ", PageText(page));
    }

    [Fact]
    public void CarriesOneEncouragementLine()
    {
        var page = Squat();
        var voiced = page.Printed.Where(i => i.Role == PrintedRole.Voice).ToList();
        var text = PageText(page);

        Assert.NotEmpty(voiced);
        Assert.InRange(voiced.Count, 1, 3);
        Assert.Contains("Five more pounds today.", text);
        Assert.Contains("You were ready for it last week.", text);
    }

    [Fact]
    public void ASilentSeatPrintsNoVoice()
    {
        var page = WorkoutDocument.Build(WorkoutSamples.Workout(exercises: [WorkoutSamples.Walk()]))[1];

        Assert.DoesNotContain(page.Printed, i => i.Role == PrintedRole.Voice);
    }

    [Fact]
    public void ALongEncouragementIsHiddenNotBroken()
    {
        var rambling = string.Join(' ', Enumerable.Repeat("You are doing more than you think you are today.", 4));
        var page = WorkoutDocument.Build(WorkoutSamples.Workout(exercises: [WorkoutSamples.Squat(false, rambling)]))[1];

        Assert.DoesNotContain(page.Printed, i => i.Role == PrintedRole.Voice);
    }

    [Fact]
    public void TheDoseIsOnTheDoseRung()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Squat()]));
        var page = document.GetPage(1);
        var dose = page.Letters.Where(l => l.PointSize > 40).ToList();

        Assert.Equal(["3", "×", "1", "0"], dose.Where(l => l.Value != " ").Select(l => l.Value));
        Assert.All(dose, l => Assert.Equal(PaperTypeScale.Dose, (float)l.PointSize, 0.1f));
    }

    [Fact]
    public void TopToBottomIsDoseLoadRestScoreVoiceRatingTick()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Squat(false)]));
        var page = document.GetPage(1);
        double Y(string word) => page.GetWords().First(w => w.Text == word).BoundingBox.Bottom;

        var order = new[] { Y("3"), Y("kettlebell"), Y("Rest"), Y("SET"), Y("Five"), Y("HOW"), Y("tick") };
        Assert.Equal(order.OrderByDescending(y => y), order);
    }

    [Fact]
    public void TheDoseBaselineIsWhereTheBookDrewIt()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([Squat()]));
        var page = document.GetPage(1);
        var dose = page.Letters.First(l => l.PointSize > 40);

        Assert.Equal(148.0, page.Height - dose.StartBaseLine.Y, 1.5);
    }

    [Fact]
    public void RestIsInSecondsAndOptional()
    {
        Assert.Contains("Rest 60 seconds between sets", PageText(Squat()));
        var walk = WorkoutDocument.Build(WorkoutSamples.Workout(exercises: [WorkoutSamples.Walk()]))[1];
        Assert.DoesNotContain("Rest", PageText(walk));
    }

    [Fact]
    public void TheFigureSlotIsKeptOnlyWhileTheExerciseIsNew()
    {
        var shown = Squat(true);
        var hidden = Squat(false);
        var slot = shown.Printed.Single(i => i.Name == WorkoutIds.FigureSlot);

        Assert.Equal(PrintedRole.Slot, slot.Role);
        Assert.Equal(new InkRect(36f, slot.Bounds.Top, 270f, 150f), slot.Bounds);
        Assert.DoesNotContain(hidden.Printed, i => i.Name == WorkoutIds.FigureSlot);

        var scoreShown = shown.Printed.Single(i => i.Text == "SET SCORE TO").Bounds.Top;
        var scoreHidden = hidden.Printed.Single(i => i.Text == "SET SCORE TO").Bounds.Top;
        Assert.True(scoreShown > slot.Bounds.Bottom);
        Assert.True(scoreHidden < scoreShown - 100f);
    }

    [Fact]
    public void NothingIsPrintedInTheFigureSlot()
    {
        var page = Squat(true);
        var slot = page.Printed.Single(i => i.Name == WorkoutIds.FigureSlot).Bounds;

        Assert.DoesNotContain(page.Printed, i => i.Role != PrintedRole.Slot && i.Role != PrintedRole.Rule && i.Role != PrintedRole.Dotted && i.Bounds.Overlaps(slot));
    }

    [Fact]
    public void TargetsHaveStableIdsTheResolverCanName()
    {
        var page = Squat();
        var rule = new ExercisePageRule("goblet squat", WorkoutIds.Done, WorkoutIds.Rating);

        Assert.NotNull(page.Map.TryGet(rule.DoneBoxId));
        Assert.Equal(["easy", "right", "hard"], Enumerable.Range(1, 3).Select(i => page.Map.ById($"{rule.RatingGroup}-{i}").Meaning));
        Assert.All(Enumerable.Range(1, 3), i => Assert.Equal(rule.RatingGroup, page.Map.ById($"{rule.RatingGroup}-{i}").Group));
        Assert.Equal(InkTargetKind.NotesMargin, page.Map.ById(WorkoutIds.Notes).Kind);
    }

    [Fact]
    public void TheNotesMarginIsNinetyOnePointsOfRuledColumn()
    {
        var page = Squat();
        var notes = page.Map.ById(WorkoutIds.Notes);

        Assert.Equal(91f, notes.Bounds.Width);
        Assert.Equal(318f, notes.Bounds.Left);
        Assert.Contains(page.Printed, i => i.Text == "NOTES");
        Assert.True(page.Printed.Count(i => i.Role == PrintedRole.Dotted) >= 15);
    }

    [Fact]
    public void EveryPageIsFitToPrint()
    {
        foreach (var page in WorkoutSamples.AllPages())
        {
            Assert.True(page.Violations().Count == 0, $"{page.Title}: " + string.Join("; ", page.Violations().Select(v => $"{v.Kind} {v.TargetId} {v.Other}")));
        }
    }

    [Fact]
    public void PagesAreNumberedOfTheWholeDocument()
    {
        var pages = WorkoutDocument.Build(WorkoutSamples.Workout());

        Assert.Equal(8, pages.Count);
        Assert.Equal(["Goblet squat"], pages.Where(p => p.Title == "Goblet squat").Select(p => p.Title));
        Assert.Contains("3 of 8", PageText(pages[2]));
        Assert.Contains("EXERCISE 2 OF 7 · STRENGTH", PageText(pages[2]));
        Assert.Contains("Constitution · Strength · Tue Oct 20", PageText(pages[2]));
    }

    [Fact]
    public void WordsKeepInsideTheLeftColumn()
    {
        foreach (var page in WorkoutDocument.Build(WorkoutSamples.Workout()).Skip(1))
        {
            using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
            var letters = document.GetPage(1).Letters.Where(l => l.GlyphRectangle.Top < 560).ToList();

            Assert.DoesNotContain(letters, l => l.GlyphRectangle.Left < 314 && l.GlyphRectangle.Right > 314);
        }
    }

    [Fact]
    public void ARenderedExerciseDocumentReadsBackAtThePageSize()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(WorkoutDocument.Build(WorkoutSamples.Workout())));

        Assert.Equal(8, document.NumberOfPages);
        Assert.All(document.GetPages(), p => Assert.Equal((445d, 594d), (Math.Round(p.Width), Math.Round(p.Height))));
    }

    [Fact]
    public void PaintedTargetsLandWhereTheMapSaysAndInMutedInk()
    {
        var pages = WorkoutDocument.Build(WorkoutSamples.Workout());
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(pages));
        var muted = PdfReadBack.Rgb(PaperColorTokens.InkMuted);
        var seen = 0;

        for (var i = 0; i < pages.Count; i++)
        {
            var paths = PdfReadBack.Paths(document.GetPage(i + 1));
            foreach (var tick in pages[i].Map.Targets.Where(t => t.Kind == InkTargetKind.LargeTick))
            {
                var outline = paths.Where(p => p.Stroked && PdfReadBack.Same(p.Bounds, tick.Bounds)).ToList();
                Assert.NotEmpty(outline);
                Assert.All(outline, p => Assert.True(PdfReadBack.SameColour(p.Stroke, muted)));
                seen++;
            }
        }

        Assert.Equal(7, seen);
    }

    [Fact]
    public void OnlyTheHeaderRuleIsColouredAndTextIsInk()
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(WorkoutSamples.AllPages()));
        var header = PdfReadBack.Rgb(AppIdentityTokens.ConstitutionDocument);
        string[] neutral = [PaperColorTokens.Paper, PaperColorTokens.PaperBright, PaperColorTokens.PaperDeep, PaperColorTokens.PaperShade, PaperColorTokens.Hairline, PaperColorTokens.Ink, PaperColorTokens.InkSoft, PaperColorTokens.InkMuted];

        foreach (var page in document.GetPages())
        {
            foreach (var c in PdfReadBack.PaintedColours(page))
            {
                var grey = neutral.Any(h => PdfReadBack.SameColour((c.R, c.G, c.B), PdfReadBack.Rgb(h))) || c.R > 0.99 && c.G > 0.99 && c.B > 0.99;
                if (!grey)
                {
                    Assert.Equal("stroke", c.Use);
                    Assert.True(PdfReadBack.SameColour((c.R, c.G, c.B), header));
                }
            }
        }
    }
}
