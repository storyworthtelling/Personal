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
    public void HomeShelves_FillTheFirstScreenInFourEvenBands_ItemsRestOnLedges_AndTrayStacksBelow()
    {
        const float viewport = 800f;
        var scene = BookshelfLayout.Build(Width, M,
        [
            new ShelfSpec(ShelfFace.Clock, [], Home: true),
            new ShelfSpec(ShelfFace.CoverOut, [Cover("com.a"), Cover("com.b")], Home: true),
            new ShelfSpec(ShelfFace.CoverOut, [Cover("com.c"), Cover("com.d")], Home: true),
            new ShelfSpec(ShelfFace.CoverOut, [Cover("com.e"), Cover("com.f")], Home: true),
            new ShelfSpec(ShelfFace.SpineOut, [Spine("com.tray", SpineSizing.Size.Regular)]),
        ], viewportHeight: viewport);

        var home = scene.Shelves.Where(s => s.Home).ToList();
        Assert.Equal(4, home.Count);
        // A small crown reserve, then four EQUAL bands fill the first screen: shelf i's ledge sits at the
        // bottom of its band, so exactly the four home shelves land before the fold.
        const float crown = viewport * 0.055f;
        var band = (viewport - crown) / 4f;
        for (var i = 0; i < 4; i++)
        {
            Assert.True(home[i].Home);
            Assert.Equal(crown + (i + 1) * band, home[i].Ledge.Bottom, precision: 1);
            // Items STAND on the native ledge — their bottom is the ledge's top surface.
            Assert.All(home[i].Items, it => Assert.Equal(home[i].Ledge.Top, it.Rect.Bottom, precision: 1));
        }
        // The clock stands on its shelf (bottom-rested), not centered mid-band.
        var clock = Assert.Single(home[0].Items);
        Assert.Equal(home[0].Ledge.Top, clock.Rect.Bottom, precision: 1);
        // The two cover books fill most of their band height (a full shelf, not half-empty).
        var covers = home[1].Items;
        Assert.Equal(2, covers.Count);
        Assert.All(covers, it => Assert.True(it.Rect.Bottom - it.Rect.Top > band * 0.7f, "home books should fill the compartment"));
        // The tray shelf begins at/after the fold, not intruding into the home screen.
        var tray = scene.Shelves.First(s => !s.Home);
        Assert.True(tray.Ledge.Top >= viewport - 1, "tray intrudes into the home screen");
    }

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

    [Theory]
    [InlineData(6, 360f)]
    [InlineData(7, 360f)]
    [InlineData(6, 400f)]
    [InlineData(7, 400f)]
    public void EveryWrappedRowHasALedge(int siblings, float width)
    {
        var keys = Enumerable.Range(0, siblings).Select(i => $"com.example.sibling{i}").ToArray();
        var scene = BookshelfLayout.Build(width, M, [CoverShelf(keys)]);

        Assert.True(scene.Shelves.Count > 1, "the siblings must wrap for this test to mean anything");
        Assert.Equal(siblings, scene.Shelves.Sum(s => s.Items.Count));
        Assert.All(scene.Shelves, shelf =>
        {
            Assert.True(shelf.Ledge.Width > 0 && shelf.Ledge.Height > 0, "a row without a ledge floats");
            // Every book stands on its own row's ledge.
            Assert.All(shelf.Items, it => Assert.Equal(shelf.Ledge.Top, it.Rect.Bottom, precision: 1));
        });
    }
}
