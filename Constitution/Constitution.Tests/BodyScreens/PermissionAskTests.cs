using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;
using Constitution.Domain.Health;
using Constitution.Domain.Notices;

namespace Constitution.Tests.BodyScreens;

/// <summary>
/// C-7.3.5: Health Connect is asked per signal, the first time its chart is opened, never all up
/// front. The chart says what it needs and that it only reads, the bar carries the action, and a
/// permission taken away later is a Problem notice while that chart waits.
/// </summary>
public class PermissionAskTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AsksOnFirstOpenOnly()
    {
        var first = PermissionAsk.For(BodySignal.HeartRateVariability, SignalChoice.NotAsked, grantedNow: false);
        Assert.Equal(AskKind.FirstOpen, first.Kind);
        Assert.Equal("Asked now because this is the first time the chart was opened.", first.Note);

        // Asked once and turned down: the chart still cannot draw, but it no longer claims to be asking now.
        var later = PermissionAsk.For(BodySignal.HeartRateVariability, SignalChoice.Asked, grantedNow: false);
        Assert.Equal(AskKind.Declined, later.Kind);
        Assert.NotEqual(first.Note, later.Note);
        Assert.DoesNotContain("first time", later.Note ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void ASignalThatIsAllowedIsNeverAsked()
    {
        foreach (var choice in new[] { SignalChoice.NotAsked, SignalChoice.Asked, SignalChoice.Granted })
        {
            Assert.Equal(AskKind.None, PermissionAsk.For(BodySignal.Sleep, choice, grantedNow: true).Kind);
        }
    }

    [Fact]
    public void AWorkedOutSignalHasNothingToAskFor()
    {
        Assert.Equal(AskKind.None, PermissionAsk.For(BodySignal.Strength, SignalChoice.NotAsked, grantedNow: false).Kind);
        Assert.Equal(AskKind.None, PermissionAsk.For(BodySignal.Calories, SignalChoice.NotAsked, grantedNow: false).Kind);
    }

    [Fact]
    public void TheAskSaysWhatItNeedsAndThatItOnlyReads()
    {
        var ask = PermissionAsk.For(BodySignal.HeartRateVariability, SignalChoice.NotAsked, grantedNow: false);

        Assert.Equal(
            "Constitution needs to read heart rate variability from Health Connect. It reads only; it never writes.",
            ask.Need);
    }

    [Fact]
    public void TheBarCarriesAllowInHealthConnect()
    {
        Assert.Equal("Allow in Health Connect", PermissionAsk.AllowLabel);
        Assert.Equal(PermissionAsk.AllowLabel, PermissionAsk.For(BodySignal.Steps, SignalChoice.NotAsked, false).BarLabel);
        Assert.Equal(PermissionAsk.AllowLabel, PermissionAsk.For(BodySignal.Steps, SignalChoice.Asked, false).BarLabel);
        Assert.Null(PermissionAsk.For(BodySignal.Steps, SignalChoice.Granted, true).BarLabel);
    }

    [Fact]
    public void ATakenAwayPermissionIsRevoked()
    {
        var ask = PermissionAsk.For(BodySignal.Steps, SignalChoice.Granted, grantedNow: false);

        Assert.Equal(AskKind.Revoked, ask.Kind);
        Assert.Equal(PermissionAsk.AllowLabel, ask.BarLabel);
    }

    [Fact]
    public void ARevokedNoticeStillWaitingKeepsTheChartWaiting()
    {
        // The open-time reconcile will already have demoted Granted to Asked; the notice is what remembers.
        var ask = PermissionAsk.For(BodySignal.Steps, SignalChoice.Asked, grantedNow: false, revokedNoticeWaiting: true);

        Assert.Equal(AskKind.Revoked, ask.Kind);
    }

    [Fact]
    public void RevokedBecomesAProblemNotice()
    {
        var choices = HealthChoices.Empty
            .With(BodySignal.Steps, SignalChoice.Granted)
            .With(BodySignal.Sleep, SignalChoice.Granted)
            .With(BodySignal.Weight, SignalChoice.Asked);

        var revoked = PermissionAsk.RevokedSignals(choices, signal => signal == BodySignal.Sleep);

        // Steps was allowed and no longer is; weight was only ever asked, so nothing was taken from it.
        Assert.Equal([BodySignal.Steps], revoked);

        var board = new NoticeBoard();
        Assert.True(PermissionAsk.Apply(board, revoked, Now));

        var notice = Assert.Single(board.Waiting());
        Assert.Equal(NoticeKind.HealthConnectRevoked, notice.Kind);
        Assert.Equal(Codex.Domain.Design.FoldRole.Problem, notice.Role);
    }

    [Fact]
    public void SeveralRevokedSignalsAreOneNotice()
    {
        var board = new NoticeBoard();
        PermissionAsk.Apply(board, [BodySignal.Steps, BodySignal.Sleep], Now);
        PermissionAsk.Apply(board, [BodySignal.Steps, BodySignal.Sleep], Now.AddMinutes(1));

        Assert.Single(board.Waiting());
    }

    [Fact]
    public void TheNoticeGoesWhenTheCauseDoes()
    {
        var board = new NoticeBoard();
        PermissionAsk.Apply(board, [BodySignal.Steps], Now);

        Assert.True(PermissionAsk.Apply(board, [], Now.AddMinutes(1)));
        Assert.Empty(board.Waiting());
        Assert.False(PermissionAsk.Apply(board, [], Now.AddMinutes(2)));
    }

    [Fact]
    public void TheNoticeNeverCarriesAReading()
    {
        var board = new NoticeBoard();
        PermissionAsk.Apply(board, [BodySignal.Weight], Now);

        // A notice is a kind and a cause key; the cause names no signal and no value.
        var notice = Assert.Single(board.Waiting());
        Assert.DoesNotContain("weight", notice.CauseKey, StringComparison.OrdinalIgnoreCase);
    }
}
