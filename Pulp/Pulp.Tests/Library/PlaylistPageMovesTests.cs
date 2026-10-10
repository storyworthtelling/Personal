using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;

namespace Pulp.Tests.Library;

/// <summary>
/// Hand order by Move rather than drag (D103, the brief's ruling): a target big enough to hit, offering
/// only the moves that change something, and each keeping every other entry in its place. Plus the
/// status line under a playlist's header (mockup 04: "Shared · both of you · 6 titles").
/// </summary>
public class PlaylistPageMovesTests
{
    private static readonly PlaylistEntry A = PlaylistEntry.ForTitle(new TitleId("a-film-example"), TitleKind.Film);
    private static readonly PlaylistEntry B = PlaylistEntry.ForTitle(new TitleId("b-film-example"), TitleKind.Film);
    private static readonly PlaylistEntry C = PlaylistEntry.ForTitle(new TitleId("c-show-example"), TitleKind.Show);
    private static readonly PlaylistEntry D = PlaylistEntry.ForTitle(new TitleId("d-film-example"), TitleKind.Film);

    private static Playlist Four() => Playlist.Named("Rainy Sundays").Add(A).Add(B).Add(C).Add(D);

    [Fact]
    public void TheFirstEntryCanOnlyGoDown()
    {
        Assert.Equal([PlaylistMove.Down, PlaylistMove.ToBottom], PlaylistPageMoves.Offered(Four(), A));
    }

    [Fact]
    public void TheLastEntryCanOnlyGoUp()
    {
        Assert.Equal([PlaylistMove.ToTop, PlaylistMove.Up], PlaylistPageMoves.Offered(Four(), D));
    }

    [Fact]
    public void AMiddleEntryIsOfferedEveryMove()
    {
        Assert.Equal([PlaylistMove.ToTop, PlaylistMove.Up, PlaylistMove.Down, PlaylistMove.ToBottom], PlaylistPageMoves.Offered(Four(), B));
    }

    [Fact]
    public void ALoneEntryIsOfferedNothing()
    {
        Assert.Empty(PlaylistPageMoves.Offered(Playlist.Named("One").Add(A), A));
    }

    [Theory]
    [InlineData(PlaylistMove.Up, new[] { "a", "c", "b", "d" })]
    [InlineData(PlaylistMove.Down, new[] { "a", "b", "d", "c" })]
    [InlineData(PlaylistMove.ToTop, new[] { "c", "a", "b", "d" })]
    [InlineData(PlaylistMove.ToBottom, new[] { "a", "b", "d", "c" })]
    public void EachMoveKeepsTheRestInOrder(PlaylistMove move, string[] expected)
    {
        var moved = PlaylistPageMoves.Apply(Four(), C, move);

        Assert.Equal(expected, moved.Entries.Select(entry => entry.Key[..1]));
    }

    [Fact]
    public void AMoveThatChangesNothingIsRefused()
    {
        Assert.Throws<ArgumentException>(() => PlaylistPageMoves.Apply(Four(), A, PlaylistMove.Up));
    }

    [Fact]
    public void EachMoveHasALabel()
    {
        var labels = Enum.GetValues<PlaylistMove>().Select(PlaylistPageMoves.Label).ToList();

        Assert.Equal(4, labels.Count);
        Assert.Equal(["Move up", "Move down", "Move to the top", "Move to the bottom"], labels);
    }

    [Fact]
    public void ASharedPlaylistSaysBothOfYouAndCountsItsTitles()
    {
        var me = new ViewerId("viewer-one-example");
        var shared = new HouseholdPlaylist(new PlaylistId("p1"), new ViewerId("viewer-two-example"), Shared: true, Four());

        Assert.Equal("Shared · both of you · 4 titles", PlaylistPageSummary.Status(shared, me));
    }

    [Fact]
    public void APersonalPlaylistSaysYoursOnly()
    {
        var me = new ViewerId("viewer-one-example");
        var mine = new HouseholdPlaylist(new PlaylistId("p2"), me, Shared: false, Playlist.Named("Late films").Add(A));

        Assert.Equal("Yours only · 1 title", PlaylistPageSummary.Status(mine, me));
    }

    [Fact]
    public void AnEmptyPlaylistSaysNothingOnItYet()
    {
        var me = new ViewerId("viewer-one-example");
        var mine = new HouseholdPlaylist(new PlaylistId("p3"), me, Shared: false, Playlist.Named("Someday"));

        Assert.Equal("Yours only · no titles yet", PlaylistPageSummary.Status(mine, me));
    }
}
