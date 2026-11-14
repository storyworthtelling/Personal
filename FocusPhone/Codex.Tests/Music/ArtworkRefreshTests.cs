using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

// Efficiency audit §5: the Playing screen decoded the cover once a second on the UI thread. It now decodes only when
// what it would show has changed: another track, another override, other embedded art, another feed.
public class ArtworkRefreshTests
{
    [Fact]
    public void TheSameCoverIsDecodedOnce()
    {
        var refresh = new ArtworkRefresh();
        var key = ArtworkRefresh.KeyOf("track-1", "content://invented/cover", 1200, null);

        Assert.True(refresh.Due(key));
        Assert.False(refresh.Due(key));
        Assert.False(refresh.Due(key));
    }

    [Fact]
    public void AnythingThatChangesTheCoverDecodesAgain()
    {
        var refresh = new ArtworkRefresh();
        refresh.Due(ArtworkRefresh.KeyOf("track-1", null, 1200, null));

        Assert.True(refresh.Due(ArtworkRefresh.KeyOf("track-2", null, 1200, null)));
        Assert.True(refresh.Due(ArtworkRefresh.KeyOf("track-2", "content://invented/cover", 1200, null)));
        Assert.True(refresh.Due(ArtworkRefresh.KeyOf("track-2", "content://invented/cover", 900, null)));
        Assert.True(refresh.Due(ArtworkRefresh.KeyOf("track-2", "content://invented/cover", 900, "feed-1")));
    }

    [Fact]
    public void AForgottenCoverIsDecodedAgain()
    {
        var refresh = new ArtworkRefresh();
        var key = ArtworkRefresh.KeyOf("track-1", null, 1200, null);
        refresh.Due(key);

        refresh.Forget();

        Assert.True(refresh.Due(key));
    }
}
