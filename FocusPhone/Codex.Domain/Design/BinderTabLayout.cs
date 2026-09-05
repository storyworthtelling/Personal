namespace Codex.Domain.Design;

/// <summary>One tab's vertical extent within the binder gutter, in dp.</summary>
public readonly record struct BinderTab(int TopDp, int HeightDp)
{
    public int BottomDp => TopDp + HeightDp;
}

/// <summary>
/// The strip of tabs down the right edge, plus whether it had to scroll to fit.
/// </summary>
public sealed class BinderTabStrip(IReadOnlyList<BinderTab> tabs, bool scrolls)
    : IReadOnlyList<BinderTab>
{
    public bool Scrolls { get; } = scrolls;

    public BinderTab this[int index] => tabs[index];

    public int Count => tabs.Count;

    public System.Collections.Generic.IEnumerator<BinderTab> GetEnumerator() => tabs.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Which tab a point down the gutter hits, or null for the gaps between them
    /// and anywhere outside the strip. Gaps miss deliberately: a tap that lands
    /// between two areas should do nothing rather than guess.
    /// </summary>
    public int? IndexAt(int yDp)
    {
        for (var i = 0; i < tabs.Count; i++)
        {
            if (yDp >= tabs[i].TopDp && yDp < tabs[i].BottomDp)
            {
                return i;
            }
        }

        return null;
    }
}

/// <summary>
/// Where the binder tabs go (DESIGN.md §9) — the right-edge divider tabs that are
/// Score's app frame. Pure, so the arithmetic that decides whether six areas fit the
/// gutter is testable without a device; the view is a thin painter.
///
/// Two rules shape it. Every tab clears §5's 48dp touch minimum, without exception —
/// past the point where they all fit, the strip <b>scrolls</b> rather than shrinking
/// them, which is why six is the practical ceiling. And the strip is <b>top-aligned</b>
/// under the header rather than centred in the well: a centred strip shifts every tab
/// whenever an area is added or removed, while a top-aligned one keeps each tab where
/// the hand last found it, which is the entire point of reaching for a divider.
/// </summary>
public static class BinderTabLayout
{
    /// <summary>The height a tab prefers when there is room for it.</summary>
    public const int PreferredHeightDp = 66;

    /// <summary>The breathing room between tabs.</summary>
    public const int GapDp = DesignTokens.Space.X2;

    public static BinderTabStrip Compute(int count, int wellHeightDp)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "A binder cannot have a negative number of tabs.");
        }

        if (count == 0)
        {
            return new BinderTabStrip([], scrolls: false);
        }

        var gaps = GapDp * (count - 1);
        var preferred = (PreferredHeightDp * count) + gaps;

        // Shrink toward the touch minimum before giving up and scrolling, so five
        // slightly shorter tabs beat five that need a swipe to reach.
        var available = Math.Max(wellHeightDp, 0);
        var height = preferred <= available
            ? PreferredHeightDp
            : Math.Max(DesignTokens.Shape.TouchTargetDp, (available - gaps) / count);

        var used = (height * count) + gaps;
        var scrolls = used > available;

        var tabs = new BinderTab[count];
        for (var i = 0; i < count; i++)
        {
            tabs[i] = new BinderTab(i * (height + GapDp), height);
        }

        return new BinderTabStrip(tabs, scrolls);
    }
}
