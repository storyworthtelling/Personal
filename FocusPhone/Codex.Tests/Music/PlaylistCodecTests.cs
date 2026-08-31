using Codex.Domain.Music;
using Codex.Infrastructure.Music;
using Xunit;

namespace Codex.Tests.Music;

public class PlaylistCodecTests
{
    [Fact]
    public void RoundTripsEveryKind()
    {
        IReadOnlyList<Playlist> lists =
        [
            Playlist.Manual("a", "Practice", ["1", "2"]),
            Playlist.Generated("b", "Something quiet", ["3"]),
            Playlist.FromRules("c", "Switchfoot", new PlaylistRules(
                RuleMatch.Any,
                [
                    new PlaylistRule(TrackField.Artist, RuleOperator.Is, "Switchfoot"),
                    new PlaylistRule(TrackField.Genre, RuleOperator.IsNot, "Classical"),
                ])),
        ];

        var restored = PlaylistCodec.Decode(PlaylistCodec.Encode(lists));

        // Compared field by field on purpose. A record with a collection member has
        // reference equality on that member, so two playlists with identical contents
        // are never Equal — worth knowing before anything tries to use == to decide
        // whether the list changed.
        Assert.Equal(lists.Count, restored.Count);
        for (var i = 0; i < lists.Count; i++)
        {
            Assert.Equal(lists[i].Id, restored[i].Id);
            Assert.Equal(lists[i].Name, restored[i].Name);
            Assert.Equal(lists[i].Kind, restored[i].Kind);
            Assert.Equal(lists[i].TrackIds, restored[i].TrackIds);
            Assert.Equal(lists[i].Rules?.Match, restored[i].Rules?.Match);
            Assert.Equal(lists[i].Rules?.Rules, restored[i].Rules?.Rules);
        }
    }

    [Fact]
    public void RoundTripsProvenance()
    {
        IReadOnlyList<Playlist> lists = [Playlist.Manual("a", "Sunday", ["1"]) with { From = "Ashley" }];

        Assert.Equal("Ashley", PlaylistCodec.Decode(PlaylistCodec.Encode(lists))[0].From);
    }

    // Nothing stored yet is the ordinary first-run case, not a fault.
    [Fact]
    public void DecodesNothingToAnEmptyList()
    {
        Assert.Empty(PlaylistCodec.Decode(null));
        Assert.Empty(PlaylistCodec.Decode(""));
    }

    // A garbled value loses the playlists, which is bad, but taking the Music tab
    // down with it every launch is worse and cannot be recovered from on-device.
    [Fact]
    public void DecodesGarbageToAnEmptyListRatherThanThrowing()
    {
        Assert.Empty(PlaylistCodec.Decode("{ not json"));
        Assert.Empty(PlaylistCodec.Decode("[1, 2, 3]"));
    }
}
