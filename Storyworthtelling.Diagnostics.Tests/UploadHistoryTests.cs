namespace Storyworthtelling.Diagnostics.Tests;

// What the weekly worker remembers between runs (plan §3.3): when an upload was last confirmed, which
// windows failed, and so when "three weeks in a row" has come. Weeks are counted by window, not by run,
// so seven failed daily runs in one week are one failed week.
public class UploadHistoryTests
{
    private static readonly TimeSpan Local = TimeSpan.FromHours(-5);

    private static DateTimeOffset Saturday(int week, int hour = 19) =>
        new DateTimeOffset(2026, 10, 3, hour, 0, 0, Local).AddDays(7 * week);

    [Fact]
    public void AConfirmedRunRecordsTheTimeAndClearsTheFailures()
    {
        var history = UploadHistory.None.Failed(Saturday(0)).Failed(Saturday(1)).Confirmed(Saturday(2));

        Assert.Equal(Saturday(2), history.LastConfirmed);
        Assert.Equal(0, history.FailedWeeks);
    }

    [Fact]
    public void ManyFailedRunsInOneWeekAreOneFailedWeek()
    {
        var history = UploadHistory.None;
        for (var day = 0; day < 7; day++)
        {
            history = history.Failed(Saturday(0).AddDays(day));
        }

        Assert.Equal(1, history.FailedWeeks);
    }

    [Fact]
    public void ThreeWeeksOfFailuresInARowAreThree()
    {
        var history = UploadHistory.None.Failed(Saturday(0)).Failed(Saturday(1)).Failed(Saturday(2).AddDays(1));

        Assert.Equal(3, history.FailedWeeks);
        Assert.Equal("Logs have not reached Drive for 3 weeks.", LogUploadStatus.Sentence(null, history.FailedWeeks));
    }

    [Fact]
    public void ItRoundTripsThroughItsStoredForm()
    {
        var history = UploadHistory.None.Failed(Saturday(0)).Confirmed(Saturday(1)).Failed(Saturday(2));

        Assert.Equal(history, UploadHistory.Parse(history.Format()));
        Assert.Equal(UploadHistory.None, UploadHistory.Parse(null));
        Assert.Equal(UploadHistory.None, UploadHistory.Parse("garbled"));
    }
}
