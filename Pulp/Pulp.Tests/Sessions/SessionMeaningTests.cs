using Pulp.Domain.Sessions;
using Pulp.Domain.Watching;

namespace Pulp.Tests.Sessions;

/// <summary>
/// What a relayed session means to the watch evidence (D30, D182, D286, D288). The load-bearing case:
/// a session alone never marks anything watched below the existing ninety-five-percent rule, and the
/// meaning only ever becomes a <see cref="SessionObservation"/> that <see cref="Watched.Decide"/>
/// judges. Titles and times are invented.
/// </summary>
public class SessionMeaningTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 2, 21, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Episode = TimeSpan.FromMinutes(44);

    private static SessionMessage Message(
        double positionMinutes,
        SessionPlayState state = SessionPlayState.Playing,
        string? title = "Harbor Lights",
        TimeSpan? duration = null) =>
        new("com.example.streamer", title, TimeSpan.FromMinutes(positionMinutes), duration ?? Episode, state);

    [Fact]
    public void PlayingIsProgressAndCarriesWhereItGot()
    {
        var meaning = SessionReading.Interpret(Message(10), null, Now);

        Assert.Equal(SessionEventKind.Progress, meaning.Kind);
        Assert.Equal(TimeSpan.FromMinutes(10), meaning.Observation.Position);
        Assert.Equal(Episode, meaning.Observation.Duration);
        Assert.Equal(Now, meaning.Observation.At);
        Assert.False(meaning.Observation.Ended);
    }

    [Fact]
    public void PausedIsStillProgress()
    {
        var meaning = SessionReading.Interpret(Message(10, SessionPlayState.Paused), null, Now);

        Assert.Equal(SessionEventKind.Progress, meaning.Kind);
        Assert.False(meaning.Observation.Ended);
    }

    [Fact]
    public void StoppedShortOfTheEndIsNotEnded()
    {
        var meaning = SessionReading.Interpret(Message(20, SessionPlayState.Stopped), null, Now);

        Assert.Equal(SessionEventKind.StoppedShort, meaning.Kind);
        Assert.False(meaning.Observation.Ended);
        Assert.False(Watched.Decide(Evidence(meaning)).IsWatched);
    }

    [Fact]
    public void StoppedJustBelowNinetyFivePercentIsNotEnded()
    {
        var position = Episode.TotalMinutes * Watched.EndFraction - 0.1;

        var meaning = SessionReading.Interpret(Message(position, SessionPlayState.Stopped), null, Now);

        Assert.Equal(SessionEventKind.StoppedShort, meaning.Kind);
        Assert.False(Watched.Decide(Evidence(meaning)).IsWatched);
    }

    [Fact]
    public void StoppedAtNinetyFivePercentIsEndedAndWatched()
    {
        var position = Episode.TotalMinutes * Watched.EndFraction;

        var meaning = SessionReading.Interpret(Message(position, SessionPlayState.Stopped), null, Now);

        Assert.Equal(SessionEventKind.Ended, meaning.Kind);
        Assert.True(meaning.Observation.Ended);
        Assert.Equal(WatchedReason.SessionReachedEnd, Watched.Decide(Evidence(meaning)).Reason);
    }

    [Fact]
    public void PlayingPastNinetyFivePercentIsStillOnlyProgressButTheRuleStillSeesIt()
    {
        var meaning = SessionReading.Interpret(Message(43), null, Now);

        Assert.Equal(SessionEventKind.Progress, meaning.Kind);
        Assert.False(meaning.Observation.Ended);
        Assert.True(Watched.Decide(Evidence(meaning)).IsWatched);
    }

    [Fact]
    public void StoppedWithNoDurationCannotBeEnded()
    {
        var meaning = SessionReading.Interpret(Message(40, SessionPlayState.Stopped, duration: TimeSpan.Zero), null, Now);

        Assert.Equal(SessionEventKind.StoppedShort, meaning.Kind);
        Assert.False(meaning.Observation.Ended);
    }

    [Fact]
    public void ADifferentTitleFromTheSamePackageIsATitleChange()
    {
        var before = Message(30);

        var meaning = SessionReading.Interpret(Message(2, title: "Harbor Lights, Part Two"), before, Now);

        Assert.Equal(SessionEventKind.TitleChanged, meaning.Kind);
        Assert.Equal("Harbor Lights, Part Two", meaning.Title);
    }

    [Fact]
    public void ATitleDifferingOnlyInCaseOrPaddingIsNotAChange()
    {
        var meaning = SessionReading.Interpret(Message(31, title: "  HARBOR LIGHTS "), Message(30), Now);

        Assert.Equal(SessionEventKind.Progress, meaning.Kind);
    }

    [Fact]
    public void AMissingTitleOnEitherSideIsNotAChange()
    {
        Assert.Equal(SessionEventKind.Progress, SessionReading.Interpret(Message(31, title: null), Message(30), Now).Kind);
        Assert.Equal(SessionEventKind.Progress, SessionReading.Interpret(Message(31), Message(30, title: null), Now).Kind);
    }

    [Fact]
    public void AStoppedMessageWhoseTitleDiffersIsStillTheEndOfTheOne()
    {
        var position = Episode.TotalMinutes * 0.99;

        var meaning = SessionReading.Interpret(Message(position, SessionPlayState.Stopped, title: "Next Up"), Message(40), Now);

        Assert.Equal(SessionEventKind.Ended, meaning.Kind);
    }

    [Fact]
    public void AnotherPackagesPreviousMessageIsNotCompared()
    {
        var other = Message(30) with { Package = "com.example.otherstreamer", Title = "Something Else" };

        var meaning = SessionReading.Interpret(Message(31), other, Now);

        Assert.Equal(SessionEventKind.Progress, meaning.Kind);
    }

    private static WatchEvidence Evidence(SessionMeaning meaning) => new([], [meaning.Observation], []);
}
