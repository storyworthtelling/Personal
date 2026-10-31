using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;
using Constitution.Domain.Voice;
using Constitution.Tests.Body;

namespace Constitution.Tests.BodyScreens;

/// <summary>
/// C-7.3.6: the day after a device sat out, today's monthly sentence carries the reminder. It is
/// Body's monthly seat on that day, not a seventh seat (book page 18).
/// </summary>
public class DeviceReminderTests
{
    private static readonly DateOnly Today = ScreenData.Today;

    /// <summary>Resting heart rate (the watch's) read on each of the given days back from today.</summary>
    private static List<BodyReading> WatchOn(params int[] daysBack) =>
        daysBack.Select(back => new BodyReading(BodySignal.RestingHeartRate, Today.AddDays(-back), 59)).ToList();

    [Fact]
    public void OnlyTheDayAfter()
    {
        // Read the day before yesterday, nothing yesterday: today is the day after.
        Assert.Equal("watch", DeviceReminder.SkippedDevice(WatchOn(2, 3, 4), Today));

        // Read yesterday: nothing to remind.
        Assert.Null(DeviceReminder.SkippedDevice(WatchOn(1, 2, 3), Today));

        // Missing yesterday and the day before as well: the day after was yesterday, so it is not said again.
        Assert.Null(DeviceReminder.SkippedDevice(WatchOn(3, 4, 5), Today));
    }

    [Fact]
    public void ADeviceThatNeverReadHasNothingToRemindAbout()
    {
        Assert.Null(DeviceReminder.SkippedDevice([], Today));
    }

    [Fact]
    public void OnlyTheWatchsOwnSignalsCount()
    {
        // Weight comes from a scale, not the watch: a weigh-in on the day before yesterday proves nothing about the wrist.
        var scale = new[] { new BodyReading(BodySignal.Weight, Today.AddDays(-2), 180) };

        Assert.Null(DeviceReminder.SkippedDevice(scale, Today));
    }

    [Fact]
    public void NotASeventhSeat()
    {
        var model = BodyTabModel.Build(ScreenData.Inputs(WatchOn(2, 3, 4)));

        // The six seats are a closed list, and the reminder is not among them: it is the monthly seat's own request.
        Assert.Equal(6, Enum.GetValues<VoiceSeat>().Length);
        Assert.NotNull(model.Monthly);
        Assert.Equal(VoiceSeat.BodyMonthly, model.Monthly!.Seat);
        Assert.Equal("watch", model.SkippedDevice);
    }

    [Fact]
    public void TheNeutralSentenceIsPageEighteens()
    {
        var model = BodyTabModel.Build(ScreenData.Inputs(WatchOn(2, 3, 4)));

        Assert.Equal("No watch readings yesterday.", model.Monthly!.Fallback);
    }

    [Fact]
    public void TheReminderReplacesTheMonthForTheDay()
    {
        var readings = ScreenData.PageSixteen().Where(reading => reading.Signal != BodySignal.RestingHeartRate).ToList();
        readings.AddRange(WatchOn(2, 3, 4));

        var model = BodyTabModel.Build(ScreenData.Inputs(readings));

        Assert.Equal("No watch readings yesterday.", model.Monthly!.Fallback);
        Assert.DoesNotContain(model.Monthly.Facts, fact => fact.Contains("Sleep", StringComparison.Ordinal));
    }

    [Fact]
    public void NoReminderOnAnOrdinaryDay()
    {
        var model = BodyTabModel.Build(ScreenData.Inputs(ScreenData.PageSixteen().Concat(WatchOn(1, 0))));

        Assert.Null(model.SkippedDevice);
    }
}
