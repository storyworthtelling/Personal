using Watch.Domain.Dash;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// People waiting (W-5.1.1, A14; mock: People waiting): newest first, a person their name and one line, the time it
/// came said relatively. No counts and no unread mark (the owner's ruling of 27 Nov 2026).
/// </summary>
public class PeopleWaitingTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    private static readonly DashPeople Three = new(
    [
        new DashPerson("c1111111111111111", "Juniper’s school", "Pickup moves to 3:15 on Friday.", T0.AddHours(-3), Unread: false, Group: false),
        new DashPerson(LeeKey, "Lee", "Running late. I’ll be at the field by four.", T0.AddMinutes(-2), Unread: true, Group: false),
        new DashPerson(GroupKey, "Team snacks", "Photo", T0.AddHours(-1), Unread: true, Group: true),
    ]);

    [Fact]
    public void RowsAreNewestFirst()
    {
        var rows = PeopleWaiting.Rows(Three, T0, Zone);

        Assert.Equal(["Lee", "Team snacks", "Juniper’s school"], rows.Select(row => row.Name));
    }

    [Fact]
    public void ARowKeepsItsNameLineAndWhen()
    {
        var lee = PeopleWaiting.Rows(Three, T0, Zone)[0];

        Assert.Equal(new PersonRow(LeeKey, "Lee", "Running late. I’ll be at the field by four.", "2 min", Unread: true, Group: false), lee);
    }

    [Fact]
    public void TheWhenIsTheWatchsWords()
    {
        Assert.Equal(["2 min", "1 h", "3 h"], PeopleWaiting.Rows(Three, T0, Zone).Select(row => row.When));
    }

    [Fact]
    public void UnreadAndGroupAreKept()
    {
        var rows = PeopleWaiting.Rows(Three, T0, Zone);

        Assert.Equal([true, true, false], rows.Select(row => row.Unread));
        Assert.Equal([false, true, false], rows.Select(row => row.Group));
    }

    [Fact]
    public void NobodyWaitingIsNoRows() => Assert.Empty(PeopleWaiting.Rows(new DashPeople([]), T0, Zone));

    [Fact]
    public void TheSameMomentKeepsPostsOrder()
    {
        var two = new DashPeople(
        [
            new DashPerson(LeeKey, "Lee", "One", T0, Unread: false, Group: false),
            new DashPerson(GroupKey, "Team snacks", "Two", T0, Unread: false, Group: true),
        ]);

        Assert.Equal(["Lee", "Team snacks"], PeopleWaiting.Rows(two, T0, Zone).Select(row => row.Name));
    }
}
