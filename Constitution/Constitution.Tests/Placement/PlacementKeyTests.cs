using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;

namespace Constitution.Tests.Placement;

/// <summary>
/// The hidden key is stored on events in the owner's Google Calendar, so its spelling is a frozen
/// identifier: change it and every event already written is a stranger to the next run. The expected
/// strings below are written out as literals for that reason.
/// </summary>
public class PlacementKeyTests
{
    [Fact]
    public void WorkoutKeysAreFrozen()
    {
        Assert.Equal("c1:sam:strength:105703:0", PlacementKey.ForWorkout("Sam", SessionKind.Strength, 105703, 0).Value);
        Assert.Equal("c1:lee:gentlewalk:105704:2", PlacementKey.ForWorkout("Lee", SessionKind.GentleWalk, 105704, 2).Value);
    }

    [Fact]
    public void HouseholdKeysAreFrozen()
    {
        Assert.Equal("c1:lee:household:105703:dinner-a", PlacementKey.ForHousehold("Lee", 105703, "dinner-a").Value);
    }

    [Fact]
    public void TheExtendedPropertyNameIsFrozen()
    {
        Assert.Equal("constitutionKey", PlacementKey.Property);
    }

    [Fact]
    public void EverySessionKindHasAFrozenName()
    {
        // A new SessionKind has to be named here on purpose: enum names are never used for a stored key.
        var kinds = Enum.GetValues<SessionKind>();
        var names = kinds.Select(kind => PlacementKey.ForWorkout("Sam", kind, 1, 0).Value.Split(':')[2]).ToList();

        Assert.Equal(5, kinds.Length);
        Assert.Equal(["strength", "outdoorcardio", "treadmill", "mobility", "gentlewalk"], names);
    }

    [Fact]
    public void TheWeekIndexIsTheWeekNumber()
    {
        // The household's week runs Sunday to Saturday: Sunday 1 November opens week 105703, and the Monday after it is in it.
        var sunday = new DateOnly(2026, 11, 1);

        Assert.Equal(105703, PlacementKey.WeekIndexOf(sunday));
        Assert.Equal(105703, PlacementKey.WeekIndexOf(sunday.AddDays(1)));
        Assert.Equal(PlacementKey.WeekIndexOf(sunday), PlacementKey.WeekIndexOf(sunday.AddDays(6)));
        Assert.Equal(PlacementKey.WeekIndexOf(sunday) + 1, PlacementKey.WeekIndexOf(sunday.AddDays(7)));
    }

    [Fact]
    public void ANameIsMatchedWithoutCase()
    {
        Assert.Equal(
            PlacementKey.ForWorkout("SAM", SessionKind.Strength, 5, 0),
            PlacementKey.ForWorkout(" sam ", SessionKind.Strength, 5, 0));
    }

    [Fact]
    public void AnAwkwardNameCannotForgeAnotherKey()
    {
        var forged = PlacementKey.ForWorkout("sam:strength:5", SessionKind.Mobility, 6, 0);

        Assert.NotEqual(PlacementKey.ForWorkout("sam", SessionKind.Strength, 5, 0), forged);
        Assert.Equal(5, forged.Value.Split(':').Length);
        Assert.Equal(5, PlacementKey.ForHousehold("a:b", 1, "c:d").Value.Split(':').Length);
    }

    [Fact]
    public void KeysDifferByEveryPartOfTheirIdentity()
    {
        var keys = new[]
        {
            PlacementKey.ForWorkout("Sam", SessionKind.Strength, 5, 0),
            PlacementKey.ForWorkout("Lee", SessionKind.Strength, 5, 0),
            PlacementKey.ForWorkout("Sam", SessionKind.Mobility, 5, 0),
            PlacementKey.ForWorkout("Sam", SessionKind.Strength, 6, 0),
            PlacementKey.ForWorkout("Sam", SessionKind.Strength, 5, 1),
            PlacementKey.ForHousehold("Sam", 5, "0"),
        };

        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Theory]
    [InlineData("cook-2026-11-03")]
    [InlineData("cook 2026/11/03")]
    [InlineData("cook-20261103")]
    [InlineData("cook 11/03/2026")]
    [InlineData("cook-3.11.2026")]
    [InlineData("cook-110326")]
    public void ADateInAnIdentityIsRefused(string identity)
    {
        Assert.Throws<ArgumentException>(() => PlacementKey.ForHousehold("Lee", 5, identity));
    }

    [Theory]
    [InlineData("dinner-a")]
    [InlineData("shop 2")]
    [InlineData("slow-cooker")]
    public void AnIdentityWithNoDateIsKept(string identity)
    {
        Assert.StartsWith("c1:lee:household:5:", PlacementKey.ForHousehold("Lee", 5, identity).Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ABlankOrNegativePartIsRefused()
    {
        Assert.Throws<ArgumentException>(() => PlacementKey.ForWorkout(" ", SessionKind.Strength, 5, 0));
        Assert.Throws<ArgumentException>(() => PlacementKey.ForHousehold(" ", 5, "a"));
        Assert.Throws<ArgumentException>(() => PlacementKey.ForHousehold("Sam", 5, " "));
        Assert.ThrowsAny<ArgumentException>(() => PlacementKey.ForWorkout("Sam", SessionKind.Strength, -1, 0));
        Assert.ThrowsAny<ArgumentException>(() => PlacementKey.ForWorkout("Sam", SessionKind.Strength, 5, -1));
        Assert.ThrowsAny<ArgumentException>(() => PlacementKey.ForWorkout("Sam", (SessionKind)99, 5, 0));
    }

    [Fact]
    public void AStoredKeyComesBackAsTheSameKey()
    {
        var key = PlacementKey.ForWorkout("Sam", SessionKind.Treadmill, 105703, 1);

        Assert.Equal(key, PlacementKey.FromStored(key.Value));
        Assert.Equal(key, PlacementKey.FromStored(" " + key.Value + " "));
        var household = PlacementKey.ForHousehold("Lee", 105703, "slow cooker");
        Assert.Equal(household, PlacementKey.FromStored(household.Value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense")]
    [InlineData("c2:sam:strength:5:0")]
    [InlineData("c1:sam:strength:5")]
    [InlineData("c1:sam:strength:5:0:extra")]
    public void AStoredKeyThatIsNotOursIsRefused(string stored)
    {
        Assert.Throws<ArgumentException>(() => PlacementKey.FromStored(stored));
    }

    [Fact]
    public void ADefaultKeyIsEmptyNotNull()
    {
        Assert.Equal(string.Empty, default(PlacementKey).Value);
        Assert.Equal(string.Empty, default(PlacementKey).ToString());
    }
}
