using Constitution.Domain.Body;
using Constitution.Domain.Body.Screens;
using Constitution.Domain.Credentials;
using Constitution.Domain.Notices;
using Constitution.Tests.Body;

namespace Constitution.Tests.BodyScreens;

/// <summary>
/// C-7.2.2, book page 16 (fourth phone): with no bed reading since a date, the sleep line stops at the
/// last reading, the chart says "Waiting for the bed", and the Body sentence says so calmly. The error
/// itself waits behind a Problem notice and is never voiced.
/// </summary>
public class BedOfflineTests
{
    private static readonly DateOnly Today = ScreenData.Today;

    // Today is Tuesday 17 November, so a sleep reading dated Friday the 13th is Thursday night's.
    private static readonly DateOnly LastReading = new(2026, 11, 13);

    /// <summary>Sleep read every day up to <see cref="LastReading"/>, the month before it too.</summary>
    private static List<BodyReading> SleepThenSilence()
    {
        var readings = BodyData.Daily(BodySignal.Sleep, LastReading, 30, 6.2).ToList();
        readings.AddRange(BodyData.Daily(BodySignal.Sleep, LastReading.AddDays(-30), 30, 6.87));
        return readings;
    }

    [Fact]
    public void LineStopsAtTheLastReading()
    {
        var model = BodyTabModel.Build(ScreenData.Inputs(SleepThenSilence(), bedChosen: true));
        var card = ScreenData.CardOf(model, BodySignal.Sleep);

        Assert.Equal(SignalCardState.WaitingForBed, card.State);
        Assert.Equal("Waiting for the bed", card.StatusLine);
        Assert.Equal("Last reading Thursday night", card.ChangeText);

        // The geometry the painter draws from stops short of today: the days after are empty space.
        var geometry = ChartGeometry.Compute(card.Readings, model.Window, model.Today, 290, 70);
        Assert.Equal(LastReading, geometry.LatestDay);
        Assert.True(geometry.Dot!.Value.X < 290 - 1, "The line should stop before the right edge.");
        Assert.All(geometry.Lines.SelectMany(line => line), point => Assert.True(point.X <= geometry.Dot.Value.X));
    }

    [Fact]
    public void AnOfflineBedIsOnlyJudgedWhenOneIsChosen()
    {
        var model = BodyTabModel.Build(ScreenData.Inputs(SleepThenSilence(), bedChosen: false));

        // No bed is in use: a quiet sleep series is just a watch left on the dresser, not a bed problem.
        Assert.Equal(SignalCardState.Charted, ScreenData.CardOf(model, BodySignal.Sleep).State);
        Assert.False(model.Bed.IsOffline);
    }

    [Fact]
    public void AFreshNightMeansTheBedIsFine()
    {
        var readings = BodyData.Daily(BodySignal.Sleep, Today.AddDays(-1), 40, 6.5).ToList();

        Assert.False(BedOffline.Judge(readings, bedChosen: true, Today).IsOffline);
    }

    [Fact]
    public void ABedThatNeverReportedIsNotOfflineYet()
    {
        Assert.False(BedOffline.Judge([], bedChosen: true, Today).IsOffline);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(5, true)]
    public void TwoSilentDaysMakeItOffline(int daysSinceLastReading, bool offline)
    {
        var readings = BodyData.Daily(BodySignal.Sleep, Today.AddDays(-daysSinceLastReading), 5, 6.5).ToList();

        Assert.Equal(offline, BedOffline.Judge(readings, bedChosen: true, Today).IsOffline);
    }

    [Fact]
    public void TheBodySentenceSaysSoCalmly()
    {
        var readings = SleepThenSilence();
        var model = BodyTabModel.Build(ScreenData.Inputs(readings, bedChosen: true));

        Assert.Contains("The bed hasn’t reported since Thursday, so that line waits.", model.Monthly!.Fallback, StringComparison.Ordinal);
        Assert.Contains(model.Monthly.Facts, fact => fact.Contains("Thursday", StringComparison.Ordinal));
    }

    [Fact]
    public void ErrorIsNotVoiced()
    {
        var model = BodyTabModel.Build(ScreenData.Inputs(SleepThenSilence(), bedChosen: true));
        var spoken = model.Monthly!.Facts.Append(model.Monthly.Fallback);

        // Whatever the voice is handed is calm fact about a line waiting; nothing in it is a failure.
        string[] errorWords = ["error", "failed", "failure", "refused", "rejected", "sign-in", "sign in", "login", "offline", "couldn"];
        foreach (var text in spoken)
        {
            foreach (var word in errorWords)
            {
                Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void TheProblemNoticeAndNothingElseCarriesTheError()
    {
        var board = new NoticeBoard();
        var state = BedOffline.Judge(SleepThenSilence(), bedChosen: true, Today);

        Assert.True(BedOffline.Apply(board, state, credentialRejected: false, DateTimeOffset.UnixEpoch));

        var notice = Assert.Single(board.Waiting());
        Assert.Equal(NoticeKind.BedOffline, notice.Kind);
        Assert.Equal(Codex.Domain.Design.FoldRole.Problem, notice.Role);
        // The same cause the Settings credential check uses, so the two never stack as two cards.
        Assert.Equal(ConnectionNotices.CauseKey(CredentialIds.SleepNumber), notice.CauseKey);
    }

    [Fact]
    public void TheNoticeGoesWhenTheBedReportsAgain()
    {
        var board = new NoticeBoard();
        BedOffline.Apply(board, BedOffline.Judge(SleepThenSilence(), true, Today), false, DateTimeOffset.UnixEpoch);

        var fresh = BedOffline.Judge(BodyData.Daily(BodySignal.Sleep, Today, 5, 6.5).ToList(), true, Today);
        Assert.True(BedOffline.Apply(board, fresh, credentialRejected: false, DateTimeOffset.UnixEpoch));

        Assert.Empty(board.Waiting());
    }

    [Fact]
    public void ARefusedLoginKeepsItsNoticeWhateverTheWatchSays()
    {
        var board = new NoticeBoard();
        ConnectionNotices.Apply(board, CredentialIds.SleepNumber, ConnectionHealth.Rejected, DateTimeOffset.UnixEpoch);

        var fresh = BedOffline.Judge(BodyData.Daily(BodySignal.Sleep, Today, 5, 6.5).ToList(), true, Today);
        Assert.False(BedOffline.Apply(board, fresh, credentialRejected: true, DateTimeOffset.UnixEpoch));

        Assert.Single(board.Waiting());
    }

    [Fact]
    public void TheLastNightIsTheNightBeforeTheDatedMorning()
    {
        var state = BedOffline.Judge(SleepThenSilence(), bedChosen: true, Today);

        Assert.Equal(LastReading, state.LastReading);
        Assert.Equal("Thursday night", state.LastNight);
    }
}
