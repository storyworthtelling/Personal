namespace Storyworthtelling.Diagnostics.Tests;

// O12: weekly, just before the Sunday reader. The daily worker asks UploadWindow whether to act: yes from
// Saturday 18:00 on the phone's own clock until a confirmed upload lands that week.
public class UploadWindowTests
{
    private static readonly TimeSpan Local = TimeSpan.FromHours(-5);

    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, Local);

    [Fact]
    public void ClosedBeforeSaturdayEvening()
    {
        var lastWeek = At(10, 24, 18, 30);

        Assert.False(UploadWindow.Due(lastWeek, At(10, 31, 17, 59)));
        Assert.False(UploadWindow.Due(lastWeek, At(10, 29, 9)));
    }

    [Fact]
    public void OpensAtSaturdayEighteenHundredLocal()
    {
        var lastWeek = At(10, 24, 18, 30);

        Assert.True(UploadWindow.Due(lastWeek, At(10, 31, 18)));
        Assert.True(UploadWindow.Due(lastWeek, At(10, 31, 23, 15)));
    }

    [Fact]
    public void TheWindowIsTheOwnersLocalEveningNotUtc()
    {
        var lastWeek = At(10, 24, 18, 30);

        // 22:00 UTC on Saturday is 17:00 here: still closed.
        var saturdayAfternoonHere = new DateTimeOffset(2026, 10, 31, 22, 0, 0, TimeSpan.Zero).ToOffset(Local);

        Assert.False(UploadWindow.Due(lastWeek, saturdayAfternoonHere));
    }

    [Fact]
    public void OneConfirmedUploadClosesItForTheWeek()
    {
        var uploaded = At(10, 31, 18, 5);

        Assert.False(UploadWindow.Due(uploaded, At(10, 31, 21)));
        Assert.False(UploadWindow.Due(uploaded, At(11, 1, 2)));
        Assert.False(UploadWindow.Due(uploaded, At(11, 6, 12)));
    }

    [Fact]
    public void AMissedSaturdayCatchesUpAtTheFirstChance()
    {
        var lastWeek = At(10, 24, 18, 30);

        Assert.True(UploadWindow.Due(lastWeek, At(11, 1, 8)));   // Sunday morning
        Assert.True(UploadWindow.Due(lastWeek, At(11, 2, 12)));  // Monday
        Assert.True(UploadWindow.Due(lastWeek, At(11, 6, 12)));  // Friday
    }

    [Fact]
    public void ItReopensTheFollowingSaturday()
    {
        var uploaded = At(10, 31, 18, 5);

        Assert.True(UploadWindow.Due(uploaded, At(11, 7, 18)));
    }

    [Fact]
    public void ANeverUploadedPhoneIsDue()
    {
        Assert.True(UploadWindow.Due(null, At(10, 28, 12)));
    }

    [Fact]
    public void TheWindowStartIsTheMostRecentSaturdayEvening()
    {
        Assert.Equal(At(10, 24, 18), UploadWindow.Start(At(10, 31, 17, 59)));
        Assert.Equal(At(10, 31, 18), UploadWindow.Start(At(10, 31, 18)));
        Assert.Equal(At(10, 31, 18), UploadWindow.Start(At(11, 6, 23)));
    }
}
