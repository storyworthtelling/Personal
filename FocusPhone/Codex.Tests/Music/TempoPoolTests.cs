using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class TempoPoolTests
{
    private static Track T(string id, int bpm) =>
        new(id, $"Song {id}", "Artist", "Album", "Artist", 1, 1, "Rock", TimeSpan.FromMinutes(3), null, bpm);

    [Fact]
    public void Build_TakesWhatIsOnTempo()
    {
        var pool = TempoPool.Build([T("on", 160), T("off", 100)], 160);

        Assert.Equal(["on"], pool.Tracks.Select(track => track.Id));
    }

    [Fact]
    public void Build_AllowsTheTolerance()
    {
        var pool = TempoPool.Build([T("near", 160 + TempoPool.Tolerance), T("far", 160 + TempoPool.Tolerance + 1)], 160);

        Assert.Equal(["near"], pool.Tracks.Select(track => track.Id));
    }

    // Zero is unknown, not slow. Nobody has measured these, so they are not evidence
    // of anything and must not be run at a tempo they may not hold.
    [Fact]
    public void Build_IgnoresTracksNobodyHasMeasured()
    {
        Assert.Empty(TempoPool.Build([T("unmeasured", 0)], 160).Tracks);
    }

    // A track at half the target keeps step with it — every other beat lands. This is
    // ordinary practice for running to music, and it is most of what saves a pool
    // from being thin.
    [Fact]
    public void Build_KeepsHalfAndDoubleTime()
    {
        var pool = TempoPool.Build([T("half", 80), T("double", 320), T("other", 120)], 160);

        Assert.Equal(["double", "half"], pool.Tracks.Select(track => track.Id).OrderBy(id => id));
    }

    [Fact]
    public void Build_PutsTheClosestFirst()
    {
        var pool = TempoPool.Build([T("near", 162), T("exact", 160)], 160);

        Assert.Equal(["exact", "near"], pool.Tracks.Select(track => track.Id));
    }

    // Two tracks the same distance from the target must come out in the same order
    // every run, or the pool reshuffles itself under the player.
    [Fact]
    public void Build_IsSteadyWhenTwoAreEquallyClose()
    {
        var pool = TempoPool.Build([T("b", 162), T("a", 158)], 160);

        Assert.Equal(["a", "b"], pool.Tracks.Select(track => track.Id));
    }

    [Fact]
    public void Pool_KnowsWhenItIsThinEnoughToLoop()
    {
        var many = Enumerable.Range(0, TempoPool.Thin).Select(i => T($"t{i}", 160)).ToList();

        Assert.False(TempoPool.Build(many, 160).IsThin);
        Assert.True(TempoPool.Build(many.Take(TempoPool.Thin - 1).ToList(), 160).IsThin);
    }

    // What crosses over to Friday is a measurement, not a preference: "there is not
    // enough music at this speed", never "you liked this".
    [Fact]
    public void Gap_NamesTheTempoThatIsShort()
    {
        var gap = TempoPool.Build([T("a", 160)], 160).Gap;

        Assert.NotNull(gap);
        Assert.Contains("165", TempoPool.Build([T("a", 165)], 165).Gap!);
    }

    [Fact]
    public void Gap_IsSilentWhenThereIsEnoughToPlay()
    {
        var many = Enumerable.Range(0, TempoPool.Thin).Select(i => T($"t{i}", 160)).ToList();

        Assert.Null(TempoPool.Build(many, 160).Gap);
    }
}

public class TempoBandsTests
{
    [Theory]
    [InlineData(TempoBand.Easy)]
    [InlineData(TempoBand.Steady)]
    [InlineData(TempoBand.Hard)]
    public void Default_SitsInsideItsOwnBand(TempoBand band)
    {
        var (low, high) = TempoBands.Range(band);
        var start = TempoBands.Default(band);

        Assert.InRange(start, low, high);
    }

    // The bands have to cover the dial with no gap, or a tempo the owner can set is a
    // tempo no band claims and the label under the dial goes blank.
    [Fact]
    public void Bands_CoverTheWholeDial()
    {
        for (var bpm = TempoBands.Slowest; bpm <= TempoBands.Fastest; bpm++)
        {
            var (low, high) = TempoBands.Range(TempoBands.Of(bpm));
            Assert.InRange(bpm, low, high);
        }
    }

    [Fact]
    public void Of_ClampsBeyondTheDial()
    {
        Assert.Equal(TempoBand.Easy, TempoBands.Of(0));
        Assert.Equal(TempoBand.Hard, TempoBands.Of(400));
    }

    // The segmented control indexes the labels by the enum's own order, so a label
    // added out of order would silently set the wrong band.
    [Fact]
    public void Names_MatchTheBandsInOrder()
    {
        Assert.Equal(
            Enum.GetValues<TempoBand>().Select(band => band.ToString()),
            TempoBands.Names);
    }
}
