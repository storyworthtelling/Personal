using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

/// <summary>
/// The crate came back near-identical every Friday because nothing remembered what it had
/// already offered: the same profile and the same library went in each week, so the same
/// records came out. The history is that memory.
/// </summary>
public class CrateHistoryTests
{
    private static readonly DateOnly Friday = new(2026, 11, 13);

    private static Recommendation Song(string title, string artist) => new(title, artist, "why");

    [Fact]
    public void ARememberedPickIsOffered_AndAnotherIsNot()
    {
        var history = CrateHistory.Empty.Remember([Song("Blue in Green", "Bill Evans")], Friday);

        Assert.True(history.HasOffered(Song("blue in green ", "BILL EVANS")));
        Assert.False(history.HasOffered(Song("So What", "Miles Davis")));
    }

    [Fact]
    public void APickOlderThanTheWindowIsForgotten()
    {
        var history = CrateHistory.Empty
            .Remember([Song("Old", "Artist")], Friday.AddDays(-7 * CrateHistory.Weeks))
            .Remember([Song("New", "Artist")], Friday);

        Assert.False(history.HasOffered(Song("Old", "Artist")));
        Assert.True(history.HasOffered(Song("New", "Artist")));
    }

    [Fact]
    public void RememberingTheSameCrateTwiceKeepsOneEntryEach()
    {
        var crate = new[] { Song("A", "X"), Song("B", "Y") };
        var history = CrateHistory.Empty.Remember(crate, Friday).Remember(crate, Friday);

        Assert.Equal(2, history.Offered.Count);
    }

    [Fact]
    public void TheHistoryIsCappedNewestKept()
    {
        var history = CrateHistory.Empty;
        for (var i = 0; i < CrateHistory.MaxPicks + 10; i++)
        {
            history = history.Remember([Song($"Track {i}", "Artist")], Friday);
        }

        Assert.Equal(CrateHistory.MaxPicks, history.Offered.Count);
        Assert.True(history.HasOffered(Song($"Track {CrateHistory.MaxPicks + 9}", "Artist")));
        Assert.False(history.HasOffered(Song("Track 0", "Artist")));
    }

    [Fact]
    public void FreshDropsWhatWasAlreadyOffered()
    {
        var history = CrateHistory.Empty.Remember([Song("Blue in Green", "Bill Evans")], Friday);

        var fresh = history.Fresh([Song("Blue in Green", "Bill Evans"), Song("So What", "Miles Davis")]);

        Assert.Equal("So What", Assert.Single(fresh).Title);
    }

    [Fact]
    public void ItRoundTripsThroughItsCodec()
    {
        var history = CrateHistory.Empty.Remember(
            [Song("Blue in Green", "Bill Evans"), new Recommendation("Radiolab", "", "why", DiscoveryKind.Podcast)], Friday);

        var back = CrateHistoryCodec.Read(CrateHistoryCodec.Write(history));

        Assert.Equal(history.Offered, back.Offered);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void AnUnreadableHistoryReadsAsEmpty(string? json) =>
        Assert.Empty(CrateHistoryCodec.Read(json).Offered);
}
