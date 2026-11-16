using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Apps;

/// <summary>
/// Flyleaf's shelf for TalkBack and for fingers (audit W9.2, report 10 §1, §14, §15, §25). The shelf is one drawn
/// canvas and the only home on the locked phone, so without these nodes a blind or switch user could open nothing.
/// </summary>
public class ShelfAccessTests
{
    private const float Target = 48f;

    private static PlacedItem Book(string key, float left, float right) =>
        new(key, ShelfFace.SpineOut, SpineSizing.Size.Slim, new LayoutRect(left, 100, right, 260));

    private static ShelfSceneLayout Scene(params PlacedShelf[] shelves) => new(shelves, 1000);

    private static PlacedShelf Row(params PlacedItem[] items) =>
        new(ShelfFace.SpineOut, new LayoutRect(0, 260, 400, 270), null, null, null, items);

    [Fact]
    public void EveryBookTheClockAndEveryNamedBookendIsANodeInReadingOrder()
    {
        var clock = new PlacedItem(BookshelfLayout.ClockKey, ShelfFace.Clock, SpineSizing.Size.Regular, new LayoutRect(100, 10, 276, 186));
        var scene = Scene(
            new PlacedShelf(ShelfFace.Clock, new LayoutRect(0, 186, 400, 196), null, null, null, [clock], Home: true),
            new PlacedShelf(ShelfFace.SpineOut, new LayoutRect(0, 260, 400, 270), new LayoutRect(20, 100, 70, 260), "folder_games", "Games",
                [Book("com.a", 72, 94), Book("com.b", 96, 118)]));

        var nodes = ShelfAccess.Nodes(scene, Target);

        Assert.Equal(
            [(ShelfNodeKind.Clock, BookshelfLayout.ClockKey), (ShelfNodeKind.Heading, "Games"), (ShelfNodeKind.Book, "com.a"), (ShelfNodeKind.Book, "com.b")],
            nodes.Select(node => (node.Kind, node.Key)).ToArray());
        Assert.Equal(Enumerable.Range(0, 4), nodes.Select(node => node.Id));
    }

    [Fact]
    public void ALoneSlimSpineIsWidenedToTheTargetAroundItsCentre()
    {
        var node = Assert.Single(ShelfAccess.Nodes(Scene(Row(Book("com.a", 100, 122))), Target));

        Assert.Equal(Target, node.Touch.Width, precision: 2);
        Assert.Equal(111f, (node.Touch.Left + node.Touch.Right) / 2, precision: 2);
    }

    [Fact]
    public void BetweenTwoSlimSpinesATapGoesToTheNearerOne()
    {
        var nodes = ShelfAccess.Nodes(Scene(Row(Book("com.a", 100, 122), Book("com.b", 124, 146))), Target);

        // The widened boxes meet halfway between the two spines' centres and never overlap.
        Assert.Equal(nodes[0].Touch.Right, nodes[1].Touch.Left, precision: 2);
        Assert.Equal(123f, nodes[0].Touch.Right, precision: 2);
        Assert.Equal("com.a", ShelfAccess.At(nodes, 122.5f, 200)?.Key);
        Assert.Equal("com.b", ShelfAccess.At(nodes, 123.5f, 200)?.Key);
    }

    [Fact]
    public void AWideBookKeepsItsOwnWidthAndAHeadingIsNeverATapTarget()
    {
        var wide = new PlacedItem("com.wide", ShelfFace.SpineOut, SpineSizing.Size.Broad, new LayoutRect(100, 100, 160, 260));
        var scene = Scene(new PlacedShelf(ShelfFace.SpineOut, new LayoutRect(0, 260, 400, 270), new LayoutRect(20, 100, 70, 260), "g", "Games", [wide]));
        var nodes = ShelfAccess.Nodes(scene, Target);

        Assert.Equal(60f, nodes.Single(node => node.Key == "com.wide").Touch.Width, precision: 2);
        Assert.Null(ShelfAccess.At(nodes, 45, 200));
    }

    [Fact]
    public void ABookReadsItsTitleAndWhatItsBookmarkMeans()
    {
        Assert.Equal("Post", ShelfAccess.BookText("Post", null));
        Assert.Equal("Post, needs attention", ShelfAccess.BookText("Post", BookmarkRole.Problem));
        Assert.Equal("Constitution, something to know", ShelfAccess.BookText("Constitution", BookmarkRole.Reference));
        Assert.Equal("Score, a notification is waiting", ShelfAccess.BookText("Score", BookmarkRole.Progress));
        Assert.Equal("Pulp, a quiet notification", ShelfAccess.BookText("Pulp", BookmarkRole.Resolved));
    }

    [Fact]
    public void TheClockSaysTheTimeTwelveHour() =>
        Assert.Equal("7:42 pm", ShelfAccess.ClockText(new TimeSpan(19, 42, 0)));
}
