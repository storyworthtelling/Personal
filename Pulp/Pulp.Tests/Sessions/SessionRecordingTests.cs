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
}
