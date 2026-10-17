namespace Constitution.Domain.Scraps;

/// <summary>
/// The paper around one kind of scrap and where its tape goes, from the mockup CSS. Padding and
/// border are what the box adds to the content the view measured; margins are the space it asks of
/// its neighbours; the tape is placed by a fraction of the width, its top offset from the box, and
/// its own turn.
/// </summary>
/// <param name="PaddingTopDp">Space above the content inside the box.</param>
/// <param name="PaddingBottomDp">Space below the content inside the box.</param>
/// <param name="PaddingStartDp">Space left of the content, for the painter to measure against.</param>
/// <param name="PaddingEndDp">Space right of the content, for the painter to measure against.</param>
/// <param name="BorderVerticalDp">The border's thickness top and bottom together.</param>
/// <param name="MarginTopDp">Space asked above the box.</param>
/// <param name="MarginBottomDp">Space asked below the box.</param>
/// <param name="TapeWidthDp">The tape strip's width.</param>
/// <param name="TapeCentreFraction">Where along the box's width the tape's centre sits, 0 to 1.</param>
/// <param name="TapeTopDp">The tape's top edge relative to the box's top (negative stands proud of it).</param>
/// <param name="TapeTurnDegrees">The tape's own turn, before the scrap's tilt is added.</param>
public sealed record ScrapStyle(
    double PaddingTopDp,
    double PaddingBottomDp,
    double PaddingStartDp,
    double PaddingEndDp,
    double BorderVerticalDp,
    double MarginTopDp,
    double MarginBottomDp,
    double TapeWidthDp,
    double TapeCentreFraction,
    double TapeTopDp,
    double TapeTurnDegrees)
{
    /// <summary>The CSS's default tape (<c>.scrap .tape</c>, line 204): 78 wide, centred, 11 proud, turned -3.</summary>
    private const double DefaultTapeWidth = 78;
    private const double DefaultTapeTop = -11;
    private const double DefaultTapeTurn = -3;

    private static readonly ScrapStyle Plain = new(
        PaddingTopDp: 14, PaddingBottomDp: 12, PaddingStartDp: 16, PaddingEndDp: 16,
        BorderVerticalDp: 2,
        MarginTopDp: ScrapMetrics.MarginTopDp, MarginBottomDp: ScrapMetrics.MarginBottomDp,
        TapeWidthDp: DefaultTapeWidth, TapeCentreFraction: 0.5, TapeTopDp: DefaultTapeTop, TapeTurnDegrees: DefaultTapeTurn);

    /// <summary>The style for a kind. Line numbers are in the mockup CSS.</summary>
    public static ScrapStyle For(ScrapKind kind) => kind switch
    {
        // .scrap.mag (211): no border, padding 16 34 14 16; tape at 24%, turned 5 (207).
        ScrapKind.MagazineClipping => Plain with
        {
            PaddingTopDp = 16, PaddingBottomDp = 14, PaddingEndDp = 34, BorderVerticalDp = 0,
            TapeCentreFraction = 0.24, TapeTurnDegrees = 5,
        },

        // .scrap.index (217): padding-top 9; tape at 76%, 64 wide, turned -2 (208).
        ScrapKind.IndexCard => Plain with
        {
            PaddingTopDp = 9, TapeWidthDp = 64, TapeCentreFraction = 0.76, TapeTurnDegrees = -2,
        },

        // .scrap.note (197): padding-top 16, no top border; tape sits low, top -4 (206).
        ScrapKind.NotepadLeaf => Plain with
        {
            PaddingTopDp = 16, BorderVerticalDp = 1, TapeTopDp = -4,
        },

        // .scrap.receipt (201, 209): the default box; tape at 30%, turned 3.
        ScrapKind.Receipt => Plain with
        {
            TapeCentreFraction = 0.3, TapeTurnDegrees = 3,
        },

        // .scrap.chart (321): no border, padding 10 12 16, margin-top 18; the default tape.
        ScrapKind.ChartScrap => Plain with
        {
            PaddingTopDp = 10, PaddingBottomDp = 16, PaddingStartDp = 12, PaddingEndDp = 12,
            BorderVerticalDp = 0, MarginTopDp = 18,
        },

        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A new scrap kind needs its paper read off the mockups."),
    };
}
