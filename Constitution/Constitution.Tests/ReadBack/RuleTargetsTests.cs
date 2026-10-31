using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;
using Constitution.Tests.Ink;

namespace Constitution.Tests.ReadBack;

/// <summary>
/// What each rule reads off the page. The resolver keeps this knowledge to itself, so these tests hold the
/// read-back's copy of it to the resolver: a page read of exactly the boxes named here is enough for the
/// resolver to answer, and nothing it asks for is missing.
/// </summary>
public class RuleTargetsTests
{
    private readonly InkFixture fixture = new();

    private IReadOnlyList<string> Ids(InkRule rule) => RuleTargets.For(rule, fixture.Readout.Map).Select(target => target.Id).ToList();

    [Fact]
    public void AWorkoutBoxReadsItsOwnBox() => Assert.Equal(["pushups"], Ids(InkFixture.WorkoutBox));

    [Fact]
    public void AnExercisePageReadsDoneAndEveryLane() =>
        Assert.Equal(["done", "feel-1", "feel-2", "feel-3"], Ids(InkFixture.ExercisePage));

    [Fact]
    public void AMealRowReadsTheTickAndTheReason() => Assert.Equal(["dinner-1", "dinner-no"], Ids(InkFixture.Meal));

    [Fact]
    public void ARatingReadsEveryLaneOfItsGroup() => Assert.Equal(["feel-1", "feel-2", "feel-3"], Ids(InkFixture.Rating));

    [Fact]
    public void TheOtherRulesReadOneBoxEach()
    {
        Assert.Equal(["sore"], Ids(InkFixture.StillSore));
        Assert.Equal(["oil"], Ids(InkFixture.Staple));
        Assert.Equal(["reach"], Ids(InkFixture.Number));
    }

    [Fact]
    public void ABoxTheMapLacksIsLeftOut()
    {
        var rule = new ExercisePageRule("squats", "no-such-box", "feel");

        Assert.Equal(["feel-1", "feel-2", "feel-3"], Ids(rule));
    }

    public static TheoryData<InkRule, string[]> Subjects => new()
    {
        { InkFixture.WorkoutBox, ["pushups"] },
        { InkFixture.ExercisePage, ["done", "feel"] },
        { InkFixture.Meal, ["dinner-1"] },
        { InkFixture.Rating, ["feel"] },
        { InkFixture.StillSore, ["sore"] },
        { InkFixture.Staple, ["oil"] },
        { InkFixture.Number, ["reach"] },
    };

    [Theory]
    [MemberData(nameof(Subjects))]
    public void ARuleIsReportedUnderTheIdsTheResolverUses(InkRule rule, string[] expected)
    {
        Assert.Equal(expected, RuleTargets.SubjectIds(rule));
    }

    public static TheoryData<InkRule> EveryRule => new()
    {
        InkFixture.WorkoutBox, InkFixture.ExercisePage, InkFixture.Meal, InkFixture.Rating,
        InkFixture.StillSore, InkFixture.Staple, InkFixture.Number,
    };

    [Theory]
    [MemberData(nameof(EveryRule))]
    public void TheResolverNeedsNoBoxBeyondTheOnesNamed(InkRule rule)
    {
        var reads = RuleTargets.For(rule, fixture.Readout.Map).Select(target => new InkTargetRead(target.Id, false, InkFixture.Sure));
        var page = InkPageRead.Read(reads);

        var resolutions = fixture.Resolver.Resolve(page, [rule], InkFixture.Deadline.AddHours(1));

        Assert.NotEmpty(resolutions);
        Assert.DoesNotContain(resolutions, resolution => resolution is InkResolution.NotRead);
    }

    [Theory]
    [MemberData(nameof(EveryRule))]
    public void TheResolverReportsUnderTheNamedIds(InkRule rule)
    {
        var reads = RuleTargets.For(rule, fixture.Readout.Map).Select(target => new InkTargetRead(target.Id, false, InkFixture.Sure));
        var page = InkPageRead.Read(reads);

        var resolutions = fixture.Resolver.Resolve(page, [rule], InkFixture.Deadline.AddHours(1));

        Assert.All(resolutions, resolution => Assert.Contains(resolution.SubjectId, RuleTargets.SubjectIds(rule)));
    }
}
