using Constitution.Domain.Delivery;
using Constitution.Domain.ReadBack;
using static Constitution.Tests.ReadBack.ReadBackFixture;

namespace Constitution.Tests.ReadBack;

/// <summary>
/// A page recorded for today (or later) has not been done yet. Reading it at 7:15 finds a blank, and once pen ink has
/// been proven that blank settles a skip for a session that has not happened. The service itself must therefore leave
/// any document whose day is not yet over alone, whatever store it is handed.
/// </summary>
public class ReadBackTodayTests
{
    private static readonly DateOnly MorningDay = DateOnly.FromDateTime(Morning.DateTime);

    private static Rig Proven(params ExpectedDocument[] documents)
    {
        var rig = new Rig(true, documents);
        rig.Vision.Reply = document => Read(document, PenStrokes.None);
        return rig;
    }

    [Fact]
    public async Task ASameDayPageIsNotReadOrSettled()
    {
        var today = Workout(MorningDay);
        var rig = Proven(today);

        var report = await rig.Service.Run();

        Assert.Empty(report.Documents);
        Assert.Equal(0, rig.Tablet.Downloads);
        Assert.Equal(0, rig.Vision.Reads);
        Assert.Equal(0, rig.Ledger.Saves);
        Assert.Empty(rig.Tablet.Trashed);
        Assert.Empty(rig.Expected.Retired);
    }

    [Fact]
    public async Task AFutureDayPageIsNotReadEither()
    {
        var rig = Proven(Workout(MorningDay.AddDays(2)));

        var report = await rig.Service.Run();

        Assert.Empty(report.Documents);
        Assert.Equal(0, rig.Vision.Reads);
    }

    [Fact]
    public async Task YesterdaysPageIsStillRead()
    {
        var rig = Proven(Workout(MorningDay.AddDays(-1)));

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(outcome);
        Assert.Equal(1, rig.Vision.Reads);
    }

    [Fact]
    public async Task OnlyAnEndedDayIsReadFromAMixedList()
    {
        var yesterday = Workout(MorningDay.AddDays(-1));
        var rig = Proven(Workout(MorningDay), yesterday);

        var report = await rig.Service.Run();

        Assert.Equal(yesterday.Name, Assert.Single(report.Documents).Name);
        Assert.Equal(1, rig.Vision.Reads);
    }

    [Fact]
    public async Task ASavedResultOfTodayIsNotFinishedEarly()
    {
        var rig = Proven(Workout(MorningDay));
        rig.Expected.MarkSaved(Workout(MorningDay).Name);

        var report = await rig.Service.Run();

        Assert.Empty(report.Documents);
        Assert.Empty(rig.Tablet.Trashed);
    }

    [Fact]
    public async Task TheDayTurnsOverOnTheOwnersClockNotUtc()
    {
        // 9 pm on 4 November at five hours behind UTC is already 5 November in UTC: to the owner the 4th is not over.
        var rig = Proven(Workout(MorningDay));
        rig.Now = new DateTimeOffset(2026, 11, 4, 21, 0, 0, TimeSpan.FromHours(-5));

        var report = await rig.Service.Run();

        Assert.Empty(report.Documents);
        Assert.Equal(0, rig.Vision.Reads);
    }

    [Fact]
    public async Task OneMinuteAfterTheirMidnightThePageIsDue()
    {
        var rig = Proven(Workout(MorningDay));
        rig.Now = new DateTimeOffset(2026, 11, 5, 0, 1, 0, TimeSpan.FromHours(-5));

        var outcome = await rig.RunOne();

        Assert.IsType<DocumentReadBack.Settled>(outcome);
    }

    [Fact]
    public async Task OneMinuteBeforeTheirMidnightItIsNot()
    {
        var rig = Proven(Workout(MorningDay));
        rig.Now = new DateTimeOffset(2026, 11, 4, 23, 59, 0, TimeSpan.FromHours(-5));

        var report = await rig.Service.Run();

        Assert.Empty(report.Documents);
    }

    [Theory]
    [InlineData(DeliveryKind.Workout, -1, true)]
    [InlineData(DeliveryKind.Workout, 0, false)]
    [InlineData(DeliveryKind.Workout, 1, false)]
    [InlineData(DeliveryKind.Meals, 0, true)]
    [InlineData(DeliveryKind.Digest, 0, true)]
    public void OnlyAWorkoutWaitsForItsDayToEnd(DeliveryKind kind, int daysFromToday, bool due)
    {
        Assert.Equal(due, ReadBackPolicy.IsDue(kind, MorningDay.AddDays(daysFromToday), MorningDay));
    }
}
