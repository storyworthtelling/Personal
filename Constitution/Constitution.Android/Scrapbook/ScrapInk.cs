using Android.Graphics;
using Storyworthtelling.Paper;

namespace Constitution.Scrapbook;

/// <summary>
/// The scrapbook's inks, parsed once from the registered tokens in <c>MaterialTokens</c> (and the
/// suite's hairline from <c>PaperTheme</c>). Painters read colours from here and nowhere else;
/// <c>PainterTokensTests</c> fails on a colour built or parsed any other way.
/// </summary>
internal static class ScrapInk
{
    public static readonly Color CreaseInk = Color.ParseColor(MaterialTokens.CreaseInkHex);
    public static readonly Color TickInk = Color.ParseColor(MaterialTokens.TickInkHex);
    public static readonly Color PerforationInk = Color.ParseColor(MaterialTokens.PerforationInkHex);

    public static readonly Color Tape = Color.ParseColor(MaterialTokens.TapeHex);
    public static readonly Color TapeEdge = Color.ParseColor(MaterialTokens.TapeEdgeHex);

    public static readonly Color MagazineStock = Color.ParseColor(MaterialTokens.MagazineStockHex);
    public static readonly Color MagazineEdge = Color.ParseColor(MaterialTokens.MagazineEdgeHex);
    public static readonly Color HalftoneInk = Color.ParseColor(MaterialTokens.HalftoneInkHex);
    public static readonly Color MagazineColumnInk = Color.ParseColor(MaterialTokens.MagazineColumnInkHex);

    public static readonly Color NoteCard = Color.ParseColor(MaterialTokens.NoteCardHex);
    public static readonly Color CardHeadRule = Color.ParseColor(MaterialTokens.CardHeadRuleHex);
    public static readonly Color CardBodyRule = Color.ParseColor(MaterialTokens.CardBodyRuleHex);

    public static readonly Color NotepadLeaf = Color.ParseColor(MaterialTokens.NotepadLeafHex);
    public static readonly Color NotepadRule = Color.ParseColor(MaterialTokens.NotepadRuleHex);

    public static readonly Color Receipt = Color.ParseColor(MaterialTokens.ReceiptHex);

    public static readonly Color ChartPaper = Color.ParseColor(MaterialTokens.ChartPaperHex);
    public static readonly Color ChartGrid = Color.ParseColor(MaterialTokens.ChartGridHex);

    /// <summary>An alpha token (a fraction 0 to 1) as the 0 to 255 an Android paint takes.</summary>
    public static int Alpha(decimal fraction) => (int)Math.Round((double)fraction * 255);

    /// <summary>A flat fill, opaque unless <paramref name="alpha"/> says otherwise.</summary>
    public static Paint Fill(Color color, int alpha = 255)
    {
        var paint = new Paint { AntiAlias = true, Color = color, Alpha = alpha };
        paint.SetStyle(Paint.Style.Fill);
        return paint;
    }

    /// <summary>A line of <paramref name="widthDp"/> (the canvas is in dp, so the width is too).</summary>
    public static Paint Stroke(Color color, float widthDp, int alpha = 255)
    {
        var paint = new Paint { AntiAlias = true, Color = color, Alpha = alpha, StrokeWidth = widthDp };
        paint.SetStyle(Paint.Style.Stroke);
        return paint;
    }
}
