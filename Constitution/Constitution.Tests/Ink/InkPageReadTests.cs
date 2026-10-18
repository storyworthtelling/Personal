using Constitution.Domain.Documents;
using Constitution.Domain.Ink;

namespace Constitution.Tests.Ink;

/// <summary>
/// "Page not read" is not "box empty". Whatever did not get read is reported as not read, never
/// defaulted: this is the half of C-2.2.1 that keeps silence honest.
/// </summary>
public class InkPageReadTests
{
    private readonly InkFixture fixture = new();

    public static IEnumerable<object[]> EveryRule()
    {
        yield return [InkFixture.WorkoutBox];
        yield return [InkFixture.ExercisePage];
        yield return [InkFixture.Meal];
        yield return [InkFixture.Rating];
        yield return [InkFixture.StillSore];
        yield return [InkFixture.Staple];
        yield return [InkFixture.Number];
    }

    [Theory]
    [MemberData(nameof(EveryRule))]
    public void AnUnreadPageResolvesNothing(InkRule rule)
    {
        var page = InkPageRead.NotRead("the call failed");
        var pastEverything = InkFixture.Deadline.AddDays(30);

        var resolutions = fixture.Resolve(page, rule, pastEverything);

        Assert.NotEmpty(resolutions);
        Assert.All(resolutions, r =>
        {
            var notRead = Assert.IsType<InkResolution.NotRead>(r);
            Assert.Equal("the call failed", notRead.Why);
        });
    }

    [Theory]
    [MemberData(nameof(EveryRule))]
    public void AReadThatLeftTargetsOutIsNotSilence(InkRule rule)
    {
        var page = InkPageRead.Read([]);
        var resolutions = fixture.Resolve(page, rule, InkFixture.Deadline.AddDays(30));

        Assert.NotEmpty(resolutions);
        Assert.All(resolutions, r => Assert.IsType<InkResolution.NotRead>(r));
    }

    [Fact]
    public void AMissingLaneIsNotAnUncircledRating()
    {
        var page = InkFixture.Page(InkFixture.Empty("feel-1"), InkFixture.Empty("feel-3"));
        Assert.IsType<InkResolution.NotRead>(fixture.One(page, InkFixture.Rating));
    }

    [Fact]
    public void AMissingReasonFieldIsNotAnApproval()
    {
        var page = InkFixture.Page(InkFixture.Empty("dinner-1"));
        Assert.IsType<InkResolution.NotRead>(fixture.One(page, InkFixture.Meal, InkFixture.Deadline.AddDays(1)));
    }

    [Fact]
    public void AMissingRatingDoesNotUndoATick()
    {
        var page = InkFixture.Page(InkFixture.Ink("done"));
        var resolutions = fixture.Resolve(page, InkFixture.ExercisePage);

        Assert.Equal(new InkResolution.Ticked("done"), resolutions[0]);
        Assert.IsType<InkResolution.NotRead>(resolutions[1]);
    }

    [Fact]
    public void AnEmptyDoneBoxWithNoRatingReadIsNotSkipped()
    {
        var page = InkFixture.Page(InkFixture.Empty("done"));
        var resolutions = fixture.Resolve(page, InkFixture.ExercisePage);

        Assert.All(resolutions, r => Assert.IsType<InkResolution.NotRead>(r));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.PositiveInfinity)]
    public void AConfidenceThatIsNotAProbabilityIsRefused(double confidence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new InkTargetRead("pushups", true, confidence));
    }

    [Fact]
    public void ABlankTargetIdIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new InkTargetRead(" ", true, 0.9));
    }

    [Fact]
    public void WritingIsTrimmedAndBlankMeansNone()
    {
        Assert.Equal("12", new InkTargetRead("reach", true, 0.9, "  12 ").Text);
        Assert.Null(new InkTargetRead("reach", true, 0.9, "   ").Text);
    }

    [Fact]
    public void TheSameTargetReadTwiceIsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
            InkPageRead.Read([InkFixture.Empty("pushups"), InkFixture.Ink("pushups")]));
    }

    [Fact]
    public void ARuleNamingAnUnknownTargetIsRefused()
    {
        var page = InkFixture.Page(InkFixture.Empty("pushups"));
        var rule = new WorkoutBoxRule("squats", "not-on-this-page");
        Assert.Throws<KeyNotFoundException>(() => fixture.Resolve(page, rule));
    }

    [Fact]
    public void ARatingGroupWithNoLanesIsRefused()
    {
        var page = InkFixture.Page(InkFixture.Empty("pushups"));
        Assert.Throws<ArgumentException>(() => fixture.Resolve(page, new RatingRule("squats", "no-such-group")));
    }

    [Fact]
    public void ABlankSubjectIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new WorkoutBoxRule(" ", "pushups"));
    }

    [Fact]
    public void AMarkInsideABoxMarksOnlyThatBox()
    {
        var box = fixture.Readout.Map.ById("pushups").Bounds;
        var mark = new InkMark(new InkRect(box.Left + 4, box.Top + 4, 12, 12), 0.9);

        var page = InkPageRead.FromMarks(fixture.Readout, [mark], 0.9);

        Assert.True(page.WasRead);
        Assert.True(page.For("pushups")!.Marked);
        Assert.False(page.For("done")!.Marked);
        Assert.Equal(0.9, page.For("pushups")!.Confidence);
    }

    [Fact]
    public void ACircleAcrossTwoLanesMarksBoth()
    {
        // 100..150 across the hairline at x = 124 between the first two lanes (40..124, 124..208).
        var circle = new InkMark(new InkRect(100, 300, 50, 32), 0.9);
        var page = InkPageRead.FromMarks(fixture.Readout, [circle], 0.9);

        Assert.Equal(
            [true, true, false],
            new[] { "feel-1", "feel-2", "feel-3" }.Select(id => page.For(id)!.Marked));

        var ask = Assert.IsType<InkResolution.NeedsAsking>(fixture.One(page, InkFixture.Rating));
        Assert.Equal(["easy", "right"], [ask.First, ask.Second]);
    }

    [Fact]
    public void UntouchedTargetsReadAsEmptyAtTheStatedConfidence()
    {
        var page = InkPageRead.FromMarks(fixture.Readout, [], 0.88);

        Assert.All(fixture.Readout.Map.Targets, t =>
        {
            var read = page.For(t.Id)!;
            Assert.False(read.Marked);
            Assert.Equal(0.88, read.Confidence);
        });
    }

    [Fact]
    public void AWeakMarkMakesItsTargetDoubtful()
    {
        var box = fixture.Readout.Map.ById("pushups").Bounds;
        var weak = new InkMark(new InkRect(box.Left + 4, box.Top + 4, 12, 12), 0.5);
        var strong = new InkMark(new InkRect(box.Left + 2, box.Top + 2, 4, 4), 0.99);

        var page = InkPageRead.FromMarks(fixture.Readout, [weak, strong], 0.9);

        Assert.Equal(0.5, page.For("pushups")!.Confidence);
        Assert.IsType<InkResolution.NeedsAsking>(fixture.One(page, InkFixture.WorkoutBox));
    }

    [Fact]
    public void WritingInAFieldIsCarriedToIt()
    {
        var field = fixture.Readout.Map.ById("dinner-no").Bounds;
        var words = new InkMark(new InkRect(field.Left + 5, field.Top + 5, 80, 20), 0.9, "no time");

        var page = InkPageRead.FromMarks(fixture.Readout, [words], 0.9);

        Assert.Equal("no time", page.For("dinner-no")!.Text);
        Assert.Equal(
            new InkResolution.Denied("dinner-1", "no time"),
            fixture.One(page, InkFixture.Meal));
    }
}
