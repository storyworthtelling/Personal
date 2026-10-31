using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.ReadBack;
using Constitution.Tests.RecipeDocuments;
using static Constitution.Tests.RecipeRating.RatingSamples;

namespace Constitution.Tests.RecipeRating;

/// <summary>
/// C-2.5.4: the rating page read back through the REAL ink resolver and <see cref="TasteInk"/>. The page, the expected
/// page and the rows the taste profile reads are all built from one model, so the ids cannot drift.
/// </summary>
public class RatingReadTests
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 18, 7, 0, 0, TimeSpan.FromHours(-5));
    private static readonly DateOnly Day = new(2026, 11, 17);

    private static InkMark Around(InkTarget target) =>
        new(new InkRect(target.Bounds.CentreX - 8f, target.Bounds.CentreY - 8f, 16f, 16f), 0.95);

    private static InkMark Circle(InkPageLayout page, int row, int lane) => Around(page.Map.ById($"rate-{row}-{lane}"));

    private static IReadOnlyList<InkResolution> Resolve(RecipeRatingModel model, IEnumerable<InkMark> marks)
    {
        var page = Page(model);
        var readout = new InkReadout(page.Map);
        return new InkResolver(readout).Resolve(InkPageRead.FromMarks(readout, marks, 0.95), RecipeRatingPage.Rules(model), Morning);
    }

    private static TasteInkReading Read(RecipeRatingModel model, IEnumerable<InkMark> marks, bool? didntCook = null)
    {
        var resolutions = Resolve(model, marks);
        return TasteInk.ReadRatings(resolutions, RecipeRatingPage.RatingRows(model), Day, didntCook ?? RecipeRatingPage.DidntCook(resolutions));
    }

    [Fact]
    public void TheRowsTheProfileReadsUseTheIdsThePageDrew()
    {
        var model = Book();
        var page = Page(model);
        var rows = RecipeRatingPage.RatingRows(model);

        Assert.Equal(["rate-1", "rate-2", "rate-3", "rate-4"], rows.Select(r => r.Group));
        Assert.Equal([SamId, LeeId, RobinId, JuniperId], rows.Select(r => r.Person));
        Assert.Equal([Chicken, Chicken, Noodles, Noodles], rows.Select(r => r.Recipe));
        Assert.All(rows, r => Assert.Equal(3, page.Map.Targets.Count(t => t.Group == r.Group)));
        Assert.Equal(
            rows.Select(r => r.Group),
            RecipeRatingPage.Rules(model).OfType<RatingRule>().Select(rule => rule.Group));
        Assert.Equal(
            ["rate-1", "rate-2", "rate-3", "rate-4"],
            RecipeRatingPage.Rules(model).OfType<RatingRule>().Select(rule => rule.Group));
    }

    [Fact]
    public void ACircleOnEachRowIsThatPersonsRatingOfTheirOwnDish()
    {
        var page = Page();
        var reading = Read(Book(), [Circle(page, 1, 2), Circle(page, 3, 3), Circle(page, 4, 1)]);

        Assert.Equal(
            [
                new TasteRating(SamId, Chicken, Day, TasteVerdict.Fine),
                new TasteRating(RobinId, Noodles, Day, TasteVerdict.NeverAgain),
                new TasteRating(JuniperId, Noodles, Day, TasteVerdict.Loved),
            ],
            reading.Ratings);
        Assert.Empty(reading.Unsettled);
    }

    [Fact]
    public void NoCircleIsUnchangedAndInventsNothing()
    {
        var resolutions = Resolve(Book(), []);

        Assert.Equal(4, resolutions.OfType<InkResolution.Unchanged>().Count());
        Assert.Empty(Read(Book(), []).Ratings);
        Assert.Empty(Read(Book(), []).Unsettled);
    }

    [Fact]
    public void ACircleAcrossTwoLanesIsAskedNotGuessed()
    {
        var page = Page();
        var across = new InkMark(new InkRect(190f, page.Map.ById("rate-1-1").Bounds.Top + 8f, 44f, 16f), 0.95);
        var reading = Read(Book(), [across]);

        Assert.Empty(reading.Ratings);
        Assert.Equal(["rate-1"], reading.Unsettled);
        Assert.Contains(Resolve(Book(), [across]), r => r is InkResolution.NeedsAsking { SubjectId: "rate-1" });
    }

    [Fact]
    public void DidntCookIgnoresEveryRatingWhateverWasCircled()
    {
        var page = Page();
        var marks = new[] { Circle(page, 1, 1), Circle(page, 2, 2), Circle(page, 4, 3), Around(page.Map.ById(RecipeIds.DidntCook)) };
        var resolutions = Resolve(Book(), marks);

        Assert.True(RecipeRatingPage.DidntCook(resolutions));
        Assert.Contains(resolutions, r => r is InkResolution.Circled { SubjectId: "rate-1" });
        var reading = TasteInk.ReadRatings(resolutions, RecipeRatingPage.RatingRows(Book()), Day, RecipeRatingPage.DidntCook(resolutions));
        Assert.Empty(reading.Ratings);
        Assert.Empty(reading.Unsettled);
    }

    [Fact]
    public void AScribbleInTheDidntCookBoxIsATick()
    {
        var page = Page();
        var box = page.Map.ById(RecipeIds.DidntCook).Bounds;
        var scribble = new InkMark(new InkRect(box.Left + 2f, box.Top + 3f, 6f, 9f), 0.95);

        Assert.True(RecipeRatingPage.DidntCook(Resolve(Book(), [scribble])));
    }

    [Fact]
    public void ABlankDidntCookBoxMeansWeCookedAndTheRatingsStand()
    {
        var page = Page();
        var resolutions = Resolve(Book(), [Circle(page, 2, 3)]);

        Assert.False(RecipeRatingPage.DidntCook(resolutions));
        Assert.Contains(resolutions, r => r is InkResolution.Skipped { SubjectId: "didnt-cook" });
        Assert.Single(Read(Book(), [Circle(page, 2, 3)]).Ratings);
    }

    [Fact]
    public void APageThatWasNotReadIsNeitherDidntCookNorAnyRating()
    {
        var model = Book();
        var page = Page(model);
        var resolutions = new InkResolver(new InkReadout(page.Map)).Resolve(InkPageRead.NotRead("the call failed"), RecipeRatingPage.Rules(model), Morning);

        Assert.False(RecipeRatingPage.DidntCook(resolutions));
        var reading = TasteInk.ReadRatings(resolutions, RecipeRatingPage.RatingRows(model), Day, false);
        Assert.Empty(reading.Ratings);
        Assert.Equal(["rate-1", "rate-2", "rate-3", "rate-4"], reading.Unsettled);
    }

    [Fact]
    public void ADoubtfulDidntCookReadIsAskedAndIsNotATick()
    {
        var page = Page();
        var box = page.Map.ById(RecipeIds.DidntCook).Bounds;
        var faint = new InkMark(new InkRect(box.Left + 2f, box.Top + 3f, 6f, 9f), 0.4);

        var resolutions = Resolve(Book(), [faint]);

        Assert.False(RecipeRatingPage.DidntCook(resolutions));
        Assert.Contains(resolutions, r => r is InkResolution.NeedsAsking { SubjectId: "didnt-cook" });
    }

    [Fact]
    public void TheExpectedRecipeReadsOnlyTheRatingPage()
    {
        var expected = ExpectedDocument.ForRecipe(RecipeSamples.Timed(), Book(), "Lee", Day, "Sheet-pan chicken");

        Assert.Equal(DeliveryKind.Recipe, expected.Kind);
        Assert.Equal("Constitution — Recipe — 2026-11-17 — Sheet-pan chicken", expected.Name);
        Assert.Equal(DeliveredName.For(DeliveryKind.Recipe, Day, "Sheet-pan chicken"), expected.Name);
        Assert.True(expected.ReadsInk);
        Assert.Null(expected.Session);
        Assert.Null(expected.Marks);
        var page = Assert.Single(expected.Pages);
        Assert.Equal(4, page.Number);
        Assert.Equal(["rate-1", "rate-2", "rate-3", "rate-4"], page.Rules.OfType<RatingRule>().Select(r => r.Group));
        Assert.Contains(page.Rules, r => r is WorkoutBoxRule { BoxId: "didnt-cook" });
        Assert.Equal(13, page.AskedTargets.Count);
        Assert.DoesNotContain(page.AskedTargets, t => t.Kind == InkTargetKind.NotesMargin);
    }

    [Fact]
    public void TheExpectedPageIsThePageBuiltFromTheSameModel()
    {
        var expected = ExpectedDocument.ForRecipe(RecipeSamples.Timed(), Book(), "Lee", Day);
        var built = RecipeWithRating.Build(RecipeSamples.Timed(), Book())[^1];

        Assert.Equal(built.Map.Targets, expected.Pages[0].Readout.Map.Targets);
        Assert.Null(expected.Subject);
        Assert.Equal("Constitution — Recipe — 2026-11-17", expected.Name);
    }

    [Fact]
    public void TheWholeChainResolvesAMarkSetOnTheExpectedPage()
    {
        var expected = ExpectedDocument.ForRecipe(RecipeSamples.Timed(), Book(), "Lee", Day);
        var page = expected.Pages[0];
        var marks = new[] { Around(page.Readout.Map.ById("rate-2-1")), Around(page.Readout.Map.ById("rate-4-2")) };
        var resolutions = new InkResolver(page.Readout).Resolve(InkPageRead.FromMarks(page.Readout, marks, 0.95), page.Rules, Morning);
        var reading = TasteInk.ReadRatings(resolutions, RecipeRatingPage.RatingRows(Book()), Day, RecipeRatingPage.DidntCook(resolutions));

        Assert.Equal(
            [new TasteRating(LeeId, Chicken, Day, TasteVerdict.Loved), new TasteRating(JuniperId, Noodles, Day, TasteVerdict.Fine)],
            reading.Ratings);
    }

    [Fact]
    public void TheExpectedRecipeSurvivesTheReadBackFile()
    {
        var expected = ExpectedDocument.ForRecipe(RecipeSamples.Timed(), Book(), "Lee", Day, "Sheet-pan chicken");

        var folder = Directory.CreateTempSubdirectory("constitution-recipe-");
        try
        {
            var file = Path.Combine(folder.FullName, "expected.json");
            new FileExpectedDocuments(file).Record(expected);
            Check(expected, new FileExpectedDocuments(file).Outstanding().Single().Document);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static void Check(ExpectedDocument expected, ExpectedDocument back)
    {
        Assert.Equal(DeliveryKind.Recipe, back.Kind);
        Assert.Equal(expected.Name, back.Name);
        Assert.Equal(expected.Pages[0].Number, back.Pages[0].Number);
        Assert.Equal(expected.Pages[0].AskedTargets, back.Pages[0].AskedTargets);
        Assert.Equal(expected.Pages[0].Rules, back.Pages[0].Rules);
    }

    [Fact]
    public void ARecipeIsReadTheMorningAfterAndNotTheSameEvening()
    {
        var day = new DateOnly(2026, 11, 17);

        Assert.False(ReadBackPolicy.IsDue(DeliveryKind.Recipe, day, day));
        Assert.True(ReadBackPolicy.IsDue(DeliveryKind.Recipe, day, day.AddDays(1)));
    }

    [Fact]
    public void ARecipeWithNoSessionAndNoPageIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new ExpectedDocument(DeliveryKind.Recipe, Day, null, "Lee", null, []));
    }
}
