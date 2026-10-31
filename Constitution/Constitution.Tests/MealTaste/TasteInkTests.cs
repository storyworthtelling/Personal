using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using static Constitution.Tests.MealTaste.TasteData;

namespace Constitution.Tests.MealTaste;

/// <summary>The rating page and the meal sheet, read through the ink resolver's outcomes: an uncircled rating is unchanged, a written denial is evidence.</summary>
public class TasteInkTests
{
    private static readonly TasteRatingRow[] Rows =
    [
        new("rate-sam", Sam, Pasta),
        new("rate-lee", Lee, Pasta),
        new("rate-robin", Robin, Burritos),
        new("rate-juniper", Juniper, Burritos),
    ];

    private static InkResolution Asked(string group) => new InkResolution.NeedsAsking(group, "the rating", "Loved", "Fine", "read:the rating:loved|fine");

    [Fact]
    public void ACircledLaneIsThatPersonsRating()
    {
        var reading = TasteInk.ReadRatings([new InkResolution.Circled("rate-sam", "Loved")], Rows, Day(3), didntCook: false);

        var rating = Assert.Single(reading.Ratings);
        Assert.Equal(new TasteRating(Sam, Pasta, Day(3), TasteVerdict.Loved), rating);
    }

    [Fact]
    public void EachRowKeepsTheDishThatPersonAte()
    {
        var reading = TasteInk.ReadRatings(
            [new InkResolution.Circled("rate-sam", "Fine"), new InkResolution.Circled("rate-robin", "Never again")], Rows, Day(3), didntCook: false);

        Assert.Contains(new TasteRating(Sam, Pasta, Day(3), TasteVerdict.Fine), reading.Ratings);
        Assert.Contains(new TasteRating(Robin, Burritos, Day(3), TasteVerdict.NeverAgain), reading.Ratings);
        Assert.Equal(2, reading.Ratings.Count);
    }

    [Fact]
    public void AnUncircledRatingIsUnchangedAndInventsNothing()
    {
        var reading = TasteInk.ReadRatings(Rows.Select(row => (InkResolution)new InkResolution.Unchanged(row.Group)).ToList(), Rows, Day(3), didntCook: false);

        Assert.Empty(reading.Ratings);
        Assert.Empty(reading.Unsettled);
    }

    [Fact]
    public void ARowTheResolverNeverReportedIsUnsettledNotAssumed()
    {
        var reading = TasteInk.ReadRatings([new InkResolution.Circled("rate-sam", "Loved")], Rows, Day(3), didntCook: false);

        Assert.Equal(["rate-lee", "rate-robin", "rate-juniper"], reading.Unsettled);
    }

    [Fact]
    public void AskedUnreadAndUndecidedRowsAreUnsettledWithNoRating()
    {
        var reading = TasteInk.ReadRatings(
        [
            Asked("rate-sam"),
            new InkResolution.NotRead("rate-lee", "The page was not read."),
            new InkResolution.Undecided("rate-robin"),
            new InkResolution.Unchanged("rate-juniper"),
        ], Rows, Day(3), didntCook: false);

        Assert.Empty(reading.Ratings);
        Assert.Equal(["rate-sam", "rate-lee", "rate-robin"], reading.Unsettled);
    }

    [Fact]
    public void AWordThatIsNoLaneIsUnsettledNotGuessed()
    {
        var reading = TasteInk.ReadRatings([new InkResolution.Circled("rate-sam", "Adored")], Rows.Take(1).ToList(), Day(3), didntCook: false);

        Assert.Empty(reading.Ratings);
        Assert.Equal(["rate-sam"], reading.Unsettled);
    }

    [Fact]
    public void ADifferentKindOfOutcomeOnARatingRowIsUnsettled()
    {
        var reading = TasteInk.ReadRatings([new InkResolution.Ticked("rate-sam")], Rows.Take(1).ToList(), Day(3), didntCook: false);

        Assert.Empty(reading.Ratings);
        Assert.Equal(["rate-sam"], reading.Unsettled);
    }

    [Fact]
    public void DidntCookIgnoresEveryRatingOnThePage()
    {
        var reading = TasteInk.ReadRatings(
            [new InkResolution.Circled("rate-sam", "Never again"), new InkResolution.Circled("rate-lee", "Loved")], Rows, Day(3), didntCook: true);

        Assert.Empty(reading.Ratings);
        Assert.Empty(reading.Unsettled);
    }

    [Fact]
    public void ResolutionsForOtherSubjectsAreNotMistakenForRatings()
    {
        var reading = TasteInk.ReadRatings([new InkResolution.Circled("somebody-elses-group", "Loved")], Rows.Take(1).ToList(), Day(3), didntCook: false);

        Assert.Empty(reading.Ratings);
    }

    [Fact]
    public void AWrittenDenialIsEvidenceForTheDishItRefused()
    {
        var evidence = TasteInk.ReadDenials(
            [new InkResolution.Denied("item-curry", "we had lentils on Sunday and I am not doing it again")],
            [new TasteDenialRow("item-curry", Lee, Curry)], Day(4));

        var found = Assert.Single(evidence);
        Assert.Equal(new TasteEvidence(Lee, TasteEvidenceKind.Denial, "we had lentils on Sunday and I am not doing it again", Curry, Day(4)), found);
    }

    [Fact]
    public void ALeaveItOutWithNoWordsLearnsNothing()
    {
        var evidence = TasteInk.ReadDenials([new InkResolution.Denied("item-curry", null)], [new TasteDenialRow("item-curry", Lee, Curry)], Day(4));

        Assert.Empty(evidence);
    }

    [Fact]
    public void ABlankReasonLearnsNothing()
    {
        var evidence = TasteInk.ReadDenials([new InkResolution.Denied("item-curry", "   ")], [new TasteDenialRow("item-curry", Lee, Curry)], Day(4));

        Assert.Empty(evidence);
    }

    [Fact]
    public void ApprovalsAndSilenceLearnNothing()
    {
        var evidence = TasteInk.ReadDenials(
        [
            new InkResolution.Ticked("item-curry"),
            new InkResolution.ApprovedAtDeadline("item-tart"),
            new InkResolution.Undecided("item-pasta"),
            new InkResolution.NotRead("item-tacos", "not read"),
            Asked("item-omelette"),
        ],
        [
            new TasteDenialRow("item-curry", Lee, Curry), new TasteDenialRow("item-tart", Lee, Tart), new TasteDenialRow("item-pasta", Lee, Pasta),
            new TasteDenialRow("item-tacos", Lee, Tacos), new TasteDenialRow("item-omelette", Lee, Omelette),
        ], Day(4));

        Assert.Empty(evidence);
    }

    [Fact]
    public void ALongDenialIsCutToTheEvidenceLimitNotRefused()
    {
        var evidence = TasteInk.ReadDenials(
            [new InkResolution.Denied("item-curry", new string('a', TasteEvidence.MaxWordsLength + 50))],
            [new TasteDenialRow("item-curry", Lee, Curry)], Day(4));

        Assert.Equal(TasteEvidence.MaxWordsLength, Assert.Single(evidence).Words.Length);
    }
}
