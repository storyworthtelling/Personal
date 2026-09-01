using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class TasteEvidenceBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static ListeningEvent Event(
        ListeningSignal signal,
        string genre = "Jazz",
        string artist = "Bill Evans",
        string albumArtist = "Bill Evans",
        int hour = 20,
        string session = "s1") =>
        new("id", genre, artist, albumArtist, signal, hour, session, Now);

    [Fact]
    public void Build_NoEvents_ReturnsEmpty()
    {
        var evidence = TasteEvidenceBuilder.Build([]);
        Assert.True(evidence.IsEmpty);
        Assert.Equal(0, evidence.SessionsAnalyzed);
        Assert.Equal(24, evidence.HourHistogram.Count);
        Assert.All(evidence.HourHistogram, count => Assert.Equal(0, count));
    }

    [Fact]
    public void Build_CountsDistinctSessionsNotRawPlays()
    {
        // Five completions of the same genre, but all in one session -> one distinct sitting.
        var events = Enumerable.Range(0, 5)
            .Select(_ => Event(ListeningSignal.Completed, session: "s1"))
            .ToList();

        var genre = Assert.Single(TasteEvidenceBuilder.Build(events).Genres);
        Assert.Equal("Jazz", genre.Key);
        Assert.Equal(1, genre.Sessions);
        Assert.Equal(5, genre.Completions);
    }

    [Fact]
    public void Build_DistinctSessionsAcrossSittings()
    {
        var events = new[]
        {
            Event(ListeningSignal.Completed, session: "s1"),
            Event(ListeningSignal.Completed, session: "s2"),
            Event(ListeningSignal.Completed, session: "s3"),
        };
        Assert.Equal(3, TasteEvidenceBuilder.Build(events).Genres.Single().Sessions);
        Assert.Equal(3, TasteEvidenceBuilder.Build(events).SessionsAnalyzed);
    }

    [Fact]
    public void Build_AggregatesEachSignalKind()
    {
        var events = new[]
        {
            Event(ListeningSignal.Completed),
            Event(ListeningSignal.Skipped),
            Event(ListeningSignal.Replayed),
            Event(ListeningSignal.PlayedFromAlbum),
            Event(ListeningSignal.PlayedFromShuffle),
        };
        var genre = TasteEvidenceBuilder.Build(events).Genres.Single();
        Assert.Equal(1, genre.Completions);
        Assert.Equal(1, genre.Skips);
        Assert.Equal(1, genre.Replays);
        Assert.Equal(1, genre.AlbumPlays);
        Assert.Equal(1, genre.ShufflePlays);
    }

    [Fact]
    public void Build_GroupsByArtistAndAlbumArtistSeparately()
    {
        var events = new[]
        {
            Event(ListeningSignal.Completed, artist: "Miles Davis", albumArtist: "Miles Davis"),
            Event(ListeningSignal.Completed, artist: "John Coltrane", albumArtist: "Various Artists"),
        };
        var evidence = TasteEvidenceBuilder.Build(events);
        Assert.Equal(2, evidence.Artists.Count);
        Assert.Contains(evidence.AlbumArtists, a => a.Key == "Various Artists");
    }

    [Fact]
    public void Build_HourHistogramCountsByLocalHour()
    {
        var events = new[]
        {
            Event(ListeningSignal.Completed, hour: 7),
            Event(ListeningSignal.Completed, hour: 7),
            Event(ListeningSignal.Completed, hour: 23),
        };
        var histogram = TasteEvidenceBuilder.Build(events).HourHistogram;
        Assert.Equal(2, histogram[7]);
        Assert.Equal(1, histogram[23]);
        Assert.Equal(0, histogram[12]);
    }

    [Fact]
    public void Build_SortsByDistinctSessionsThenNetCompletions()
    {
        var events = new[]
        {
            // Rock: 2 sessions
            Event(ListeningSignal.Completed, genre: "Rock", session: "s1"),
            Event(ListeningSignal.Completed, genre: "Rock", session: "s2"),
            // Jazz: 1 session
            Event(ListeningSignal.Completed, genre: "Jazz", session: "s3"),
        };
        var genres = TasteEvidenceBuilder.Build(events).Genres;
        Assert.Equal("Rock", genres[0].Key);
        Assert.Equal("Jazz", genres[1].Key);
    }
}
