using Android.Content;
using Android.Graphics;
using Constitution.Domain.Scraps;

namespace Constitution.Scrapbook;

/// <summary>
/// What a painter is handed to draw one scrap's paper: the size of the scrap's box in dp, the
/// folio a clipping prints (null for the kinds that have none), and a context for typefaces.
/// </summary>
public readonly record struct ScrapSurface(Context Context, float WidthDp, float HeightDp, string? Folio);

/// <summary>
/// The thin painter for one <see cref="ScrapKind"/> (C-3.2.3): it draws the paper, its cut edge and
/// its rules, and nothing else. Where the edges, rules and boxes fall is decided by the pure
/// <c>Constitution.Domain.Scrapbook</c> types and every colour comes from <c>MaterialTokens</c>, so
/// a painter is a loop over those numbers. It never draws a shadow: an edge, a tint and the tilt
/// are what separate a scrap from the page.
/// </summary>
public interface IScrapPainter
{
    /// <summary>The one kind of scrap this painter draws.</summary>
    ScrapKind Kind { get; }

    /// <summary>
    /// Draws the paper into <paramref name="canvas"/>, whose unit is already one dp and whose origin
    /// is the scrap's top-left corner. The content is not drawn here; it is a child view.
    /// </summary>
    void DrawPaper(Canvas canvas, ScrapSurface surface);
}
