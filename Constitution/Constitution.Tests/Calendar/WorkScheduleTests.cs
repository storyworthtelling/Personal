using Constitution.Domain.Calendar;
using static Constitution.Tests.Calendar.InventedFamily;

namespace Constitution.Tests.Calendar;

/// <summary>
/// Reading the work schedule Foreword keeps. The personal vault carries it as the JSON Foreword's
/// calendar screens save (<c>PersonalVault.WorkScheduleJson</c>); this reads that shape without
/// depending on Foreword. The danger is silent: a document that parses but is not the schedule
/// would read as "nobody works", and a workout would go over somebody's day. So anything that is
/// not recognisably the schedule is reported as unknown, with a reason, never as an empty one.
/// </summary>
public class WorkScheduleTests
{
    [Fact]
    public void ReadsTheFileForewordWrites()
    {
        var schedule = WorkSchedule.Read(ForewordSchedule);

        Assert.True(schedule.IsKnown);
        Assert.Null(schedule.Problem);
        Assert.Equal(
            [
                new WorkShift(Sam, new TimeOnly(8, 0), new TimeOnly(16, 0)),
                new WorkShift(Lee, new TimeOnly(8, 0), new TimeOnly(15, 30)),
            ],
            schedule.Shifts);
        Assert.Equal(["Off", "PTO", "Vacation"], schedule.OffKeywords);
    }

    [Fact]
    public void ReadsForewordsOwnOutputExactly()
    {
        // Pasted from a run of Foreword's own CalendarMappingCodec.Write over a WorkScheduleOptions
        // holding two working shifts and a third switched off (a scratch program referencing
        // Foreword.Infrastructure, not committed). Nothing here depends on Foreword; if Foreword's
        // file ever changes shape, this string is the thing to update, deliberately.
        const string written =
            "{\"Shifts\":[{\"Person\":\"Sam\",\"Start\":\"08:00\",\"End\":\"16:00\",\"Title\":\"Work\",\"Enabled\":true}," +
            "{\"Person\":\"Lee\",\"Start\":\"08:00\",\"End\":\"15:30\",\"Title\":\"Work\",\"Enabled\":true}," +
            "{\"Person\":\"Lee\",\"Start\":\"17:00\",\"End\":\"19:00\",\"Title\":\"Work\",\"Enabled\":false}]," +
            "\"OffKeywords\":[\"Off\",\"PTO\",\"Vacation\"]}";

        var schedule = WorkSchedule.Read(written);

        Assert.True(schedule.IsKnown);
        Assert.Equal(
            [
                new WorkShift(Sam, new TimeOnly(8, 0), new TimeOnly(16, 0)),
                new WorkShift(Lee, new TimeOnly(8, 0), new TimeOnly(15, 30)),
            ],
            schedule.Shifts);
    }

    [Fact]
    public void PropertyNamesAreReadWithoutCase()
    {
        // Foreword reads its own file case-insensitively; so does this.
        var schedule = WorkSchedule.Read("{\"shifts\":[{\"person\":\"Sam\",\"start\":\"09:15\",\"end\":\"17:45\"}],\"offkeywords\":[\"Leave\"]}");

        Assert.True(schedule.IsKnown);
        Assert.Equal([new WorkShift(Sam, new TimeOnly(9, 15), new TimeOnly(17, 45))], schedule.Shifts);
        Assert.Equal(["Leave"], schedule.OffKeywords);
    }

    [Fact]
    public void AShiftTurnedOffIsKeptOutOfTheHours()
    {
        // Foreword keeps a shift it has switched off so the hours need not be typed again; it draws
        // no block for it, and neither does this.
        var json = "{\"Shifts\":[{\"Person\":\"Sam\",\"Start\":\"08:00\",\"End\":\"16:00\",\"Enabled\":false}]}";

        var schedule = WorkSchedule.Read(json);

        Assert.True(schedule.IsKnown);
        Assert.Empty(schedule.Shifts);
    }

    [Fact]
    public void ADisabledShiftNeedNotBeReadable()
    {
        var json = "{\"Shifts\":[{\"Person\":\"Sam\",\"Start\":\"soon\",\"End\":\"later\",\"Enabled\":false}]}";

        Assert.True(WorkSchedule.Read(json).IsKnown);
    }

    [Fact]
    public void MissingOffKeywordsTakeForewordsDefaults()
    {
        var schedule = WorkSchedule.Read("{\"Shifts\":[]}");

        Assert.True(schedule.IsKnown);
        Assert.Equal(["Off", "PTO", "Vacation"], schedule.OffKeywords);
    }

    [Fact]
    public void AnEmptyKeywordListMeansNoOffDayWords()
    {
        var schedule = WorkSchedule.Read("{\"Shifts\":[],\"OffKeywords\":[]}");

        Assert.True(schedule.IsKnown);
        Assert.Empty(schedule.OffKeywords);
    }

    [Fact]
    public void ASavedEmptyScheduleIsKnownAndEmpty()
    {
        var schedule = WorkSchedule.Read("{\"Shifts\":[]}");

        Assert.True(schedule.IsKnown);
        Assert.Empty(schedule.Shifts);
    }

    [Fact]
    public void ASplitShiftIsTwoBlocksForOnePerson()
    {
        var json = "{\"Shifts\":[" +
            "{\"Person\":\"Lee\",\"Start\":\"07:00\",\"End\":\"11:00\"}," +
            "{\"Person\":\"lee\",\"Start\":\"15:00\",\"End\":\"19:00\"}]}";

        var schedule = WorkSchedule.Read(json);

        Assert.Equal(2, schedule.ShiftsFor(Lee).Count);
        Assert.Empty(schedule.ShiftsFor(Sam));
    }

    [Fact]
    public void ShiftsForAPersonIgnoreCase()
    {
        Assert.Single(WorkSchedule.Read(ForewordSchedule).ShiftsFor("SAM"));
    }

    [Fact]
    public void AnOvernightShiftIsAllowed()
    {
        var schedule = WorkSchedule.Read("{\"Shifts\":[{\"Person\":\"Sam\",\"Start\":\"22:00\",\"End\":\"06:00\"}]}");

        Assert.True(schedule.IsKnown);
        Assert.Equal([new WorkShift(Sam, new TimeOnly(22, 0), new TimeOnly(6, 0))], schedule.Shifts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingSavedIsUnknownNotEmpty(string? json)
    {
        var schedule = WorkSchedule.Read(json);

        Assert.False(schedule.IsKnown);
        Assert.Contains("saved", schedule.Problem);
    }

    [Fact]
    public void GarbledJsonIsUnknownAndSaysWhy()
    {
        var schedule = WorkSchedule.Read("{\"Shifts\": [");

        Assert.False(schedule.IsKnown);
        Assert.Contains("not valid JSON", schedule.Problem);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"Shifts\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void AnythingButAnObjectIsUnknown(string json)
    {
        Assert.False(WorkSchedule.Read(json).IsKnown);
    }

    [Fact]
    public void ADocumentWithoutShiftsIsUnknownNotEmpty()
    {
        // Valid JSON that is not the schedule. Reading it as "no shifts" would silently free every
        // working day, which is the whole failure this guards.
        var schedule = WorkSchedule.Read("{\"blocks\":[\"example-work-marker\"]}");

        Assert.False(schedule.IsKnown);
        Assert.Contains("Shifts", schedule.Problem);
    }

    [Fact]
    public void ShiftsThatAreNotAListAreUnknown()
    {
        var schedule = WorkSchedule.Read("{\"Shifts\":{\"Person\":\"Sam\"}}");

        Assert.False(schedule.IsKnown);
        Assert.Contains("Shifts", schedule.Problem);
    }

    [Theory]
    [InlineData("{\"Shifts\":[{\"Person\":\"Sam\",\"Start\":\"8am\",\"End\":\"16:00\"}]}", "Start")]
    [InlineData("{\"Shifts\":[{\"Person\":\"Sam\",\"Start\":\"08:00\",\"End\":\"25:00\"}]}", "End")]
    [InlineData("{\"Shifts\":[{\"Person\":\"Sam\",\"End\":\"16:00\"}]}", "Start")]
    [InlineData("{\"Shifts\":[{\"Person\":\"Sam\",\"Start\":8,\"End\":\"16:00\"}]}", "Start")]
    [InlineData("{\"Shifts\":[{\"Start\":\"08:00\",\"End\":\"16:00\"}]}", "Person")]
    [InlineData("{\"Shifts\":[{\"Person\":\"  \",\"Start\":\"08:00\",\"End\":\"16:00\"}]}", "Person")]
    [InlineData("{\"Shifts\":[\"Sam\"]}", "Shifts")]
    public void AnUnreadableShiftMakesItUnknown(string json, string namesTheField)
    {
        // One shift dropped quietly would be one person's working day freed. Unknown means the
        // caller assumes hours for everyone and says so, which is the safe direction.
        var schedule = WorkSchedule.Read(json);

        Assert.False(schedule.IsKnown);
        Assert.Contains(namesTheField, schedule.Problem);
    }

    [Fact]
    public void AShiftThatStartsWhereItEndsIsUnknown()
    {
        var schedule = WorkSchedule.Read("{\"Shifts\":[{\"Person\":\"Sam\",\"Start\":\"08:00\",\"End\":\"08:00\"}]}");

        Assert.False(schedule.IsKnown);
        Assert.Contains("same", schedule.Problem);
    }

    [Fact]
    public void ANonBooleanEnabledIsUnknown()
    {
        var schedule = WorkSchedule.Read("{\"Shifts\":[{\"Person\":\"Sam\",\"Start\":\"08:00\",\"End\":\"16:00\",\"Enabled\":\"yes\"}]}");

        Assert.False(schedule.IsKnown);
        Assert.Contains("Enabled", schedule.Problem);
    }

    [Fact]
    public void OffKeywordsNotAListAreUnknown()
    {
        var schedule = WorkSchedule.Read("{\"Shifts\":[],\"OffKeywords\":\"Off\"}");

        Assert.False(schedule.IsKnown);
        Assert.Contains("OffKeywords", schedule.Problem);
    }

    [Fact]
    public void BlankOffKeywordsAreDropped()
    {
        var schedule = WorkSchedule.Read("{\"Shifts\":[],\"OffKeywords\":[\"PTO\",\"\",\"  \"]}");

        Assert.Equal(["PTO"], schedule.OffKeywords);
    }

    [Fact]
    public void ExtraPropertiesAreIgnored()
    {
        var schedule = WorkSchedule.Read("{\"Shifts\":[{\"Person\":\"Sam\",\"Start\":\"08:00\",\"End\":\"16:00\",\"Title\":\"Work\",\"Colour\":\"x\"}],\"Future\":1}");

        Assert.True(schedule.IsKnown);
        Assert.Single(schedule.Shifts);
    }

    [Fact]
    public void ProblemsNeverEchoTheDocument()
    {
        // The schedule is a personal fact; an error message that is shown or logged must not carry it.
        var schedule = WorkSchedule.Read("{\"Shifts\":[{\"Person\":\"Sam\",\"Start\":\"example-secret-hours\",\"End\":\"16:00\"}]}");

        Assert.False(schedule.IsKnown);
        Assert.DoesNotContain("example-secret-hours", schedule.Problem);
        Assert.DoesNotContain("Sam", schedule.Problem);
    }

    [Fact]
    public void OfBuildsAKnownSchedule()
    {
        var schedule = WorkSchedule.Of([new WorkShift(Sam, new TimeOnly(9, 0), new TimeOnly(17, 0))]);

        Assert.True(schedule.IsKnown);
        Assert.Equal(["Off", "PTO", "Vacation"], schedule.OffKeywords);
    }

    [Fact]
    public void UnrecognisedNeedsAReason()
    {
        Assert.Throws<ArgumentException>(() => WorkSchedule.Unrecognised(" "));
    }

    [Fact]
    public void BuildingAZeroLengthShiftIsRefused()
    {
        Assert.Throws<ArgumentException>(() => WorkSchedule.Of([new WorkShift(Sam, new TimeOnly(9, 0), new TimeOnly(9, 0))]));
    }

    [Fact]
    public void AShiftWithNoPersonIsRefusedWhenBuilt()
    {
        Assert.Throws<ArgumentException>(() => WorkSchedule.Of([new WorkShift(" ", new TimeOnly(9, 0), new TimeOnly(17, 0))]));
    }
}
