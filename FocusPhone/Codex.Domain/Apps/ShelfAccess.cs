using Storyworthtelling.Time;

namespace Codex.Domain.Apps;

/// <summary>What a node on the shelf is: an app's book, the mantel clock, or a shelf's bookend.</summary>
public enum ShelfNodeKind
{
    Book,
    Clock,

    /// <summary>A named bookend: read as a heading so TalkBack can jump shelf to shelf. Never tapped.</summary>
    Heading,
}

/// <summary>One thing on the shelf a screen reader can land on, and the box a finger can hit it in.</summary>
/// <param name="Id">Stable for one layout: the node's place in reading order.</param>
/// <param name="Key">A book's package, the clock's key, or a bookend's name.</param>
/// <param name="Drawn">Where it is drawn.</param>
/// <param name="Touch">Where it answers a touch: at least the target wide where the neighbours leave room.</param>
public sealed record ShelfNode(int Id, ShelfNodeKind Kind, string Key, LayoutRect Drawn, LayoutRect Touch);

/// <summary>
/// Flyleaf's shelf for TalkBack, Switch Access and fingers (DESIGN.md §17, audit W9.2, report 10 §1, §14, §15, §25).
///
/// <para>The shelf is one drawn canvas and the only home on the locked phone, so a node per thing on it is the only
/// way a blind or switch user can open an app or reach Settings. The nodes are decided here, from the same layout the
/// painter draws, so what is read and what is hit cannot drift from what is drawn.</para>
///
/// <para><b>Touch.</b> A slim spine is 22dp; a box narrower than 48dp is widened about its centre, and where two
/// widened boxes would overlap they meet halfway between the two spines' centres, so a tap between two spines goes to
/// the nearer one. Abutting slim spines therefore still give each about its own width: a wider minimum spine is a
/// change to the drawing, not to this.</para>
/// </summary>
public static class ShelfAccess
{
    /// <summary>Vertical slack around a book, in the same units as the layout's density-free metrics.</summary>
    public const float SlackAbove = 12f;
    public const float SlackBelow = 8f;

    public static IReadOnlyList<ShelfNode> Nodes(ShelfSceneLayout layout, float target, float scale = 1f)
    {
        var nodes = new List<ShelfNode>();
        foreach (var shelf in layout.Shelves)
        {
            if (shelf.Bookend is { } end && !string.IsNullOrWhiteSpace(shelf.BookendName))
            {
                nodes.Add(new ShelfNode(nodes.Count, ShelfNodeKind.Heading, shelf.BookendName, end, end));
            }

            var items = shelf.Items.OrderBy(item => item.Rect.Left).ToList();
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var widened = Widened(item.Rect, target);
                var left = widened.Left;
                var right = widened.Right;

                if (i > 0)
                {
                    left = Math.Max(left, Math.Min(item.Rect.Left, Middle(items[i - 1].Rect, item.Rect)));
                }

                if (i < items.Count - 1)
                {
                    right = Math.Min(right, Math.Max(item.Rect.Right, Middle(item.Rect, items[i + 1].Rect)));
                }

                var touch = new LayoutRect(left, item.Rect.Top - SlackAbove * scale, right, item.Rect.Bottom + SlackBelow * scale);
                var kind = item.Key == BookshelfLayout.ClockKey ? ShelfNodeKind.Clock : ShelfNodeKind.Book;
                nodes.Add(new ShelfNode(nodes.Count, kind, item.Key, item.Rect, touch));
            }
        }

        return nodes;
    }

    /// <summary>The book or clock a touch lands on; a heading is read, never tapped.</summary>
    public static ShelfNode? At(IReadOnlyList<ShelfNode> nodes, float x, float y) =>
        nodes.FirstOrDefault(node => node.Kind != ShelfNodeKind.Heading
            && x >= node.Touch.Left && x < node.Touch.Right && y >= node.Touch.Top && y <= node.Touch.Bottom);

    /// <summary>A book's words: its title, and what its bookmark means — the ribbon's colour said aloud (§15).</summary>
    public static string BookText(string label, BookmarkRole? bookmark) => bookmark switch
    {
        null => label,
        BookmarkRole.Problem => $"{label}, needs attention",
        BookmarkRole.Reference => $"{label}, something to know",
        BookmarkRole.Resolved => $"{label}, a quiet notification",
        _ => $"{label}, a notification is waiting",
    };

    /// <summary>The mantel clock's words: the time, twelve-hour (§25; the status bar is off on the locked phone).</summary>
    public static string ClockText(TimeSpan time) => TimeFormats.Clock12(DateTime.MinValue.Add(time));

    private static LayoutRect Widened(LayoutRect rect, float target)
    {
        if (rect.Width >= target)
        {
            return rect;
        }

        var centre = (rect.Left + rect.Right) / 2;
        return rect with { Left = centre - (target / 2), Right = centre + (target / 2) };
    }

    private static float Middle(LayoutRect first, LayoutRect second) =>
        (((first.Left + first.Right) / 2) + ((second.Left + second.Right) / 2)) / 2;
}
