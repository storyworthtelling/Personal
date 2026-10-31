using Constitution.Domain.Ink;

namespace Constitution.Tests.Ink;

/// <summary>
/// C-2.2.1: silence and every mark mean exactly what the book wrote down, one test per rule. The
/// input is what the vision read saw per target; no model is called.
/// </summary>
public class InkResolverTests
{
    private readonly InkFixture fixture = new();

    [Fact]
    public void AnyMarkInABoxIsATick()
    {
        var page = InkFixture.Page(InkFixture.Ink("pushups"));
        Assert.Equal(new InkResolution.Ticked("pushups"), fixture.One(page, InkFixture.WorkoutBox));
    }

    [Fact]
    public void AScribbleInABoxIsStillATick()
    {
        // Scribbled out, written over, nothing legible: the read is sure there is ink (D18).
        var page = InkFixture.Page(new InkTargetRead("pushups", true, 0.9, "xxxx"));
        Assert.IsType<InkResolution.Ticked>(fixture.One(page, InkFixture.WorkoutBox));
    }

    [Fact]
    public void ATickWithAReasonIsADenial()
    {
        var page = InkFixture.Page(InkFixture.Ink("dinner-1"), InkFixture.Ink("dinner-no", "too much dairy"));
        Assert.Equal(new InkResolution.Denied("dinner-1", "too much dairy"), fixture.One(page, InkFixture.Meal));
    }

    [Fact]
    public void AReasonAloneIsAlsoADenial()
    {
        var page = InkFixture.Page(InkFixture.Empty("dinner-1"), InkFixture.Ink("dinner-no", "Lee is away"));
        Assert.Equal(new InkResolution.Denied("dinner-1", "Lee is away"), fixture.One(page, InkFixture.Meal));
    }

    [Fact]
    public void ATickAloneIsATickNotADenial()
    {
        var page = InkFixture.Page(InkFixture.Ink("dinner-1"), InkFixture.Empty("dinner-no"));
        Assert.Equal(new InkResolution.Ticked("dinner-1"), fixture.One(page, InkFixture.Meal));
    }

    [Fact]
    public void AnEmptyWorkoutBoxIsSkipped()
    {
        var page = InkFixture.Page(InkFixture.Empty("pushups"));
        Assert.Equal(new InkResolution.Skipped("pushups"), fixture.One(page, InkFixture.WorkoutBox));
    }

    [Fact]
    public void UntouchedMealItemApprovedAtDeadline()
    {
        var page = InkFixture.Page(InkFixture.Empty("dinner-1"), InkFixture.Empty("dinner-no"));
        Assert.Equal(
            new InkResolution.ApprovedAtDeadline("dinner-1"),
            fixture.One(page, InkFixture.Meal, InkFixture.Deadline));
        Assert.IsType<InkResolution.ApprovedAtDeadline>(
            fixture.One(page, InkFixture.Meal, InkFixture.Deadline.AddDays(1)));
    }

    [Fact]
    public void NothingIsApprovedBeforeTheDeadline()
    {
        var page = InkFixture.Page(InkFixture.Empty("dinner-1"), InkFixture.Empty("dinner-no"));
        var early = InkFixture.Deadline.AddMinutes(-1);
        Assert.Equal(new InkResolution.Undecided("dinner-1"), fixture.One(page, InkFixture.Meal, early));
    }

    [Fact]
    public void AnUncircledRatingIsUnchanged()
    {
        var page = InkFixture.Page(InkFixture.Feel());
        Assert.Equal(new InkResolution.Unchanged("feel"), fixture.One(page, InkFixture.Rating));
    }

    [Fact]
    public void OneCircledLaneIsTheRating()
    {
        var page = InkFixture.Page(InkFixture.Feel("feel-3"));
        Assert.Equal(new InkResolution.Circled("feel", "hard"), fixture.One(page, InkFixture.Rating));
    }

    [Fact]
    public void ABlankStillSoreMeansBetter()
    {
        var page = InkFixture.Page(InkFixture.Empty("sore"));
        Assert.Equal(new InkResolution.Better("sore"), fixture.One(page, InkFixture.StillSore));
    }

    [Fact]
    public void AMarkedStillSoreIsStillSore()
    {
        var page = InkFixture.Page(InkFixture.Ink("sore"));
        Assert.Equal(new InkResolution.StillSore("sore"), fixture.One(page, InkFixture.StillSore));
    }

    [Fact]
    public void ABlankStapleMeansNoNeed()
    {
        var page = InkFixture.Page(InkFixture.Empty("oil"));
        Assert.Equal(new InkResolution.NoNeed("oil"), fixture.One(page, InkFixture.Staple));
    }

    [Fact]
    public void AMarkedStapleIsNeeded()
    {
        var page = InkFixture.Page(InkFixture.Ink("oil", "2 bottles"));
        Assert.Equal(new InkResolution.Needed("oil", "2 bottles"), fixture.One(page, InkFixture.Staple));

        var scribble = InkFixture.Page(InkFixture.Ink("oil"));
        Assert.Equal(new InkResolution.Needed("oil", null), fixture.One(scribble, InkFixture.Staple));
    }

    [Fact]
    public void ACircleWithNoTickMeansPageDone()
    {
        var page = InkFixture.Page(InkFixture.Feel("feel-2"), InkFixture.Empty("done"));
        var resolutions = fixture.Resolve(page, InkFixture.ExercisePage);

        // The rating rides on the page-done entry; the group gets no second one to apply twice.
        Assert.Equal(new InkResolution.PageDone("done", "right"), Assert.Single(resolutions));
    }

    [Fact]
    public void ATickAndACircleAreBothKept()
    {
        var page = InkFixture.Page(InkFixture.Feel("feel-1"), InkFixture.Ink("done"));
        Assert.Equal(
            [new InkResolution.Ticked("done"), new InkResolution.Circled("feel", "easy")],
            fixture.Resolve(page, InkFixture.ExercisePage));
    }

    [Fact]
    public void ATickAloneLeavesTheRatingUnchanged()
    {
        var page = InkFixture.Page(InkFixture.Feel(), InkFixture.Ink("done"));
        Assert.Equal(
            [new InkResolution.Ticked("done"), new InkResolution.Unchanged("feel")],
            fixture.Resolve(page, InkFixture.ExercisePage));
    }

    [Fact]
    public void AnUntouchedExercisePageIsSkipped()
    {
        var page = InkFixture.Page(InkFixture.Feel(), InkFixture.Empty("done"));
        Assert.Equal(
            [new InkResolution.Skipped("done"), new InkResolution.Unchanged("feel")],
            fixture.Resolve(page, InkFixture.ExercisePage));
    }

    [Fact]
    public void ACircleAcrossTwoLanesIsNotGuessed()
    {
        var page = InkFixture.Page(InkFixture.Feel("feel-2", "feel-3"));
        var ask = Assert.IsType<InkResolution.NeedsAsking>(fixture.One(page, InkFixture.Rating));

        Assert.Equal("feel", ask.SubjectId);
        Assert.Equal(["right", "hard"], [ask.First, ask.Second]);
    }

    [Fact]
    public void TwoLanesOnAnExercisePageAskAndDoNotFinishThePage()
    {
        var page = InkFixture.Page(InkFixture.Feel("feel-1", "feel-2"), InkFixture.Empty("done"));
        var resolutions = fixture.Resolve(page, InkFixture.ExercisePage);

        Assert.Equal(
            ["done", "feel"],
            resolutions.Select(r => r.SubjectId));
        Assert.IsType<InkResolution.Undecided>(resolutions[0]);
        Assert.IsType<InkResolution.NeedsAsking>(resolutions[1]);
    }

    [Theory]
    [InlineData(0.74)]
    [InlineData(0.0)]
    public void ALowConfidenceReadIsNotGuessed(double confidence)
    {
        var page = InkFixture.Page(new InkTargetRead("pushups", true, confidence));
        var ask = Assert.IsType<InkResolution.NeedsAsking>(fixture.One(page, InkFixture.WorkoutBox));
        Assert.Equal(["Ticked", "Not ticked"], [ask.First, ask.Second]);
    }

    [Fact]
    public void ExactlyTheFloorIsSure()
    {
        var page = InkFixture.Page(new InkTargetRead("pushups", true, 0.75));
        Assert.IsType<InkResolution.Ticked>(fixture.One(page, InkFixture.WorkoutBox));
    }

    [Fact]
    public void ADoubtfulEmptyBoxIsNotSkipped()
    {
        // Unsure there is nothing there is not the same as nothing there.
        var page = InkFixture.Page(new InkTargetRead("pushups", false, 0.5));
        Assert.IsType<InkResolution.NeedsAsking>(fixture.One(page, InkFixture.WorkoutBox));
    }

    [Fact]
    public void ADoubtfulEmptyMealItemIsNotApproved()
    {
        var page = InkFixture.Page(new InkTargetRead("dinner-1", false, 0.6), InkFixture.Empty("dinner-no"));
        var ask = Assert.IsType<InkResolution.NeedsAsking>(
            fixture.One(page, InkFixture.Meal, InkFixture.Deadline.AddDays(1)));
        Assert.Equal(["Keep it", "Leave it out"], [ask.First, ask.Second]);
    }

    [Fact]
    public void AnUnreadableMealReasonIsAsked()
    {
        // Ink in the reason field with no words: a denial would be a guess and so would an approval.
        var page = InkFixture.Page(InkFixture.Ink("dinner-1"), InkFixture.Ink("dinner-no"));
        Assert.IsType<InkResolution.NeedsAsking>(fixture.One(page, InkFixture.Meal));
    }

    [Fact]
    public void AClearReasonStandsEvenIfTheTickIsDoubtful()
    {
        var page = InkFixture.Page(new InkTargetRead("dinner-1", true, 0.3), InkFixture.Ink("dinner-no", "no time"));
        Assert.Equal(new InkResolution.Denied("dinner-1", "no time"), fixture.One(page, InkFixture.Meal));
    }

    [Fact]
    public void ADoubtfulLaneIsAskedAgainstUnchanged()
    {
        var lanes = new[] { InkFixture.Empty("feel-1"), new InkTargetRead("feel-2", false, 0.4), InkFixture.Empty("feel-3") };
        var ask = Assert.IsType<InkResolution.NeedsAsking>(fixture.One(InkFixture.Page(lanes), InkFixture.Rating));
        Assert.Equal(["right", "Unchanged"], [ask.First, ask.Second]);
    }

    [Fact]
    public void AWrittenNumberIsKept()
    {
        var page = InkFixture.Page(InkFixture.Ink("reach", "12"));
        Assert.Equal(new InkResolution.Written("reach", "12"), fixture.One(page, InkFixture.Number));
    }

    [Fact]
    public void ABlankNumberBoxIsSkipped()
    {
        var page = InkFixture.Page(InkFixture.Empty("reach"));
        Assert.Equal(new InkResolution.Skipped("reach"), fixture.One(page, InkFixture.Number));
    }

    [Fact]
    public void ADoubtfulNumberOffersBothReadings()
    {
        var page = InkFixture.Page(new InkTargetRead("reach", true, 0.6, "12", "17"));
        var ask = Assert.IsType<InkResolution.NeedsAsking>(fixture.One(page, InkFixture.Number));
        Assert.Equal(["12", "17"], [ask.First, ask.Second]);
        Assert.Equal("read:toe reach:12|17", ask.CauseKey);
    }

    [Fact]
    public void ADoubtfulNumberWithOneReadingStillHasTwoChips()
    {
        var page = InkFixture.Page(new InkTargetRead("reach", true, 0.6, "12"));
        var ask = Assert.IsType<InkResolution.NeedsAsking>(fixture.One(page, InkFixture.Number));
        Assert.Equal("12", ask.First);
        Assert.NotEqual(ask.First, ask.Second);
    }

    [Fact]
    public void ResolutionsComeInTheOrderOfTheRules()
    {
        var page = InkFixture.Page(InkFixture.Empty("pushups"), InkFixture.Ink("sore"));
        var rules = new[] { InkFixture.StillSore, InkFixture.WorkoutBox };
        var resolutions = fixture.Resolver.Resolve(page, rules, InkFixture.Deadline);

        Assert.Equal(["sore", "pushups"], resolutions.Select(r => r.SubjectId));
    }
}
