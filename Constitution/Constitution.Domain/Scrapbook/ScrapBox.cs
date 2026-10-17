using Constitution.Domain.Scraps;

namespace Constitution.Domain.Scrapbook;

/// <summary>Space on each side of a box, in dp.</summary>
public readonly record struct BoxInsets(double Left, double Top, double Right, double Bottom);

/// <summary>A rectangle in a scrap's own space (origin at its top-left), in dp.</summary>
public readonly record struct PaperRect(double X, double Y, double Width, double Height);

/// <summary>
/// Where a scrap's border, paper and content sit inside the box <see cref="ScrapLayout"/>
/// decided. The layout adds padding and border to the content the view measured, but
/// <see cref="ScrapStyle"/> only totals the border top and bottom; this says which sides carry
/// it, so the view lays the content out exactly where the layout assumed it.
///
/// <para>From the mockup CSS: <c>.scrap</c> has a 1 px hairline border all round; the clipping
/// and the chart have none (<c>border:none</c>); the notepad leaf has none on top
/// (<c>border-top:none</c>), which is where it is torn.</para>
/// </summary>
public static class ScrapBox
{
    private const double Hairline = 1;

    /// <summary>The hairline border's thickness on each side.</summary>
    public static BoxInsets Border(ScrapKind kind) => kind switch
    {
        ScrapKind.MagazineClipping => new BoxInsets(0, 0, 0, 0),
        ScrapKind.ChartScrap => new BoxInsets(0, 0, 0, 0),
        ScrapKind.NotepadLeaf => new BoxInsets(Hairline, 0, Hairline, Hairline),
        ScrapKind.IndexCard => new BoxInsets(Hairline, Hairline, Hairline, Hairline),
        ScrapKind.Receipt => new BoxInsets(Hairline, Hairline, Hairline, Hairline),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A new scrap kind needs its border read off the mockups."),
    };

    /// <summary>Border plus padding: how far in from the box's edges the content starts and ends.</summary>
    public static BoxInsets Content(ScrapKind kind)
    {
        var border = Border(kind);
        var style = ScrapStyle.For(kind);
        return new BoxInsets(
            border.Left + style.PaddingStartDp,
            border.Top + style.PaddingTopDp,
            border.Right + style.PaddingEndDp,
            border.Bottom + style.PaddingBottomDp);
    }

    /// <summary>The paper's rectangle: the box inside its border, which is what the cut edge is applied to.</summary>
    public static PaperRect Paper(ScrapKind kind, double width, double height)
    {
        var border = Border(kind);
        return new PaperRect(
            border.Left,
            border.Top,
            width - border.Left - border.Right,
            height - border.Top - border.Bottom);
    }
}
