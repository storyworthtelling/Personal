using Watch.Domain.Updates;

namespace Watch.Tests.Updates;

/// <summary>
/// P14 release A fix round, finding 4: Flyleaf's watch half ran under its own five-minute budget measured from when it
/// began, after the phone's installs had already used some of WorkManager's ten minutes, and its Data Layer calls had no
/// wait at all, so one that never answered held the watch's gate for good. Now one deadline, measured from the start of
/// the worker's <c>DoWork</c>, is handed down: nothing starts past it and every wait is cut to what is left.
/// </summary>
public class RunDeadlineTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheDeadlineIsTheWorkersCeilingLessAMargin()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), RunDeadline.WorkerCeiling);
        Assert.Equal(TimeSpan.FromMinutes(1), RunDeadline.Margin);
        Assert.Equal(Start.AddMinutes(9), RunDeadline.From(Start).Ends);
    }

    [Fact]
    public void WhatIsLeftRunsDownAndNeverBelowNothing()
    {
        var deadline = RunDeadline.From(Start);

        Assert.Equal(TimeSpan.FromMinutes(9), deadline.Left(Start));
        Assert.Equal(TimeSpan.FromMinutes(6), deadline.Left(Start.AddMinutes(3)));
        Assert.Equal(TimeSpan.Zero, deadline.Left(Start.AddMinutes(9)));
        Assert.Equal(TimeSpan.Zero, deadline.Left(Start.AddMinutes(40)));
    }

    [Fact]
    public void NothingStartsPastIt()
    {
        var deadline = RunDeadline.From(Start);

        Assert.False(deadline.Passed(Start.AddMinutes(8).AddSeconds(59)));
        Assert.True(deadline.Passed(Start.AddMinutes(9)));
        Assert.True(deadline.Passed(Start.AddHours(2)));

        Assert.True(deadline.Allows(Start.AddMinutes(8).AddSeconds(59), TimeSpan.Zero));
        Assert.False(deadline.Allows(Start.AddMinutes(9), TimeSpan.Zero));
        Assert.False(deadline.Allows(Start.AddMinutes(12), TimeSpan.Zero));
    }

    [Fact]
    public void ASendNeedsRoomToStart()
    {
        var deadline = RunDeadline.From(Start);

        var latest = deadline.Ends - WatchSendPlan.SendNeeds;

        Assert.True(deadline.Allows(Start, WatchSendPlan.SendNeeds));
        Assert.True(deadline.Allows(latest, WatchSendPlan.SendNeeds));
        Assert.False(deadline.Allows(latest.AddSeconds(1), WatchSendPlan.SendNeeds));
        Assert.False(deadline.Allows(Start.AddMinutes(8), WatchSendPlan.SendNeeds));
    }

    [Fact]
    public void AWaitIsCutToWhatIsLeft()
    {
        var deadline = RunDeadline.From(Start);

        Assert.Equal(TimeSpan.FromSeconds(30), deadline.Cut(Start.AddMinutes(1), TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.FromSeconds(10), deadline.Cut(Start.AddMinutes(8).AddSeconds(50), TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.Zero, deadline.Cut(Start.AddMinutes(9), TimeSpan.FromSeconds(30)));
        Assert.Equal(TimeSpan.Zero, deadline.Cut(Start.AddMinutes(30), WatchSendPlan.ReportWait));
    }

    [Fact]
    public void TheOwnersRunHasTheSameDeadlineFromTheTap()
    {
        // "Install from GitHub now" is not a worker, but the same rule keeps its watch half from holding the gate the next
        // six-hourly run will want.
        var tapped = Start.AddHours(3);

        Assert.Equal(tapped + RunDeadline.WorkerCeiling - RunDeadline.Margin, RunDeadline.From(tapped).Ends);
    }

    [Fact]
    public void EveryWaitFitsInsideOneRun()
    {
        var run = RunDeadline.WorkerCeiling - RunDeadline.Margin;

        Assert.All(
            [WatchSendPlan.AnswerWait, WatchSendPlan.DataLayerWait, WatchSendPlan.CloseWait, WatchSendPlan.ReportWait, WatchSendPlan.SendNeeds],
            wait => Assert.InRange(wait, TimeSpan.FromSeconds(1), run));
    }
}
