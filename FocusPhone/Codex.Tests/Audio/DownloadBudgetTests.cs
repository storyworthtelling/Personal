using Codex.Domain.Audio;
using Xunit;

namespace Codex.Tests.Audio;

public class DownloadBudgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);

    private static DownloadedItem Item(
        string id, double hours, DownloadKind kind = DownloadKind.Music, double daysAgo = 0) =>
        new(id, $"Item {id}", kind, TimeSpan.FromHours(hours), Now.AddDays(-daysAgo));

    [Fact]
    public void Empty_HasTheWholeBudget()
    {
        Assert.Equal(DownloadBudget.Capacity, DownloadBudget.Empty.Remaining);
        Assert.Equal(TimeSpan.Zero, DownloadBudget.Empty.Used);
    }

    // One pool, not two. What is downloaded is playback time, whichever side of the
    // app it came from — the meter splits it for reading, never for accounting.
    [Fact]
    public void Used_CountsBothMediaAgainstOnePool()
    {
        var budget = DownloadBudget.Empty
            .With(Item("song", 1))
            .With(Item("episode", 2, DownloadKind.Podcast));

        Assert.Equal(TimeSpan.FromHours(3), budget.Used);
        Assert.Equal(TimeSpan.FromHours(1), budget.UsedBy(DownloadKind.Music));
        Assert.Equal(TimeSpan.FromHours(2), budget.UsedBy(DownloadKind.Podcast));
    }

    [Fact]
    public void With_DoesNotCountTheSameThingTwice()
    {
        var budget = DownloadBudget.Empty.With(Item("song", 1)).With(Item("song", 1));

        Assert.Equal(TimeSpan.FromHours(1), budget.Used);
        Assert.True(budget.Holds("song"));
    }

    [Fact]
    public void Without_GivesTheTimeBack()
    {
        var budget = DownloadBudget.Empty.With(Item("song", 1)).Without("song");

        Assert.Equal(TimeSpan.Zero, budget.Used);
        Assert.False(budget.Holds("song"));
    }

    [Fact]
    public void Fits_IsTrueWhileThereIsRoom()
    {
        var budget = DownloadBudget.Empty.With(Item("most", 11));

        Assert.True(budget.Fits(TimeSpan.FromMinutes(59)));
        Assert.True(budget.Fits(TimeSpan.FromHours(1)));
        Assert.False(budget.Fits(TimeSpan.FromMinutes(61)));
    }

    // Over budget asks each time, so the question has to name the cost.
    [Fact]
    public void Overage_SaysHowFarPastTheBudgetSomethingWouldPutYou()
    {
        var budget = DownloadBudget.Empty.With(Item("most", 11));

        Assert.Equal(TimeSpan.Zero, budget.Overage(TimeSpan.FromMinutes(30)));
        Assert.Equal(TimeSpan.FromMinutes(30), budget.Overage(TimeSpan.FromMinutes(90)));
    }

    // Already past it — the owner said yes to something once — and the next question
    // still has to name the true cost rather than starting the count again.
    [Fact]
    public void Overage_KeepsCountingOnceTheBudgetIsPast()
    {
        var budget = DownloadBudget.Empty.With(Item("said yes to this", 13));

        Assert.Equal(TimeSpan.FromHours(2), budget.Overage(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Remaining_NeverGoesNegative()
    {
        Assert.Equal(TimeSpan.Zero, DownloadBudget.Empty.With(Item("too much", 20)).Remaining);
    }

    // The clock runs from the download and never resets on play: the sweep is about
    // how long something has been taking up room, not about how recently it was
    // useful. Keeping a thing alive by playing it is how a 12-hour budget silently
    // becomes a library.
    [Fact]
    public void Expired_TakesWhatHasBeenHeldTooLong()
    {
        var budget = DownloadBudget.Empty
            .With(Item("old", 1, daysAgo: DownloadBudget.LifeDays + 1))
            .With(Item("today", 1));

        Assert.Equal(["old"], budget.Expired(Now).Select(item => item.MediaId));
    }

    [Fact]
    public void Expired_LeavesSomethingOnItsLastDay()
    {
        var budget = DownloadBudget.Empty.With(Item("just made it", 1, daysAgo: DownloadBudget.LifeDays - 0.1));

        Assert.Empty(budget.Expired(Now));
    }

    [Fact]
    public void Swept_RemovesEverythingExpired()
    {
        var budget = DownloadBudget.Empty
            .With(Item("old", 1, daysAgo: DownloadBudget.LifeDays + 1))
            .With(Item("today", 2));

        var swept = budget.Swept(Now);

        Assert.Equal(TimeSpan.FromHours(2), swept.Used);
        Assert.False(swept.Holds("old"));
    }

    [Fact]
    public void ExpiresIn_CountsTheDaysLeft()
    {
        var budget = DownloadBudget.Empty.With(Item("held", 1, daysAgo: 3));

        Assert.Equal(DownloadBudget.LifeDays - 3, budget.DaysLeft("held", Now));
    }

    [Fact]
    public void ExpiresIn_IsNothingForSomethingItDoesNotHold()
    {
        Assert.Null(DownloadBudget.Empty.DaysLeft("absent", Now));
    }
}
