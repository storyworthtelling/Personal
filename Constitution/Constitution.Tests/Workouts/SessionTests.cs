using Constitution.Domain.Equipment;
using Constitution.Domain.Workouts;

namespace Constitution.Tests.Workouts;

/// <summary>A session is a value that cannot be built wrong, and whose equipment is read off its exercises.</summary>
public class SessionTests
{
    private static readonly TimeSpan Thirty = TimeSpan.FromMinutes(30);

    [Fact]
    public void ASessionNeedsExercises()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Session(SessionKind.Strength, Thirty, false, []));
    }

    [Fact]
    public void ASessionTakesSomeTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Session(SessionKind.Mobility, TimeSpan.Zero, false, [new Exercise("Stretch")]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Session(SessionKind.Mobility, TimeSpan.FromMinutes(-5), false, [new Exercise("Stretch")]));
    }

    [Fact]
    public void AnExerciseNeedsAName()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Exercise("  "));
        Assert.ThrowsAny<ArgumentException>(() => new Exercise(""));
    }

    [Fact]
    public void AnExerciseCannotNeedAnItemTheChecklistDoesNotKnow()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Exercise("Row", "rowing-machine"));
    }

    [Fact]
    public void EquipmentIsReadOffTheExercisesOnceEachInChecklistOrder()
    {
        var session = new Session(
            SessionKind.Strength,
            Thirty,
            false,
            [
                new Exercise("Sit to stand from a chair"),
                new Exercise("Glute bridge", EquipmentCatalogue.MatId),
                new Exercise("Band row", EquipmentCatalogue.BandsId),
                new Exercise("Another bridge", EquipmentCatalogue.MatId),
            ]);

        Assert.Equal([EquipmentCatalogue.BandsId, EquipmentCatalogue.MatId], session.EquipmentNeeded);
    }

    [Fact]
    public void ATreadmillSessionAlwaysNeedsTheTreadmill()
    {
        var session = new Session(SessionKind.Treadmill, Thirty, true, [new Exercise("Warm-up walk")]);

        Assert.Equal([EquipmentCatalogue.TreadmillId], session.EquipmentNeeded);
    }

    [Fact]
    public void OnlyEffortKindsAreHard()
    {
        Assert.True(SessionKinds.IsHard(SessionKind.Strength));
        Assert.True(SessionKinds.IsHard(SessionKind.OutdoorCardio));
        Assert.True(SessionKinds.IsHard(SessionKind.Treadmill));
        Assert.False(SessionKinds.IsHard(SessionKind.Mobility));
        Assert.False(SessionKinds.IsHard(SessionKind.GentleWalk));
    }
}
