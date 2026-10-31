using Constitution.Domain.Workouts;

namespace Constitution.Tests.Workouts;

/// <summary>How many sessions a week asks for. A "mostly inactive, starting fresh" owner starts at three and earns a fourth.</summary>
public class WorkoutFrequencyTests
{
    [Fact]
    public void ANewInstallAsksForThree()
    {
        Assert.Equal(3, WorkoutFrequency.Target(RecoveryInputs.Fresh));
    }

    [Fact]
    public void TwoSkipsEaseItToTwo()
    {
        Assert.Equal(2, WorkoutFrequency.Target(new RecoveryInputs { SessionsDoneLastWeek = 1, SessionsSkippedLastWeek = 2 }));
    }

    [Fact]
    public void OneSkipDoesNotEaseIt()
    {
        Assert.Equal(3, WorkoutFrequency.Target(new RecoveryInputs { SessionsDoneLastWeek = 2, SessionsSkippedLastWeek = 1 }));
    }

    [Fact]
    public void AFullWeekDoneEarnsAFourth()
    {
        Assert.Equal(4, WorkoutFrequency.Target(new RecoveryInputs { SessionsDoneLastWeek = 3, SessionsSkippedLastWeek = 0 }));
    }

    [Fact]
    public void ASkipInAFullWeekKeepsItAtThree()
    {
        Assert.Equal(3, WorkoutFrequency.Target(new RecoveryInputs { SessionsDoneLastWeek = 3, SessionsSkippedLastWeek = 1 }));
    }

    [Fact]
    public void ItNeverRisesPastFour()
    {
        Assert.Equal(4, WorkoutFrequency.Target(new RecoveryInputs { SessionsDoneLastWeek = 9, SessionsSkippedLastWeek = 0 }));
    }

    [Fact]
    public void NothingDoneAndNothingSkippedIsStillThree()
    {
        Assert.Equal(3, WorkoutFrequency.Target(new RecoveryInputs { SessionsDoneLastWeek = 0, SessionsSkippedLastWeek = 0 }));
    }

    [Fact]
    public void TheMenuIsBalancedAtEachSize()
    {
        Assert.Equal([SessionKind.Strength, SessionKind.OutdoorCardio], WorkoutFrequency.Menu(2));
        Assert.Equal([SessionKind.Strength, SessionKind.OutdoorCardio, SessionKind.Mobility], WorkoutFrequency.Menu(3));
        Assert.Equal([SessionKind.Strength, SessionKind.OutdoorCardio, SessionKind.Mobility, SessionKind.Strength], WorkoutFrequency.Menu(4));
    }
}
