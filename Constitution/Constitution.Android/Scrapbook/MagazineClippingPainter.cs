using Android.Graphics;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Scrapbook;
using Constitution.Domain.Scraps;
using Storyworthtelling.Paper;

namespace Constitution.Scrapbook;

/// <summary>
/// The magazine clipping (`.scrap.mag`, book p.11): a brighter stock cut with a wandering scissor,
/// a fine halftone dot screen, a 1.5 dp rule just inside the cut so the clipping stands off the
/// ground, a sliver of the next column left on where the scissors strayed, and a printed folio.
/// The heavy kicker rule and the Serif Black head are the content (see <see cref="ScrapContent"/>).
/// </summary>
public sealed class MagazineClippingPainter : IScrapPainter
{
    /// <summary>The rule just inside the cut (`box-shadow: inset 0 0 0 1.5px`): drawn as a 3 dp line, half of it outside the clip.</summary>
    private const float EdgeInsetDp = 1.5f;

    /// <summary>A halftone dot is about 1.3 dp across (`radial-gradient(.. .6px, transparent .7px)`).</summary>
    private const float DotDiameterDp = 1.3f;

    // The next column (`.scrap.mag .col`): 26 dp wide, 3 dp in from the right, 14 dp off the top and foot,
    // ruled as 2 dp lines every 7 dp starting after 5, with a hairline on its left. Its two alphas are the
    // CSS's own (.28 and .18) and are not yet registered in MaterialTokens.
    private const float ColumnWidthDp = 26;
    private const float ColumnRightDp = 3;
    private const float ColumnInsetDp = 14;
    private const float ColumnLeadDp = 5;
    private const float ColumnLineDp = 2;
    private const float ColumnPitchDp = 7;
    private const decimal ColumnLineAlpha = 0.28m;
    private const decimal ColumnEdgeAlpha = 0.18m;

    // The folio (`.scrap.mag .folio`): 16 dp in from the left, baseline 6 dp above the foot.
    private const float FolioLeftDp = 16;
    private const float FolioBaselineDp = 6;

    private readonly Paint stock = ScrapInk.Fill(ScrapInk.MagazineStock);
    private readonly Paint edge = ScrapInk.Stroke(ScrapInk.MagazineEdge, EdgeInsetDp * 2);
    private readonly Paint dots = DotPaint();
    private readonly Paint columnLine = ScrapInk.Fill(ScrapInk.MagazineColumnInk, ScrapInk.Alpha(ColumnLineAlpha));
    private readonly Paint columnEdge = ScrapInk.Fill(ScrapInk.MagazineColumnInk, ScrapInk.Alpha(ColumnEdgeAlpha));
    private readonly Paint folio = ScrapInk.Fill(PaperTheme.InkMuted);

    public ScrapKind Kind => ScrapKind.MagazineClipping;

    public void DrawPaper(Canvas canvas, ScrapSurface surface)
    {
        var paper = ScrapBox.Paper(Kind, surface.WidthDp, surface.HeightDp);
        var right = (float)(paper.X + paper.Width);
        var bottom = (float)(paper.Y + paper.Height);

        canvas.Save();
        canvas.ClipPath(CutPaths.For(Kind, paper));
        canvas.DrawRect((float)paper.X, (float)paper.Y, right, bottom, stock);
        DrawHalftone(canvas, (float)paper.X, (float)paper.Y, right, bottom);
        canvas.DrawRect((float)paper.X, (float)paper.Y, right, bottom, edge);
        canvas.Restore();

        DrawColumn(canvas, surface);
        DrawFolio(canvas, surface);
    }

    private void DrawHalftone(Canvas canvas, float left, float top, float right, float bottom)
    {
        var pitch = (float)MaterialTokens.HalftonePitchDp;
        var points = new List<float>();
        for (var y = top + (pitch / 2); y < bottom; y += pitch)
        {
            for (var x = left + (pitch / 2); x < right; x += pitch)
            {
                points.Add(x);
                points.Add(y);
            }
        }

        canvas.DrawPoints(points.ToArray(), dots);
    }

    private void DrawColumn(Canvas canvas, ScrapSurface surface)
    {
        var right = surface.WidthDp - ColumnRightDp;
        var left = right - ColumnWidthDp;
        var top = ColumnInsetDp;
        var bottom = surface.HeightDp - ColumnInsetDp;
        if (bottom <= top)
        {
            return;
        }

        canvas.DrawRect(left, top, left + 1, bottom, columnEdge);
        for (var y = top + ColumnLeadDp; y < bottom; y += ColumnPitchDp)
        {
            canvas.DrawRect(left, y, right, Math.Min(y + ColumnLineDp, bottom), columnLine);
        }
    }

    private void DrawFolio(Canvas canvas, ScrapSurface surface)
    {
        if (string.IsNullOrEmpty(surface.Folio))
        {
            return;
        }

        // A canvas in dp: the registered printed ratio of the Label size, converted from sp to dp units.
        var density = surface.Context.Resources!.DisplayMetrics!.Density;
        folio.TextSize = PaperTheme.Sp(surface.Context, DesignTokens.TypeSp.Label * PaperTypeScale.Ratio) / density;
        folio.SetTypeface(PaperTheme.SerifRegular(surface.Context));
        canvas.DrawText(surface.Folio, FolioLeftDp, surface.HeightDp - FolioBaselineDp, folio);
    }

    private static Paint DotPaint()
    {
        var paint = ScrapInk.Fill(ScrapInk.HalftoneInk, ScrapInk.Alpha(MaterialTokens.HalftoneAlpha));
        paint.StrokeWidth = DotDiameterDp;
        paint.StrokeCap = Paint.Cap.Round;
        return paint;
    }
}
