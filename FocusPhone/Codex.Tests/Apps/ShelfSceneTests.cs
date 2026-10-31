using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class ShelfSceneTests
{
    private static readonly ShelfMetrics M = ShelfMetrics.Default;

    // The painter draws every PlacedShelf.Ledge through one DrawLedge call (lit top face + front edge
    // + shadow), so "full weight" is a property of the scene: each wrapped row's ledge must be the
    // same board as the first row's -- same thickness, same span -- and the row must say it is a
    // plain ledge-bearing shelf of the same face.
    [Theory]
    [InlineData(7, 360f)]
    [InlineData(7, 400f)]
    [InlineData(14, 400f)]
    public void WrappedLedgesArePaintedAtFullWeight(int siblings, float width)
    {
        var entries = Enumerable.Range(0, siblings).Select(i => new ShelfEntry($"com.example.sibling{i}")).ToList();
        var scene = BookshelfLayout.Build(width, M, [new ShelfSpec(ShelfFace.CoverOut, entries)]);

        Assert.True(scene.Shelves.Count > 1, "needs a wrap");
        var first = scene.Shelves[0].Ledge;
        foreach (var wrapped in scene.Shelves.Skip(1))
        {
            Assert.Equal(first.Height, wrapped.Ledge.Height, precision: 3);
            Assert.Equal(M.LedgeThickness, wrapped.Ledge.Height, precision: 3);
            Assert.Equal(first.Left, wrapped.Ledge.Left, precision: 3);
            Assert.Equal(first.Right, wrapped.Ledge.Right, precision: 3);
            Assert.Equal(scene.Shelves[0].Face, wrapped.Face);
        }
    }
}
