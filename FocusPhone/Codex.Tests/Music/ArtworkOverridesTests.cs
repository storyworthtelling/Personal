using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

public class ArtworkOverridesTests
{
    private static Track T(string album = "Blue", string albumArtist = "Miles") =>
        new("id", "Title", "Miles", album, albumArtist, 1, 1, "Jazz", TimeSpan.FromMinutes(3));

    [Fact]
    public void AlbumKey_CombinesAlbumArtistAndAlbum()
    {
        Assert.Equal(ArtworkOverrides.AlbumKey(T()), ArtworkOverrides.AlbumKey(T("Blue", "Miles")));
        Assert.NotEqual(ArtworkOverrides.AlbumKey(T("Blue")), ArtworkOverrides.AlbumKey(T("Red")));
    }

    [Fact]
    public void AlbumKey_TrackAndRawTagsAgree()
    {
        Assert.Equal(ArtworkOverrides.AlbumKey(T("Blue", "Miles")), ArtworkOverrides.AlbumKey("Miles", "Blue"));
    }

    [Fact]
    public void SetResolveClear()
    {
        var overrides = new ArtworkOverrides();
        var key = ArtworkOverrides.AlbumKey(T());
        Assert.Null(overrides.Resolve(key));

        overrides.Set(key, "/path/cover.jpg");
        Assert.Equal("/path/cover.jpg", overrides.Resolve(key));

        overrides.Clear(key);
        Assert.Null(overrides.Resolve(key));
    }

    [Fact]
    public void Codec_RoundTrips()
    {
        var overrides = new ArtworkOverrides();
        overrides.Set("k1", "/a.jpg");
        var decoded = new ArtworkOverrides(ArtworkOverrideCodec.Decode(ArtworkOverrideCodec.Encode(overrides.Export())));
        Assert.Equal("/a.jpg", decoded.Resolve("k1"));
    }

    [Fact]
    public void Codec_Garbled_ReturnsEmpty() => Assert.Empty(ArtworkOverrideCodec.Decode("nope"));
}
