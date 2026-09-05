using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class DiscoveryCrateTests
{
    private static CratePick P(string id) => new(id, $"Record {id}", "An Artist");

    private static DiscoveryCrate Crate(params string[] ids) =>
        new([.. ids.Select(P)]);

    [Fact]
    public void Crate_OpensOnTheFirstPick()
    {
        var crate = Crate("a", "b", "c");

        Assert.Equal("a", crate.Current!.Id);
        Assert.Equal(0, crate.Index);
    }

    // The stepped sleeve edges behind the pick: how much of the run is still coming.
    [Fact]
    public void Behind_CountsWhatIsStillToCome()
    {
        var crate = Crate("a", "b", "c");

        Assert.Equal(2, crate.Behind);

        crate.Next();

        Assert.Equal(1, crate.Behind);
    }

    [Fact]
    public void Next_StopsAtTheEndRatherThanWrapping()
    {
        var crate = Crate("a", "b");

        crate.Next();
        crate.Next();

        Assert.Equal("b", crate.Current!.Id);
    }

    [Fact]
    public void Previous_StopsAtTheStart()
    {
        var crate = Crate("a", "b");

        crate.Previous();

        Assert.Equal("a", crate.Current!.Id);
    }

    // A veto strikes the record and moves on — you passed on it, so the next one is
    // what you wanted. It stays in the run, which is what the backwards chevron is
    // for.
    [Fact]
    public void Veto_StrikesThePickAndMovesOn()
    {
        var crate = Crate("a", "b");

        crate.Veto();

        Assert.True(crate.IsVetoed("a"));
        Assert.Equal("b", crate.Current!.Id);
        Assert.Equal(2, crate.Picks.Count);
    }

    [Fact]
    public void Veto_OnTheLastPickStaysThere()
    {
        var crate = Crate("a");

        crate.Veto();

        Assert.True(crate.CurrentIsVetoed);
        Assert.Equal("a", crate.Current!.Id);
    }

    [Fact]
    public void Restore_LiftsTheStrike()
    {
        var crate = Crate("a", "b");
        crate.Veto();

        crate.Previous();
        crate.Restore();

        Assert.False(crate.IsVetoed("a"));
        Assert.Empty(crate.Vetoed);
    }

    [Fact]
    public void Veto_TwiceIsStillOneVeto()
    {
        var crate = Crate("a", "b");

        crate.Veto();
        crate.Previous();
        crate.Veto();

        Assert.Equal(["a"], crate.Vetoed);
    }

    // The strikes have to survive leaving the tab and coming back, or a veto is
    // undone by walking away from it.
    [Fact]
    public void Crate_RestoresStrikesItIsGiven()
    {
        var crate = new DiscoveryCrate([P("a"), P("b")], ["a"]);

        Assert.True(crate.IsVetoed("a"));
        Assert.Equal(["a"], crate.Vetoed);
    }

    [Fact]
    public void Crate_IgnoresStrikesForPicksItDoesNotHold()
    {
        var crate = new DiscoveryCrate([P("a")], ["gone"]);

        Assert.Empty(crate.Vetoed);
    }

    [Fact]
    public void AnEmptyCrate_HasNothingAndDoesNothing()
    {
        var crate = new DiscoveryCrate([]);

        crate.Next();
        crate.Veto();

        Assert.True(crate.IsEmpty);
        Assert.Null(crate.Current);
        Assert.Empty(crate.Vetoed);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    // Past three the steps stop being countable and become texture; the number beside
    // the chevrons is what reports the size of the run.
    [InlineData(30, 3)]
    public void VisibleEdges_ShowsAtMostThree(int behind, int expected)
    {
        Assert.Equal(expected, CrateGeometry.VisibleEdges(behind));
    }
}
