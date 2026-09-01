using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class LibraryMergeTests
{
    private static Track Owned(string id, string title, string artist = "Switchfoot") =>
        new(id, title, artist, "Album", artist, 1, 1, "Rock", TimeSpan.FromMinutes(3));

    private static Track Streamed(string id, string title, string artist = "Switchfoot") =>
        Owned(id, title, artist) with { Availability = TrackAvailability.Streaming };

    [Fact]
    public void Merge_KeepsEverythingOwned()
    {
        var merged = LibraryMerge.Merge([Owned("1", "Dare You To Move")], []);

        Assert.Equal(["1"], merged.Select(track => track.Id));
        Assert.Equal(TrackAvailability.Local, merged.Single().Availability);
    }

    [Fact]
    public void Merge_AddsWhatTheOwnerDoesNotHave()
    {
        var merged = LibraryMerge.Merge([Owned("1", "Meant to Live")], [Streamed("s1", "Stars")]);

        Assert.Equal(["1", "s1"], merged.Select(track => track.Id));
    }

    // The rule the whole merge exists for: a file the owner has beats a stream of the
    // same song, always. Anything else and a lapsed subscription would take music
    // they own off the shelf.
    [Fact]
    public void Merge_DropsAStreamOfSomethingAlreadyOwned()
    {
        var merged = LibraryMerge.Merge([Owned("1", "Dare You To Move")], [Streamed("s1", "Dare You To Move")]);

        Assert.Equal(["1"], merged.Select(track => track.Id));
    }

    // The two sides name it differently, which is exactly when a naive merge shows
    // the same song twice.
    [Theory]
    [InlineData("Dare You To Move - 2011 Remaster")]
    [InlineData("Dare You To Move (Radio Edit)")]
    [InlineData("Dare You To Move [Live]")]
    public void Merge_SeesPastEditionSuffixes(string streamedTitle)
    {
        var merged = LibraryMerge.Merge([Owned("1", "Dare You To Move")], [Streamed("s1", streamedTitle)]);

        Assert.Equal(["1"], merged.Select(track => track.Id));
    }

    // The same title by someone else is a different record — a cover is not a copy.
    [Fact]
    public void Merge_KeepsACoverByAnotherArtist()
    {
        var merged = LibraryMerge.Merge(
            [Owned("1", "Dare You To Move")],
            [Streamed("s1", "Dare You To Move", "Vitamin String Quartet")]);

        Assert.Equal(["1", "s1"], merged.Select(track => track.Id));
    }

    [Fact]
    public void Merge_DoesNotAddTheSameStreamedSongTwice()
    {
        var merged = LibraryMerge.Merge([], [Streamed("s1", "Stars"), Streamed("s2", "Stars")]);

        Assert.Single(merged);
    }

    [Fact]
    public void Merge_OnAnEmptyLibraryIsJustTheStream()
    {
        Assert.Equal(["s1"], LibraryMerge.Merge([], [Streamed("s1", "Stars")]).Select(track => track.Id));
    }

    // A track the provider no longer carries stays listed and stops being playable
    // (DESIGN.md §8 Disabled, never removed): the owner put it there, and a silent
    // disappearance is the failure they would notice last and trust least.
    [Fact]
    public void Unavailable_StaysInTheLibraryAndStopsBeingPlayable()
    {
        var gone = Streamed("s1", "Stars") with { Availability = TrackAvailability.Unavailable };

        var merged = LibraryMerge.Merge([], [gone]);

        Assert.Single(merged);
        Assert.False(merged.Single().IsPlayable);
    }

    [Fact]
    public void Owned_AndStreamed_AreBothPlayable()
    {
        Assert.True(Owned("1", "Stars").IsPlayable);
        Assert.True(Streamed("s1", "Stars").IsPlayable);
    }

    // An unavailable stream must not shadow a file the owner has — that would be the
    // merge taking their own music away.
    [Fact]
    public void Merge_PrefersAnOwnedFileOverAnUnavailableStream()
    {
        var gone = Streamed("s1", "Stars") with { Availability = TrackAvailability.Unavailable };

        var merged = LibraryMerge.Merge([Owned("1", "Stars")], [gone]);

        Assert.Equal(["1"], merged.Select(track => track.Id));
        Assert.True(merged.Single().IsPlayable);
    }
}
