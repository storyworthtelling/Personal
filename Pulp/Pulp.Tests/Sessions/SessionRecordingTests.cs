using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.Services;
using Pulp.Domain.Sessions;
using Pulp.Domain.Watching;

namespace Pulp.Tests.Sessions;

/// <summary>
/// Where a relayed session's evidence goes (G18 wiring): package to service, service to the title or
/// episode the household last handed off, and the viewers who handed it off. The resolution only builds
/// session sittings; <see cref="Watched.Decide"/> stays the one judge. Names and times are invented.
/// </summary>
public class SessionRecordingTests
{
    private static readonly StreamingService Flix = new(
        new ServiceId("example-flix"), "Exampleflix", ["com.example.flix", "com.example.flix.legacy"], "exampleflix", ["Exampleflix"]);

    private static readonly StreamingService Sample = new(
        new ServiceId("sample-stream"), "Samplestream", ["com.example.sample"], "samplestream", ["Samplestream"]);

    private static readonly ServiceCatalogue Catalogue = new([Flix, Sample]);
    private static readonly ViewerId Sam = new("adult-example-a");
    private static readonly ViewerId Wren = new("child-example-wren");
    private static readonly ViewerId Moss = new("child-example-moss");
    private static readonly TitleId Lantern = new("title-lantern-cove");
    private static readonly TitleId Orchard = new("title-orchard-row");
    private static readonly DateTimeOffset HandedAt = new(2026, 3, 6, 19, 30, 0, TimeSpan.FromHours(-5));
    private static readonly DateTimeOffset Stopped = HandedAt.AddMinutes(50);

    private static WatchedSitting HandOff(ViewerId viewer, TitleId title, StreamingService service, DateTimeOffset at, EpisodeRef? episode = null) =>
        new(viewer, new WatchEvent(title, WatchedKind.Show, at, service.Id, episode, WatchSource.HandOff));

    private static SessionMeaning Meaning(
        SessionEventKind kind, string package, bool ended = false, double minutes = 40, DateTimeOffset? at = null) =>
        new(kind, package, "Whatever The App Calls It", new SessionObservation(at ?? Stopped, TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(44), ended));

    [Fact]
    public void AStoppedSessionBecomesAnObservationOnTheTitleAndEpisodeHandedOff()
    {
        var episode = new EpisodeRef(2, 3);
        var sittings = new[] { HandOff(Sam, Lantern, Flix, HandedAt, episode) };

        var written = SessionRecording.Resolve(Meaning(SessionEventKind.StoppedShort, "com.example.flix"), Catalogue, sittings);

        var only = Assert.Single(written);
        Assert.Equal(Sam, only.Viewer);
        Assert.Equal(Lantern, only.Sitting.Title);
        Assert.Equal(episode, only.Sitting.Episode);
        Assert.Equal(Flix.Id, only.Sitting.Service);
        Assert.Equal(WatchSource.Session, only.Sitting.Evidence);
        Assert.Equal(Stopped, only.Sitting.At);
        Assert.Equal(TimeSpan.FromMinutes(40), only.Sitting.StoppedAt);
        Assert.Equal(TimeSpan.FromMinutes(44), only.Sitting.Runtime);
        Assert.False(only.Sitting.Ended);
    }

    [Fact]
    public void AnEndedSessionCarriesTheEndAndTheRuleStillJudgesIt()
    {
        var sittings = new[] { HandOff(Sam, Lantern, Flix, HandedAt) };

        var written = SessionRecording.Resolve(Meaning(SessionEventKind.Ended, "com.example.flix", ended: true, minutes: 43), Catalogue, sittings);

        var recorded = Assert.Single(written).Sitting;
        Assert.True(recorded.Ended);
        Assert.True(Sittings.Verdict([recorded]).IsWatched);
    }

    [Fact]
    public void ASecondPackageOfTheSameServiceResolvesToThatService()
    {
        var sittings = new[] { HandOff(Sam, Lantern, Flix, HandedAt) };

        Assert.Single(SessionRecording.Resolve(Meaning(SessionEventKind.StoppedShort, "com.example.flix.legacy"), Catalogue, sittings));
    }

    [Fact]
    public void ThePackageOfNoKnownServiceRecordsNothing()
    {
        var sittings = new[] { HandOff(Sam, Lantern, Flix, HandedAt) };

        Assert.Empty(SessionRecording.Resolve(Meaning(SessionEventKind.StoppedShort, "com.example.unknown"), Catalogue, sittings));
    }

    [Fact]
    public void ANewestHandOffOnThatServiceWins()
    {
        var sittings = new[]
        {
            HandOff(Sam, Lantern, Flix, HandedAt),
            HandOff(Sam, Orchard, Flix, HandedAt.AddMinutes(20)),
            HandOff(Sam, Lantern, Sample, HandedAt.AddMinutes(30)),
        };

        var written = SessionRecording.Resolve(Meaning(SessionEventKind.StoppedShort, "com.example.flix"), Catalogue, sittings);

        Assert.Equal(Orchard, Assert.Single(written).Sitting.Title);
    }

    [Fact]
    public void OnlyTheViewersWhoSharedThatHandOffGetTheEvidence()
    {
        // Wren and Moss watched together; Sam's earlier hand-off on the same service is not theirs to share.
        var sittings = new[]
        {
            HandOff(Sam, Lantern, Flix, HandedAt),
            HandOff(Wren, Orchard, Flix, HandedAt.AddHours(1)),
            HandOff(Moss, Orchard, Flix, HandedAt.AddHours(1)),
        };

        var written = SessionRecording.Resolve(
            Meaning(SessionEventKind.StoppedShort, "com.example.flix", at: HandedAt.AddHours(2)), Catalogue, sittings);

        Assert.Equal([Moss, Wren], written.Select(sitting => sitting.Viewer.Value).Order().Select(value => new ViewerId(value)).ToList());
        Assert.All(written, sitting => Assert.Equal(Orchard, sitting.Sitting.Title));
    }

    [Fact]
    public void AnAdultIsNeverGivenAChildsSessionEvenOnTheSameService()
    {
        var sittings = new[]
        {
            HandOff(Sam, Lantern, Flix, HandedAt),
            HandOff(Wren, Orchard, Flix, HandedAt.AddHours(1)),
        };

        var written = SessionRecording.Resolve(
            Meaning(SessionEventKind.StoppedShort, "com.example.flix", at: HandedAt.AddHours(2)), Catalogue, sittings);

        Assert.DoesNotContain(written, sitting => sitting.Viewer == Sam);
    }

    [Theory]
    [InlineData(SessionEventKind.Progress)]
    [InlineData(SessionEventKind.TitleChanged)]
    public void OnlyAClosedSessionIsRecorded(SessionEventKind kind)
    {
        // Progress would write a record per tick; a changed title is another title than the one handed off.
        var sittings = new[] { HandOff(Sam, Lantern, Flix, HandedAt) };

        Assert.Empty(SessionRecording.Resolve(Meaning(kind, "com.example.flix"), Catalogue, sittings));
    }

    [Fact]
    public void ASessionFromBeforeTheHandOffIsNotThatHandOffs()
    {
        var sittings = new[] { HandOff(Sam, Lantern, Flix, HandedAt) };

        var written = SessionRecording.Resolve(
            Meaning(SessionEventKind.StoppedShort, "com.example.flix", at: HandedAt.AddMinutes(-5)), Catalogue, sittings);

        Assert.Empty(written);
    }

    [Fact]
    public void AHandOffFromLongAgoIsNotTheTitleBeingWatchedNow()
    {
        var sittings = new[] { HandOff(Sam, Lantern, Flix, HandedAt) };

        var written = SessionRecording.Resolve(
            Meaning(SessionEventKind.StoppedShort, "com.example.flix", at: HandedAt + SessionRecording.HandOffWindow + TimeSpan.FromMinutes(1)),
            Catalogue,
            sittings);

        Assert.Empty(written);
    }

    [Fact]
    public void ServicelessAndNonHandOffSittingsAreNeverMatched()
    {
        var sittings = new[]
        {
            new WatchedSitting(Sam, new WatchEvent(Lantern, WatchedKind.Show, HandedAt, null, null, WatchSource.HandOff)),
            new WatchedSitting(Sam, new WatchEvent(Lantern, WatchedKind.Show, HandedAt, Flix.Id, null, WatchSource.MarkedWatched)),
        };

        Assert.Empty(SessionRecording.Resolve(Meaning(SessionEventKind.StoppedShort, "com.example.flix"), Catalogue, sittings));
    }

    [Fact]
    public void NoHandOffMeansNothingToAttachTheSessionTo()
    {
        Assert.Empty(SessionRecording.Resolve(Meaning(SessionEventKind.StoppedShort, "com.example.flix"), Catalogue, []));
    }

    // ---- the review's scenarios: a hand-off is one sitting, not a standing claim on the service ----

    private static readonly Dictionary<TitleId, string> Names = new()
    {
        [Lantern] = "Lantern Cove",
        [Orchard] = "Orchard Row",
    };

    private static string? NameOf(TitleId id) => Names.GetValueOrDefault(id);

    private static SessionMeaning Titled(string? title, SessionEventKind kind, bool ended, double minutes, DateTimeOffset at) =>
        new(kind, "com.example.flix", title, new SessionObservation(at, TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(100), ended));

    [Fact]
    public void AFilmWatchedToTheEndAfterAShowWasHandedOffIsNotTheShows()
    {
        // Lantern Cove (a show) was handed off; the household then opened a different film inside the app.
        var sittings = new[] { HandOff(Sam, Lantern, Flix, HandedAt) };
        var meaning = Titled("Orchard Row", SessionEventKind.Ended, ended: true, minutes: 96, at: Stopped);

        Assert.Empty(SessionRecording.Resolve(meaning, Catalogue, sittings, NameOf));
    }

    [Fact]
    public void TheRelayedTitleMatchesWhateverCaseOrDecorationTheAppAddsAround()
    {
        var sittings = new[] { HandOff(Sam, Lantern, Flix, HandedAt) };
        var meaning = Titled("LANTERN COVE - S2:E3 \"The Tide\"", SessionEventKind.StoppedShort, ended: false, minutes: 30, at: Stopped);

        Assert.Single(SessionRecording.Resolve(meaning, Catalogue, sittings, NameOf));
    }

    [Fact]
    public void ABlankRelayedTitleFallsBackToTheWindowAlone()
    {
        var sittings = new[] { HandOff(Sam, Lantern, Flix, HandedAt) };
        var meaning = Titled(null, SessionEventKind.StoppedShort, ended: false, minutes: 30, at: Stopped);

        Assert.Single(SessionRecording.Resolve(meaning, Catalogue, sittings, NameOf));
    }

    [Fact]
    public void AHandOffWhoseNameIsNotKnownFallsBackToTheWindowAlone()
    {
        var sittings = new[] { HandOff(Sam, Lantern, Flix, HandedAt) };
        var meaning = Titled("Anything At All", SessionEventKind.StoppedShort, ended: false, minutes: 30, at: Stopped);

        Assert.Single(SessionRecording.Resolve(meaning, Catalogue, sittings, titleName: id => null));
    }

    [Fact]
    public void AHandOffIsSpentOnceTheAppHasMovedToAnotherTitle()
    {
        // The title changed 20 minutes after the hand-off; the later stop belongs to the new title.
        var sittings = new[] { HandOff(Sam, Lantern, Flix, HandedAt) };
        var changedAt = HandedAt.AddMinutes(20);

        Assert.Empty(SessionRecording.Resolve(
            Meaning(SessionEventKind.StoppedShort, "com.example.flix"), Catalogue, sittings, titleChangedAt: changedAt));
    }

    [Fact]
    public void AHandOffMadeAfterTheTitleChangeIsNotSpent()
    {
        var sittings = new[] { HandOff(Sam, Orchard, Flix, HandedAt.AddMinutes(30)) };
        var changedAt = HandedAt.AddMinutes(20);

        Assert.Single(SessionRecording.Resolve(
            Meaning(SessionEventKind.StoppedShort, "com.example.flix"), Catalogue, sittings, titleChangedAt: changedAt));
    }

    [Fact]
    public void AnEpisodeThatAlreadyEndedSpendsTheHandOffSoTheAutoplayedNextOneIsNotItsTail()
    {
        var episode = new EpisodeRef(1, 1);
        var ended = new WatchedSitting(Sam, new WatchEvent(
            Lantern, WatchedKind.Show, HandedAt.AddMinutes(44), Flix.Id, episode, WatchSource.Session,
            TimeSpan.FromMinutes(43), TimeSpan.FromMinutes(44), true));
        var sittings = new[] { HandOff(Sam, Lantern, Flix, HandedAt, episode), ended };

        // Episode two autoplayed under the same title and the household stopped it ten minutes in.
        var meaning = Titled("Lantern Cove", SessionEventKind.StoppedShort, ended: false, minutes: 10, at: HandedAt.AddMinutes(60));

        Assert.Empty(SessionRecording.Resolve(meaning, Catalogue, sittings, NameOf));
    }

    [Fact]
    public void ASessionThatStoppedShortDoesNotSpendTheHandOffSoALaterStopStillCounts()
    {
        var episode = new EpisodeRef(1, 1);
        var paused = new WatchedSitting(Sam, new WatchEvent(
            Lantern, WatchedKind.Show, HandedAt.AddMinutes(20), Flix.Id, episode, WatchSource.Session,
            TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(44), false));
        var sittings = new[] { HandOff(Sam, Lantern, Flix, HandedAt, episode), paused };

        Assert.Single(SessionRecording.Resolve(
            Meaning(SessionEventKind.Ended, "com.example.flix", ended: true, minutes: 43, at: HandedAt.AddMinutes(60)), Catalogue, sittings));
    }

    [Fact]
    public void TwoViewersHandedOffAMillisecondApartAreOneHandOff()
    {
        var sittings = new[]
        {
            HandOff(Wren, Orchard, Flix, HandedAt),
            HandOff(Moss, Orchard, Flix, HandedAt.AddMilliseconds(1)),
        };

        var written = SessionRecording.Resolve(Meaning(SessionEventKind.StoppedShort, "com.example.flix"), Catalogue, sittings);

        Assert.Equal(2, written.Count);
    }

    [Fact]
    public void HandOffsFurtherApartThanTheToleranceAreTwoHandOffs()
    {
        var sittings = new[]
        {
            HandOff(Wren, Orchard, Flix, HandedAt),
            HandOff(Moss, Orchard, Flix, HandedAt + SessionRecording.HandOffTolerance + TimeSpan.FromSeconds(1)),
        };

        var written = SessionRecording.Resolve(Meaning(SessionEventKind.StoppedShort, "com.example.flix"), Catalogue, sittings);

        Assert.Equal(Moss, Assert.Single(written).Viewer);
    }

    [Fact]
    public void AnAdultsLaterHandOffTakesNothingFromTheChildAndTheChildGetsNothingFromIt()
    {
        var sittings = new[]
        {
            HandOff(Wren, Orchard, Flix, HandedAt),
            HandOff(Sam, Lantern, Flix, HandedAt.AddHours(1)),
        };

        var written = SessionRecording.Resolve(
            Meaning(SessionEventKind.StoppedShort, "com.example.flix", at: HandedAt.AddHours(2)), Catalogue, sittings);

        var only = Assert.Single(written);
        Assert.Equal(Sam, only.Viewer);
        Assert.Equal(Lantern, only.Sitting.Title);
    }

    [Fact]
    public void TheMemoryOfTitleChangesKeepsTheLatestPerPackage()
    {
        var memory = new TitleChangeMemory();
        Assert.Null(memory.LastSeen("com.example.flix"));

        memory.Seen("com.example.flix", HandedAt);
        memory.Seen("com.example.flix", HandedAt.AddMinutes(5));
        memory.Seen("com.example.flix", HandedAt.AddMinutes(2));
        memory.Seen("com.example.sample", HandedAt.AddMinutes(9));

        Assert.Equal(HandedAt.AddMinutes(5), memory.LastSeen("com.example.flix"));
        Assert.Equal(HandedAt.AddMinutes(9), memory.LastSeen("com.example.sample"));
    }
}
