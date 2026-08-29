using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

public class TagMappingTests
{
    [Fact]
    public void ToTrack_UsesFirstPerformerAndAlbumArtist()
    {
        var track = TagMapping.ToTrack(
            "file1", "Title", ["Lead", "Feature"], "Album", ["Album Artist"],
            trackNumber: 3, discNumber: 2, genres: ["Rock", "Alt"], duration: TimeSpan.FromMinutes(4));

        Assert.Equal("Lead", track.ArtistName);
        Assert.Equal("Album Artist", track.AlbumArtist);
        Assert.Equal("Rock", track.Genre);
        Assert.Equal(3, track.TrackNumber);
        Assert.Equal(2, track.DiscNumber);
        Assert.Equal(TimeSpan.FromMinutes(4), track.Duration);
    }

    [Fact]
    public void ToTrack_DefaultsDiscToOneWhenUnset()
    {
        var track = TagMapping.ToTrack("file1", "T", ["A"], "Al", ["A"], 1, 0, ["G"], TimeSpan.Zero);

        Assert.Equal(1, track.DiscNumber);
    }

    [Fact]
    public void ToTrack_BlankFieldsFallBackViaTrackDisplay()
    {
        var track = TagMapping.ToTrack("file1", null, null, null, null, 0, 0, null, TimeSpan.Zero);

        Assert.Equal("Unknown Title", track.DisplayTitle);
        Assert.Equal("Unknown Artist", track.DisplayArtist);
        Assert.Equal("Unknown Album", track.DisplayAlbum);
        Assert.Equal("Unknown Genre", track.DisplayGenre);
    }

    [Fact]
    public void ToTrack_AlbumArtistDefaultsToTrackArtistWhenMissing()
    {
        var track = TagMapping.ToTrack("file1", "T", ["Solo Act"], "Al", null, 1, 1, ["G"], TimeSpan.Zero);

        Assert.Equal("Solo Act", track.DisplayAlbumArtist);
    }

    [Fact]
    public void ToTrack_KeepsTheFileIdAsTrackId()
    {
        var track = TagMapping.ToTrack("drive-abc", "T", ["A"], "Al", ["A"], 1, 1, ["G"], TimeSpan.Zero);

        Assert.Equal("drive-abc", track.Id);
    }

    // Tempo comes from the tag first, because it is nearly free — the scanner already
    // opens every file, and reading a field it has in hand costs nothing next to
    // analysing the audio.
    [Fact]
    public void ToTrack_ReadsTheTaggedTempo()
    {
        var track = TagMapping.ToTrack(
            "file1", "T", ["A"], "Al", ["A"], 1, 1, ["G"], TimeSpan.Zero, beatsPerMinute: 128);

        Assert.Equal(128, track.Bpm);
    }

    // Most files carry no BPM tag at all, and zero is how the catalog says "unknown"
    // rather than "no beats".
    [Fact]
    public void ToTrack_LeavesTempoUnknownWhenTheTagHasNone()
    {
        var track = TagMapping.ToTrack("file1", "T", ["A"], "Al", ["A"], 1, 1, ["G"], TimeSpan.Zero);

        Assert.Equal(0, track.Bpm);
    }

    // Bitrate is what tells a duplicate scan which of two copies of the same song is
    // the better source, so it has to survive the same tag-to-domain mapping as
    // everything else the file carries.
    [Fact]
    public void ToTrack_ReadsTheBitrate()
    {
        var track = TagMapping.ToTrack(
            "file1", "T", ["A"], "Al", ["A"], 1, 1, ["G"], TimeSpan.Zero, bitrateKbps: 320);

        Assert.Equal(320, track.Bitrate);
    }

    [Fact]
    public void ToTrack_LeavesBitrateUnknownWhenNotGiven()
    {
        var track = TagMapping.ToTrack("file1", "T", ["A"], "Al", ["A"], 1, 1, ["G"], TimeSpan.Zero);

        Assert.Equal(0, track.Bitrate);
    }
}
