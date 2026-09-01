using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class HiddenTrackSplitTests
{
    private static readonly Track Original = new(
        "file123", "Where I End and You Begin", "Radiohead", "Amnesiac", "Radiohead",
        11, 1, "Rock", TimeSpan.FromMinutes(9), ArtRef: "art-ref", Bpm: 88);

    private static readonly IReadOnlyList<TrackSegment> Segments =
    [
        new TrackSegment(TimeSpan.Zero, TimeSpan.FromMinutes(4)),
        new TrackSegment(TimeSpan.FromMinutes(4.5), TimeSpan.FromMinutes(9)),
    ];

    [Fact]
    public void Apply_ReturnsTheOriginalUnchangedWhenThereIsOnlyOneSegment()
    {
        var result = HiddenTrackSplit.Apply(Original, [new TrackSegment(TimeSpan.Zero, Original.Duration)], null);

        Assert.Same(Original, Assert.Single(result));
    }

    [Fact]
    public void Apply_TheFirstSegmentKeepsTheOriginalsIdentityButIsShortened()
    {
        var result = HiddenTrackSplit.Apply(Original, Segments, "Like Spinning Plates (Slowed)");

        var first = result[0];
        Assert.Equal("Where I End and You Begin", first.Title);
        Assert.Equal(TimeSpan.FromMinutes(4), first.Duration);
        Assert.Equal("file123", first.SourceFileId);
        Assert.Equal(TimeSpan.Zero, first.ClipStart);
        Assert.NotEqual(Original.Id, first.Id); // each half needs its own queue/progress identity
    }

    [Fact]
    public void Apply_TheLastSegmentIsTheNamedHiddenTrack()
    {
        var result = HiddenTrackSplit.Apply(Original, Segments, "Like Spinning Plates (Slowed)");

        var second = result[1];
        Assert.Equal("Like Spinning Plates (Slowed)", second.Title);
        Assert.Equal(TimeSpan.FromMinutes(4.5), second.Duration);
        Assert.Equal("file123", second.SourceFileId);
        Assert.Equal(TimeSpan.FromMinutes(4.5), second.ClipStart);
    }

    [Fact]
    public void Apply_FallsBackToAGenericNameWhenClaudeDidNotKnowIt()
    {
        var result = HiddenTrackSplit.Apply(Original, Segments, hiddenTitle: null);

        Assert.Equal("Hidden Track", result[1].Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Apply_TreatsABlankNameTheSameAsNoName(string blank)
    {
        var result = HiddenTrackSplit.Apply(Original, Segments, blank);

        Assert.Equal("Hidden Track", result[1].Title);
    }

    // Everything about the source that is not specific to timing carries forward
    // unchanged onto both halves — the artwork and album fields are the same physical
    // release either way.
    [Fact]
    public void Apply_CarriesForwardEverythingElseFromTheOriginal()
    {
        var result = HiddenTrackSplit.Apply(Original, Segments, "Bonus");

        foreach (var track in result)
        {
            Assert.Equal("Radiohead", track.ArtistName);
            Assert.Equal("Amnesiac", track.AlbumName);
            Assert.Equal("Rock", track.Genre);
            Assert.Equal("art-ref", track.ArtRef);
        }
    }

    [Fact]
    public void Apply_GivesEachSegmentAUniqueIdDerivedFromTheOriginal()
    {
        var result = HiddenTrackSplit.Apply(Original, Segments, "Bonus");

        Assert.Equal(2, result.Select(t => t.Id).Distinct().Count());
        Assert.All(result, track => Assert.StartsWith("file123", track.Id));
    }
}
