using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Library;

namespace Pulp.Tests.Children;

/// <summary>
/// S4 guard (D255): a child's parent favorites are held once, as the parents' picks. The filter's
/// set is derived from them and cannot be set on its own, so a pick above the ceiling can never be
/// on the child's Favorites but missing from what the filter allow-lists, or the other way round.
/// </summary>
public class ParentFavoritesAreThePicksTests
{
    [Fact]
    public void ParentFavoritesCannotBeSetApartFromThePicks()
    {
        var property = typeof(ChildRules).GetProperty(nameof(ChildRules.ParentFavorites));

        Assert.NotNull(property);
        Assert.Null(property.SetMethod);
    }

    [Fact]
    public void ParentFavoritesAreThePickedTitles()
    {
        var parent = new ViewerId("parent-example-a");
        var at = new DateTimeOffset(2026, 3, 1, 20, 0, 0, TimeSpan.Zero);
        var child = new ChildRules(new ViewerId("child-example-b"), AgeBand.SevenToNine)
        {
            ParentPicks = [new ParentPick(new TitleId("title-a-example"), parent, at), new ParentPick(new TitleId("title-b-example"), parent, at)],
        };

        Assert.Equal(2, child.ParentFavorites.Count);
        Assert.Contains(new TitleId("title-a-example"), child.ParentFavorites);
        Assert.Contains(new TitleId("title-b-example"), child.ParentFavorites);
        Assert.Empty((child with { ParentPicks = [] }).ParentFavorites);
    }
}
