using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class DuplicateTracksTests
{
    private static Track T(string id, string title, string artist, string album, int bitrate = 0,
        TimeSpan? duration = null) =>
        new(id, title, artist, album, artist, 1, 1, "Rock", duration ?? TimeSpan.FromMinutes(3), Bitrate: bitrate);

    [Fact]
    public void FindLosingIds_KeepsTheHigherBitrateCopyAndDropsTheOther()
    {
        var tracks = new[]
        {
            T("mp3", "Dare You To Move", "Switchfoot", "The Beautiful Letdown", bitrate: 128),
            T("flac", "Dare You To Move", "Switchfoot", "The Beautiful Letdown", bitrate: 1000),
        };

        var losers = DuplicateTracks.FindLosingIds(tracks);

        Assert.Equal(["mp3"], losers);
    }

    // Same artist, same title, but a different album — a studio cut and a live-album
    // recording of the same song are not the same file, and must not be conflated.
    [Fact]
    public void FindLosingIds_DoesNotTreatTheSameSongOnADifferentAlbumAsADuplicate()
    {
        var tracks = new[]
        {
            T("studio", "Dare You To Move", "Switchfoot", "The Beautiful Letdown", bitrate: 128),
            T("live", "Dare You To Move", "Switchfoot", "Live from Los Angeles", bitrate: 320),
        };

        Assert.Empty(DuplicateTracks.FindLosingIds(tracks));
    }

    [Fact]
    public void FindLosingIds_MatchesCaseAndWhitespaceInsensitively()
    {
        var tracks = new[]
        {
            T("a", "dare you to move", "switchfoot", "the beautiful letdown", bitrate: 128),
            T("b", " Dare You To Move ", " Switchfoot ", " The Beautiful Letdown ", bitrate: 320),
        };

        Assert.Equal(["a"], DuplicateTracks.FindLosingIds(tracks));
    }

    // An acoustic version shares a title fragment but is a genuinely different
    // recording — dedup must not apply the same loose edition-stripping TrackIdentity
    // uses for cross-service reconciliation, or a real alternate take would vanish.
    [Fact]
    public void FindLosingIds_DoesNotTreatAnEditionVariantAsADuplicate()
    {
        var tracks = new[]
        {
            T("a", "Dare You To Move", "Switchfoot", "The Beautiful Letdown", bitrate: 320),
            T("b", "Dare You To Move (Acoustic)", "Switchfoot", "The Beautiful Letdown", bitrate: 320),
        };

        Assert.Empty(DuplicateTracks.FindLosingIds(tracks));
    }

    [Fact]
    public void FindLosingIds_DropsEveryLoserWhenThreeCopiesExist()
    {
        var tracks = new[]
        {
            T("worst", "Yellow", "Coldplay", "Parachutes", bitrate: 96),
            T("best", "Yellow", "Coldplay", "Parachutes", bitrate: 320),
            T("middle", "Yellow", "Coldplay", "Parachutes", bitrate: 192),
        };

        var losers = DuplicateTracks.FindLosingIds(tracks);

        Assert.Equal(2, losers.Count);
        Assert.Contains("worst", losers);
        Assert.Contains("middle", losers);
        Assert.DoesNotContain("best", losers);
    }

    // Neither file reports a usable bitrate (an unusual tag, or a format TagLib can't
    // measure) — fall back to duration, since a truncated rip is the more likely of
    // two identically-unmeasured copies to be the lesser one.
    [Fact]
    public void FindLosingIds_FallsBackToDurationWhenBitrateIsTiedOrUnknown()
    {
        var tracks = new[]
        {
            T("short", "Yellow", "Coldplay", "Parachutes", bitrate: 0, duration: TimeSpan.FromSeconds(90)),
            T("full", "Yellow", "Coldplay", "Parachutes", bitrate: 0, duration: TimeSpan.FromMinutes(4)),
        };

        Assert.Equal(["short"], DuplicateTracks.FindLosingIds(tracks));
    }

    // Every signal ties exactly — a byte-identical file scanned under two Drive ids,
    // say. The result must still be deterministic run to run, not coin-flipped, or
    // the "surviving" copy would appear to change on every rescan.
    [Fact]
    public void FindLosingIds_IsDeterministicWhenEverySignalTies()
    {
        var tracks = new[]
        {
            T("b-id", "Yellow", "Coldplay", "Parachutes", bitrate: 320),
            T("a-id", "Yellow", "Coldplay", "Parachutes", bitrate: 320),
        };

        Assert.Equal(["b-id"], DuplicateTracks.FindLosingIds(tracks));
        // Same input, same answer — order of the input list must not matter either.
        Assert.Equal(["b-id"], DuplicateTracks.FindLosingIds([.. tracks.Reverse()]));
    }

    [Fact]
    public void FindLosingIds_ReturnsNothingWhenNoSongIsDuplicated()
    {
        var tracks = new[]
        {
            T("a", "Yellow", "Coldplay", "Parachutes"),
            T("b", "Dare You To Move", "Switchfoot", "The Beautiful Letdown"),
        };

        Assert.Empty(DuplicateTracks.FindLosingIds(tracks));
    }

    [Fact]
    public void FindLosingIds_ReturnsNothingForAnEmptyLibrary()
    {
        Assert.Empty(DuplicateTracks.FindLosingIds([]));
    }
}
