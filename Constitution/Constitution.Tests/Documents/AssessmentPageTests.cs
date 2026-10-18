using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Infrastructure.Documents;

namespace Constitution.Tests.Documents;

/// <summary>C-2.3.4: assessment week asks only for a number or a circled word, and never for a rating.</summary>
public class AssessmentPageTests
{
    private static IReadOnlyList<InkPageLayout> Pages() => AssessmentDocument.Build(WorkoutSamples.Assessment());

    private static string PageText(InkPageLayout page)
    {
        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render([page]));
        return WorkoutCoverTests.TextOf(document.GetPage(1));
    }

    [Fact]
    public void TestPagesCarryNoRating()
    {
        foreach (var page in Pages().Skip(1))
        {
            var text = PageText(page);

            Assert.DoesNotContain("HOW WAS IT", text);
            Assert.DoesNotContain("easy", text);
            Assert.DoesNotContain("hard", text);
            Assert.DoesNotContain(page.Map.Targets, t => t.Group == WorkoutIds.Rating);
            Assert.NotNull(page.Map.TryGet(WorkoutIds.Done));
        }
    }

    [Fact]
    public void ToeReachUsesFourNarrowLanes()
    {
        var toe = Pages()[4];
        var lanes = toe.Map.Targets.Where(t => t.Kind == InkTargetKind.CircleLane).ToList();

        Assert.Equal(4, lanes.Count);
        Assert.All(lanes, l => Assert.Equal(68f, l.Bounds.Width));
        Assert.Equal(["knees", "shins", "ankles", "floor"], lanes.Select(l => l.Meaning));
        Assert.Equal(["choice-1", "choice-2", "choice-3", "choice-4"], lanes.Select(l => l.Id));
        Assert.Equal(3, toe.Printed.Count(i => i.Role == PrintedRole.Rule && i.Bounds.Width == 0f && i.Name.Contains("divider")));
        Assert.Equal(36f, lanes[0].Bounds.Left);
        Assert.True(lanes[3].Bounds.Right <= 318f - InkTargets.MinimumClearance);
        Assert.Contains("knees shins ankles floor", PageText(toe));
    }

    [Fact]
    public void ANumberTestHasOneBoxWithItsUnitOutside()
    {
        var page = Pages()[1];
        var box = Assert.Single(page.Map.Targets, t => t.Kind == InkTargetKind.NumberBox);

        Assert.Equal(WorkoutIds.Result, box.Id);
        Assert.Equal(new InkRect(36f, box.Bounds.Top, 96f, 40f), box.Bounds);
        Assert.Equal("reps", box.Unit);

        var unit = page.Printed.Single(i => i.Text == "reps");
        Assert.False(unit.Bounds.Overlaps(box.Bounds));
        Assert.True(unit.Bounds.Left >= box.Bounds.Right + InkTargets.MinimumClearance - 0.01f);
        Assert.Contains("WRITE THE NUMBER", PageText(page));
    }

    [Fact]
    public void TheNumberBoxHasAStableIdTheResolverCanName()
    {
        var page = Pages()[3];
        var rule = new NumberRule("plank hold", WorkoutIds.Result);

        Assert.Equal("seconds", page.Map.ById(rule.BoxId).Unit);
        Assert.Equal(InkTargetKind.NumberBox, page.Map.ById(rule.BoxId).Kind);
    }

    [Fact]
    public void TheCoverSaysWhatTheWeekIsForAndThatThereIsNoScore()
    {
        var cover = Pages()[0];
        var text = PageText(cover);

        Assert.Equal("Where you start", cover.Title);
        Assert.Contains("Three short sessions this week.", text);
        Assert.Contains("Each test asks for one number, written in its box.", text);
        Assert.Contains("That number sets where week two begins.", text);
        Assert.Contains("There is no score and nothing to beat.", text);
        Assert.Contains("ASSESSMENT WEEK · DAY 1 OF 3 · MONDAY 5:30 PM", text);
        Assert.Empty(cover.Map.Targets);
        Assert.DoesNotContain(cover.Printed, i => i.Role == PrintedRole.Voice);
    }

    [Fact]
    public void TheCoverListsTheFourTestsWithUnitsAndPages()
    {
        var text = PageText(Pages()[0]);

        Assert.Contains("Push-ups in a row reps p. 2", text);
        Assert.Contains("Bodyweight squats in a minute reps p. 3", text);
        Assert.Contains("Plank hold seconds p. 4", text);
        Assert.Contains("Toe reach circle one p. 5", text);
        Assert.Contains("Wednesday: a timed one-mile walk. Friday: the same four, again.", text);
        Assert.Contains("Constitution · Assessment · Mon Nov 2", text);
        Assert.Contains("1 of 5", text);
    }

    [Fact]
    public void TestPagesNameTheirPlaceAndKeepTheEncouragementSeat()
    {
        var pages = Pages();
        var first = PageText(pages[1]);

        Assert.Contains("ASSESSMENT · TEST 1 OF 4", first);
        Assert.Contains("As many as you can with good form, then stop.", first);
        Assert.Contains("Knees down is fine; say so in the notes.", first);
        Assert.Contains("Just a starting line.", first);
        Assert.Contains("2 of 5", first);
        Assert.Contains(pages[1].Printed, i => i.Role == PrintedRole.Voice);
        Assert.Contains("ASSESSMENT · TEST 4 OF 4", PageText(pages[4]));
    }

    [Fact]
    public void TheFigureRoomIsKeptOnlyWhereATestHasAFigure()
    {
        var pages = Pages();

        Assert.Contains(pages[1].Printed, i => i.Name == WorkoutIds.FigureSlot);
        Assert.DoesNotContain(pages[2].Printed, i => i.Name == WorkoutIds.FigureSlot);
    }

    [Fact]
    public void ATestNeedsOneAnswerNotNoneNotBoth()
    {
        var both = WorkoutSamples.Tests()[0] with { Words = ["a", "b"] };
        var neither = WorkoutSamples.Tests()[0] with { Unit = null };
        var one = WorkoutSamples.Tests()[3] with { Words = ["knees"] };

        foreach (var bad in new[] { both, neither, one })
        {
            var model = WorkoutSamples.Assessment() with { Tests = [bad] };
            Assert.Throws<ArgumentException>(() => AssessmentDocument.Build(model));
        }
    }

    [Fact]
    public void ThreeWordsGetTheWideLanes()
    {
        var three = WorkoutSamples.Tests()[3] with { Words = ["knees", "ankles", "floor"] };
        var page = AssessmentDocument.Build(WorkoutSamples.Assessment() with { Tests = [three] })[1];

        Assert.All(page.Map.Targets.Where(t => t.Kind == InkTargetKind.CircleLane), l => Assert.Equal(84f, l.Bounds.Width));
    }

    [Fact]
    public void EveryAssessmentPageIsFitToPrintAndRendersAtPageSize()
    {
        var pages = Pages();
        Assert.All(pages, p => Assert.Empty(p.Violations()));

        using var document = PdfReadBack.Open(ConstitutionDocumentRenderer.Render(pages));
        Assert.Equal(5, document.NumberOfPages);
        foreach (var page in document.GetPages())
        {
            Assert.Equal(445d, page.Width, 1);
            Assert.All(page.Letters, l => Assert.InRange(l.GlyphRectangle.Right, 35.0, 410.0));
        }
    }

    [Fact]
    public void ThereIsNoStillSoreOnAssessmentPages()
    {
        Assert.All(Pages(), p => Assert.Null(p.Map.TryGet(WorkoutIds.StillSore)));
    }
}
