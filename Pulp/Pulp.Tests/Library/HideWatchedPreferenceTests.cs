using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;

namespace Pulp.Tests.Library;

/// <summary>
/// D292 — "Hide watched" is remembered per playlist, on this phone only. The key is the playlist's id,
/// so two playlists keep their own setting and a renamed playlist keeps its. Invented ids throughout.
/// </summary>
public class HideWatchedPreferenceTests
{
    [Fact]
    public void EachPlaylistHasItsOwnKey()
    {
        var weekend = HideWatchedPreference.Key(new PlaylistId("pl-weekend-01"));
        var winter = HideWatchedPreference.Key(new PlaylistId("pl-winter-02"));

        Assert.NotEqual(weekend, winter);
        Assert.StartsWith(HideWatchedPreference.KeyPrefix, weekend, StringComparison.Ordinal);
        Assert.Contains("pl-weekend-01", weekend, StringComparison.Ordinal);
    }

    /// <summary>
    /// A playlist id compares without case, so the same playlist spelled two ways is one setting —
    /// otherwise the chip would forget itself whenever the id came back in a different case.
    /// </summary>
    [Fact]
    public void ThePlaylistsOwnIdIsOneKeyWhateverItsCase() =>
        Assert.Equal(
            HideWatchedPreference.Key(new PlaylistId("PL-Weekend-01")),
            HideWatchedPreference.Key(new PlaylistId("pl-weekend-01")));

    [Fact]
    public void AnUnsetPlaylistIdHasNoKey() =>
        Assert.Throws<ArgumentException>(() => HideWatchedPreference.Key(default));
}
