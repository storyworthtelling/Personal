using Constitution.Domain.Ink;
using Constitution.Domain.Workouts;

namespace Constitution.Tests.Workouts;

/// <summary>C-5.1.2 · The assessment's numbers are read back from the ink; what cannot be read is missing, never zero.</summary>
public class AssessmentBaselineTests
{
    private static IReadOnlyList<AssessmentMeasure> All =>
        Enum.GetValues<AssessmentTest>().SelectMany(AssessmentCatalogue.MeasuresOf).ToList();

    [Fact]
    public void TheFourMeasuresAreFrozen()
    {
        Assert.Equal("assess-sit-to-stand", AssessmentCatalogue.SitToStandId);
        Assert.Equal("assess-wall-push-ups", AssessmentCatalogue.WallPushUpsId);
        Assert.Equal("assess-timed-walk", AssessmentCatalogue.TimedWalkId);
        Assert.Equal("assess-toe-reach", AssessmentCatalogue.ToeReachId);
        Assert.Equal(
            [AssessmentCatalogue.SitToStandId, AssessmentCatalogue.WallPushUpsId, AssessmentCatalogue.TimedWalkId, AssessmentCatalogue.ToeReachId],
            All.Select(item => item.Id).ToArray());
    }

    [Fact]
    public void EachTestAsksForWhatThePlanSays()
    {
        var strength = AssessmentCatalogue.MeasuresOf(AssessmentTest.StrengthBaselines);
        Assert.Equal(2, strength.Count);
        Assert.All(strength, item => Assert.IsType<NumberMeasure>(item));

        var walk = Assert.IsType<NumberMeasure>(Assert.Single(AssessmentCatalogue.MeasuresOf(AssessmentTest.TimedWalk)));
        Assert.Equal("miles", walk.Unit);

        var toe = Assert.IsType<CircleMeasure>(Assert.Single(AssessmentCatalogue.MeasuresOf(AssessmentTest.MobilityCheck)));
        Assert.Equal(4, toe.Words.Count);
        Assert.Equal(4, toe.Words.Distinct().Count());
    }

    [Fact]
    public void ATestFollowsItsSessionKind()
    {
        Assert.Equal(AssessmentTest.StrengthBaselines, AssessmentCatalogue.TestFor(SessionKind.Strength));
        Assert.Equal(AssessmentTest.TimedWalk, AssessmentCatalogue.TestFor(SessionKind.OutdoorCardio));
        Assert.Equal(AssessmentTest.TimedWalk, AssessmentCatalogue.TestFor(SessionKind.Treadmill));
        Assert.Equal(AssessmentTest.MobilityCheck, AssessmentCatalogue.TestFor(SessionKind.Mobility));
        Assert.Throws<ArgumentOutOfRangeException>(() => AssessmentCatalogue.TestFor(SessionKind.GentleWalk));
    }

    [Fact]
    public void NumbersAreReadBack()
    {
        var toe = (CircleMeasure)AssessmentCatalogue.MeasuresOf(AssessmentTest.MobilityCheck).Single();
        var read = AssessmentBaselines.Read(
            All,
            [
                new InkResolution.Written(AssessmentCatalogue.SitToStandId, " 12 "),
                new InkResolution.Written(AssessmentCatalogue.WallPushUpsId, "0"),
                new InkResolution.Written(AssessmentCatalogue.TimedWalkId, "0.75"),
                new InkResolution.Circled(AssessmentCatalogue.ToeReachId, toe.Words[1]),
            ]);

        Assert.Equal(12m, read.Numbers[AssessmentCatalogue.SitToStandId]);
        Assert.Equal(0m, read.Numbers[AssessmentCatalogue.WallPushUpsId]);
        Assert.Equal(0.75m, read.Numbers[AssessmentCatalogue.TimedWalkId]);
        Assert.Equal(toe.Words[1], read.Choices[AssessmentCatalogue.ToeReachId]);
        Assert.Empty(read.Missing);
    }

    [Theory]
    [InlineData("twelve")]
    [InlineData("-3")]
    [InlineData("")]
    [InlineData("1,5")]
    public void AnUnreadableNumberIsMissingNotZero(string written)
    {
        var read = AssessmentBaselines.Read(All, [new InkResolution.Written(AssessmentCatalogue.SitToStandId, written)]);

        Assert.DoesNotContain(AssessmentCatalogue.SitToStandId, read.Numbers.Keys);
        Assert.Contains(AssessmentCatalogue.SitToStandId, read.Missing);
    }

    [Fact]
    public void ABlankOrUnreadBoxIsMissing()
    {
        var read = AssessmentBaselines.Read(
            All,
            [
                new InkResolution.Skipped(AssessmentCatalogue.SitToStandId),
                new InkResolution.NotRead(AssessmentCatalogue.WallPushUpsId, "page not read"),
            ]);

        Assert.Empty(read.Numbers);
        Assert.Equal(
            [AssessmentCatalogue.SitToStandId, AssessmentCatalogue.WallPushUpsId, AssessmentCatalogue.TimedWalkId, AssessmentCatalogue.ToeReachId],
            read.Missing.ToArray());
    }

    [Fact]
    public void AWordOutsideTheFourIsMissing()
    {
        var read = AssessmentBaselines.Read(All, [new InkResolution.Circled(AssessmentCatalogue.ToeReachId, "Ceiling")]);

        Assert.Empty(read.Choices);
        Assert.Contains(AssessmentCatalogue.ToeReachId, read.Missing);
    }

    [Fact]
    public void AnswersToOtherBoxesAreIgnored()
    {
        var read = AssessmentBaselines.Read(All, [new InkResolution.Written("some-other-box", "40")]);

        Assert.Empty(read.Numbers);
        Assert.Equal(4, read.Missing.Count);
    }
}
