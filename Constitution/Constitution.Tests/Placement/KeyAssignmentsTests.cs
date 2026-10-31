using Constitution.Domain.Placement;
using Constitution.Domain.Workouts;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Placement;

/// <summary>
/// B2: a workout's number is issued to a session once and kept. The record of what was issued is what a later run reads
/// to give the same session the same key however the week has since been re-planned, so its text is a frozen identifier
/// (it is read back on every run), and a number is never given out twice.
/// </summary>
public class KeyAssignmentsTests
{
    private const int Week = 105703;

    private static KeyIssue Issue(DayOfWeek weekday, int ordinal, string person = Sam, SessionKind kind = SessionKind.Strength, int slot = 0, int week = Week) =>
        new(person, week, kind, weekday, slot, ordinal);

    // ---- the record ----

    [Fact]
    public void NothingIsIssuedToBeginWith()
    {
        Assert.Empty(KeyAssignments.Empty.Issues);
        Assert.Null(KeyAssignments.Empty.HighestOrdinal(Sam, Week, SessionKind.Strength));
        Assert.False(KeyAssignments.Empty.Vouches(PlacementKey.ForWorkout(Sam, SessionKind.Strength, Week, 0)));
    }

    [Fact]
    public void AnIssueIsFoundByItsIdentity()
    {
        var record = KeyAssignments.Empty.With([Issue(DayOfWeek.Wednesday, 4)]);

        Assert.Equal(4, record.Find(Sam, Week, SessionKind.Strength, DayOfWeek.Wednesday, 0)!.Ordinal);
        Assert.Null(record.Find(Sam, Week, SessionKind.Strength, DayOfWeek.Thursday, 0));
        Assert.Null(record.Find(Sam, Week, SessionKind.Mobility, DayOfWeek.Wednesday, 0));
        Assert.Null(record.Find(Lee, Week, SessionKind.Strength, DayOfWeek.Wednesday, 0));
        Assert.Null(record.Find(Sam, Week + 1, SessionKind.Strength, DayOfWeek.Wednesday, 0));
        Assert.Null(record.Find(Sam, Week, SessionKind.Strength, DayOfWeek.Wednesday, 1));
    }

    [Fact]
    public void AnIssueVouchesForItsOwnKeyOnly()
    {
        var record = KeyAssignments.Empty.With([Issue(DayOfWeek.Monday, 0), Issue(DayOfWeek.Friday, 2)]);

        Assert.True(record.Vouches(PlacementKey.ForWorkout(Sam, SessionKind.Strength, Week, 0)));
        Assert.True(record.Vouches(PlacementKey.ForWorkout(Sam, SessionKind.Strength, Week, 2)));
        Assert.False(record.Vouches(PlacementKey.ForWorkout(Sam, SessionKind.Strength, Week, 1)));
        Assert.False(record.Vouches(PlacementKey.ForWorkout(Lee, SessionKind.Strength, Week, 0)));
        Assert.False(record.Vouches(PlacementKey.ForWorkout(Sam, SessionKind.Mobility, Week, 0)));
        Assert.False(record.Vouches(PlacementKey.ForHousehold(Sam, Week, "dinner-a")));
    }

    [Fact]
    public void TheHighestIsThePeakNotTheCount()
    {
        var record = KeyAssignments.Empty.With([Issue(DayOfWeek.Monday, 0), Issue(DayOfWeek.Friday, 2)]);

        Assert.Equal(2, record.HighestOrdinal(Sam, Week, SessionKind.Strength));
    }

    [Fact]
    public void TheHighestIsKeptPerPersonWeekAndKind()
    {
        var record = KeyAssignments.Empty.With(
        [
            Issue(DayOfWeek.Monday, 7),
            Issue(DayOfWeek.Monday, 3, person: Lee),
            Issue(DayOfWeek.Monday, 5, kind: SessionKind.Mobility),
            Issue(DayOfWeek.Monday, 9, week: Week + 1),
        ]);

        Assert.Equal(7, record.HighestOrdinal(Sam, Week, SessionKind.Strength));
        Assert.Equal(3, record.HighestOrdinal(Lee, Week, SessionKind.Strength));
        Assert.Equal(5, record.HighestOrdinal(Sam, Week, SessionKind.Mobility));
        Assert.Equal(9, record.HighestOrdinal(Sam, Week + 1, SessionKind.Strength));
        Assert.Null(record.HighestOrdinal(Lee, Week, SessionKind.Mobility));
    }

    [Fact]
    public void AnIdentityIsNeverGivenTwoNumbers()
    {
        var record = KeyAssignments.Empty.With([Issue(DayOfWeek.Monday, 0)]);

        var error = Assert.Throws<ArgumentException>(() => record.With([Issue(DayOfWeek.Monday, 1)]));
        Assert.Contains("already", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANumberIsNeverGivenToTwoSessions()
    {
        var record = KeyAssignments.Empty.With([Issue(DayOfWeek.Monday, 0)]);

        var error = Assert.Throws<ArgumentException>(() => record.With([Issue(DayOfWeek.Tuesday, 0)]));
        Assert.Contains("already", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameNumberOnTwoKindsIsTwoNumbers()
    {
        var record = KeyAssignments.Empty.With([Issue(DayOfWeek.Monday, 0), Issue(DayOfWeek.Tuesday, 0, kind: SessionKind.Mobility)]);

        Assert.Equal(2, record.Issues.Count);
    }

    [Fact]
    public void ARepeatedIssueIsNothingNew()
    {
        var record = KeyAssignments.Empty.With([Issue(DayOfWeek.Monday, 0)]);

        Assert.Same(record, record.With([Issue(DayOfWeek.Monday, 0)]));
        Assert.Same(record, record.With([]));
    }

    [Fact]
    public void WithLeavesTheOriginalAlone()
    {
        var before = KeyAssignments.Empty.With([Issue(DayOfWeek.Monday, 0)]);

        var after = before.With([Issue(DayOfWeek.Friday, 1)]);

        Assert.Single(before.Issues);
        Assert.Equal(2, after.Issues.Count);
        Assert.Empty(KeyAssignments.Empty.Issues);
    }

    [Fact]
    public void APersonIsKeptTrimmedAndLowerCase()
    {
        var issue = Issue(DayOfWeek.Monday, 0, person: "  SAM ");
        var record = KeyAssignments.Empty.With([issue]);

        Assert.Equal("sam", issue.Person);
        Assert.NotNull(record.Find("sAm", Week, SessionKind.Strength, DayOfWeek.Monday, 0));
        Assert.Equal(0, record.HighestOrdinal(" SAM", Week, SessionKind.Strength));
    }

    [Fact]
    public void AnIssueKnowsItsKey()
    {
        Assert.Equal(PlacementKey.ForWorkout(Sam, SessionKind.Mobility, Week, 3), Issue(DayOfWeek.Sunday, 3, kind: SessionKind.Mobility).Key);
    }

    [Theory]
    [InlineData("", 1, 0, 0)]
    [InlineData("sam", -1, 0, 0)]
    [InlineData("sam", 1, -1, 0)]
    [InlineData("sam", 1, 0, -1)]
    public void ARecordThatMakesNoSenseIsRefused(string person, int week, int slot, int ordinal)
    {
        Assert.ThrowsAny<ArgumentException>(() => new KeyIssue(person, week, SessionKind.Strength, DayOfWeek.Monday, slot, ordinal));
    }

    [Fact]
    public void AWeekdayThatIsNotOneIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeyIssue(Sam, Week, SessionKind.Strength, (DayOfWeek)9, 0, 0));
    }

    // ---- the text ----

    [Fact]
    public void TheTextIsFrozen()
    {
        var record = KeyAssignments.Empty.With([Issue(DayOfWeek.Wednesday, 1), Issue(DayOfWeek.Monday, 0, kind: SessionKind.GentleWalk, slot: 1)]);

        Assert.Equal(
            "{\"version\":1,\"issued\":["
            + "{\"person\":\"sam\",\"week\":105703,\"kind\":\"gentlewalk\",\"weekday\":\"mon\",\"slot\":1,\"ordinal\":0},"
            + "{\"person\":\"sam\",\"week\":105703,\"kind\":\"strength\",\"weekday\":\"wed\",\"slot\":0,\"ordinal\":1}]}",
            KeyAssignmentsCodec.Encode(record));
    }

    [Fact]
    public void EveryWeekdayHasAFrozenSpelling()
    {
        var spelled = Enum.GetValues<DayOfWeek>()
            .Select(day => KeyAssignmentsCodec.Encode(KeyAssignments.Empty.With([Issue(day, 0)])))
            .Select(text => System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("issued")[0].GetProperty("weekday").GetString())
            .ToList();

        Assert.Equal(["sun", "mon", "tue", "wed", "thu", "fri", "sat"], spelled);
    }

    [Fact]
    public void EverySessionKindHasAFrozenSpelling()
    {
        var spelled = Enum.GetValues<SessionKind>()
            .Select(kind => KeyAssignmentsCodec.Encode(KeyAssignments.Empty.With([Issue(DayOfWeek.Monday, 0, kind: kind)])))
            .Select(text => System.Text.Json.JsonDocument.Parse(text).RootElement.GetProperty("issued")[0].GetProperty("kind").GetString())
            .ToList();

        Assert.Equal(["strength", "outdoorcardio", "treadmill", "mobility", "gentlewalk"], spelled);
    }

    [Fact]
    public void TheRecordSurvivesItsOwnText()
    {
        var record = KeyAssignments.Empty.With(
        [
            Issue(DayOfWeek.Monday, 0), Issue(DayOfWeek.Friday, 2), Issue(DayOfWeek.Friday, 1, kind: SessionKind.Mobility),
            Issue(DayOfWeek.Monday, 4, person: Lee, week: Week + 1, slot: 1),
        ]);

        var read = KeyAssignmentsCodec.Decode(KeyAssignmentsCodec.Encode(record));

        Assert.True(read.IsReadable);
        Assert.Equal(record.Issues, read.Assignments.Issues);
    }

    [Fact]
    public void TheSameRecordIsTheSameTextWhateverTheOrder()
    {
        var one = KeyAssignments.Empty.With([Issue(DayOfWeek.Monday, 0), Issue(DayOfWeek.Friday, 2)]);
        var other = KeyAssignments.Empty.With([Issue(DayOfWeek.Friday, 2), Issue(DayOfWeek.Monday, 0)]);

        Assert.Equal(KeyAssignmentsCodec.Encode(one), KeyAssignmentsCodec.Encode(other));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingStoredIsAnEmptyRecord(string? stored)
    {
        var read = KeyAssignmentsCodec.Decode(stored);

        Assert.True(read.IsReadable);
        Assert.Empty(read.Assignments.Issues);
    }

    [Theory]
    [InlineData("{ not the record")]
    [InlineData("[]")]
    [InlineData("{\"issued\":[]}")]
    [InlineData("{\"version\":2,\"issued\":[]}")]
    [InlineData("{\"version\":\"1\",\"issued\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"issued\":{}}")]
    [InlineData("{\"version\":1,\"issued\":[7]}")]
    [InlineData("{\"version\":1,\"issued\":[{\"person\":\"sam\",\"week\":1,\"kind\":\"yoga\",\"weekday\":\"mon\",\"slot\":0,\"ordinal\":0}]}")]
    [InlineData("{\"version\":1,\"issued\":[{\"person\":\"sam\",\"week\":1,\"kind\":\"strength\",\"weekday\":\"Monday\",\"slot\":0,\"ordinal\":0}]}")]
    [InlineData("{\"version\":1,\"issued\":[{\"person\":\"sam\",\"week\":1,\"kind\":\"strength\",\"weekday\":\"mon\",\"slot\":0,\"ordinal\":-1}]}")]
    [InlineData("{\"version\":1,\"issued\":[{\"person\":\"sam\",\"week\":1,\"kind\":\"strength\",\"weekday\":\"mon\",\"slot\":0}]}")]
    [InlineData("{\"version\":1,\"issued\":[{\"person\":\"\",\"week\":1,\"kind\":\"strength\",\"weekday\":\"mon\",\"slot\":0,\"ordinal\":0}]}")]
    [InlineData("{\"version\":1,\"issued\":[{\"person\":\"sam\",\"week\":1,\"kind\":\"strength\",\"weekday\":\"mon\",\"slot\":0,\"ordinal\":0},{\"person\":\"sam\",\"week\":1,\"kind\":\"strength\",\"weekday\":\"tue\",\"slot\":0,\"ordinal\":0}]}")]
    public void AnythingElseIsUnreadableNotEmpty(string stored)
    {
        var read = KeyAssignmentsCodec.Decode(stored);

        Assert.False(read.IsReadable);
        Assert.False(string.IsNullOrWhiteSpace(read.UnreadableBecause));
        Assert.Empty(read.Assignments.Issues);
    }

    [Fact]
    public void AnUnreadableRecordHasToSayWhy()
    {
        Assert.Throws<ArgumentException>(() => KeyAssignmentsRead.Unreadable("  "));
        Assert.Throws<ArgumentNullException>(() => KeyAssignmentsRead.Of(null!));
    }
}
