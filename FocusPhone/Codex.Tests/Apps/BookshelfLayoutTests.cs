using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

public class BookshelfLayoutTests
{
    private const float Width = 400f;
    private static readonly ShelfMetrics M = ShelfMetrics.Default;

    private static ShelfEntry Cover(string key) => new(key);
    private static ShelfEntry Spine(string key, SpineSizing.Size size) => new(key, size);

    private static ShelfSpec CoverShelf(params string[] keys) =>
        new(ShelfFace.CoverOut, keys.Select(Cover).ToList());

    [Fact]
    public void Shelves_StackTopToBottom_WithoutOverlap()
    {
        var scene = BookshelfLayout.Build(Width, M,
        [
            new ShelfSpec(ShelfFace.Clock, []),
            CoverShelf("com.a", "com.b"),
            CoverShelf("com.c", "com.d"),
        ]);

        Assert.Equal(3, scene.Shelves.Count);
        for (var i = 1; i < scene.Shelves.Count; i++)
        {
            Assert.True(scene.Shelves[i].Ledge.Top >= scene.Shelves[i - 1].Ledge.Bottom,
                $"shelf {i} overlaps shelf {i - 1}");
        }
        Assert.True(scene.TotalHeight >= scene.Shelves[^1].Ledge.Bottom);
    }

    [Fact]
    public void ClockShelf_CentersASingleClockObject()
    {
        var scene = BookshelfLayout.Build(Width, M, [new ShelfSpec(ShelfFace.Clock, [])]);

        var clock = Assert.Single(scene.Shelves[0].Items);
        Assert.Equal(BookshelfLayout.ClockKey, clock.Key);
        Assert.Equal(ShelfFace.Clock, clock.Face);
        // Centered horizontally: left margin == right margin.
        Assert.Equal(clock.Rect.Left, Width - clock.Rect.Right, 3);
        Assert.Equal(M.ClockSize, clock.Rect.Width, 3);
    }

    [Fact]
    public void CoverOutBooks_ArePortrait_AndBottomAlignedOnTheLedge()
    {
        var scene = BookshelfLayout.Build(Width, M, [CoverShelf("com.a", "com.b")]);

        var shelf = scene.Shelves[0];
        foreach (var item in shelf.Items)
        {
            Assert.Equal(ShelfFace.CoverOut, item.Face);
            Assert.True(item.Rect.Height > item.Rect.Width, "cover should be portrait");
            Assert.Equal(shelf.Ledge.Top, item.Rect.Bottom, 3); // rests on the shelf
        }
        // Left-to-right, order preserved.
        Assert.Equal(new[] { "com.a", "com.b" }, shelf.Items.Select(i => i.Key));
        Assert.True(shelf.Items[1].Rect.Left > shelf.Items[0].Rect.Right - 1);
    }

    [Fact]
    public void SpineWidthAndHeight_RiseWithUsage()
    {
        var scene = BookshelfLayout.Build(Width, M,
        [
            new ShelfSpec(ShelfFace.SpineOut,
                [Spine("slim", SpineSizing.Size.Slim), Spine("broad", SpineSizing.Size.Broad)]),
        ]);

        var items = scene.Shelves[0].Items.ToDictionary(i => i.Key);
        Assert.True(items["broad"].Rect.Width > items["slim"].Rect.Width, "broad spine wider");
        Assert.True(items["broad"].Rect.Height > items["slim"].Rect.Height, "broad spine taller");
        // Different heights still rest on the same ledge.
        Assert.Equal(items["slim"].Rect.Bottom, items["broad"].Rect.Bottom, 3);
    }

    [Fact]
    public void ManySpines_WrapOntoAdditionalShelfRows_WithoutOverflow()
    {
        var many = Enumerable.Range(0, 40).Select(i => Spine($"com.app{i}", SpineSizing.Size.Regular)).ToList();
        var scene = BookshelfLayout.Build(Width, M, [new ShelfSpec(ShelfFace.SpineOut, many, BookendGlyph: "folder_x", BookendName: "Stuff")]);

        Assert.True(scene.Shelves.Count > 1, "40 spines should wrap onto several shelf rows");
        var contentRight = Width - M.SidePadding;
        foreach (var shelf in scene.Shelves)
        {
            foreach (var item in shelf.Items)
            {
                Assert.True(item.Rect.Right <= contentRight + 0.5f, "no book overflows the shelf width");
            }
        }
        // All 40 placed, order preserved across rows.
        Assert.Equal(many.Select(e => e.Key), scene.Shelves.SelectMany(s => s.Items).Select(i => i.Key));
    }

    [Fact]
    public void Bookend_SitsOnlyOnTheFirstRow_OfACategoryShelf()
    {
        var many = Enumerable.Range(0, 40).Select(i => Spine($"com.app{i}", SpineSizing.Size.Regular)).ToList();
        var scene = BookshelfLayout.Build(Width, M, [new ShelfSpec(ShelfFace.SpineOut, many, BookendGlyph: "folder_x", BookendName: "Stuff")]);

        Assert.NotNull(scene.Shelves[0].Bookend);
        Assert.Equal("Stuff", scene.Shelves[0].BookendName);
        Assert.All(scene.Shelves.Skip(1), s => Assert.Null(s.Bookend));
        // The first book clears the bookend.
        Assert.True(scene.Shelves[0].Items[0].Rect.Left >= scene.Shelves[0].Bookend!.Value.Right);
    }

    [Fact]
    public void SiblingAndLooseShelves_HaveNoBookend()
    {
        var scene = BookshelfLayout.Build(Width, M,
        [
            CoverShelf("com.sibling1", "com.sibling2"), // sibling row, cover-out, no bookend
            new ShelfSpec(ShelfFace.SpineOut, [Spine("com.loose", SpineSizing.Size.Slim)]),
        ]);

        Assert.All(scene.Shelves, s => Assert.Null(s.Bookend));
    }

    [Fact]
    public void EmptyNonClockShelf_IsOmitted()
    {
        var scene = BookshelfLayout.Build(Width, M,
        [
            new ShelfSpec(ShelfFace.SpineOut, [], BookendGlyph: "folder_x", BookendName: "Empty"),
            CoverShelf("com.a", "com.b"),
        ]);

        Assert.Single(scene.Shelves);
        Assert.Equal(ShelfFace.CoverOut, scene.Shelves[0].Face);
    }
}
