using Android.Graphics;
using Codex.Ui;
using Constitution.Domain.Scrapbook;
using Constitution.Domain.Scraps;
using Storyworthtelling.Paper;

namespace Constitution.Scrapbook;

/// <summary>
/// The doctor's-chart scrap (`.scrap.chart`, D44): white chart paper gridded in the Reference
/// role's mark at 10%, torn along an uneven foot, with a hairline just inside the tear. The form
/// head across the top ("SIGNAL SLEEP, UNITS HOURS") is content (see <see cref="ScrapContent.FormHead"/>).
/// </summary>
public sealed class ChartScrapPainter : IScrapPainter
{
    private const float GridLineDp = 1;

    /// <summary>The rule just inside the torn paper, drawn as a 2 dp line half of which is clipped away.</summary>
    private const float EdgeInsetDp = 1;

    private readonly Paint paperFill = ScrapInk.Fill(ScrapInk.ChartPaper);
    private readonly Paint grid = ScrapInk.Fill(ScrapInk.ChartGrid, ScrapInk.Alpha(MaterialTokens.ChartGridAlpha));
    private readonly Paint edge = ScrapInk.Stroke(PaperTheme.Hairline, EdgeInsetDp * 2);

    public ScrapKind Kind => ScrapKind.ChartScrap;

    public void DrawPaper(Canvas canvas, ScrapSurface surface)
    {
        var paper = ScrapBox.Paper(Kind, surface.WidthDp, surface.HeightDp);
        var left = (float)paper.X;
        var top = (float)paper.Y;
        var right = (float)(paper.X + paper.Width);
        var bottom = (float)(paper.Y + paper.Height);
        var pitch = (float)MaterialTokens.ChartGridPitchDp;

        canvas.Save();
        canvas.ClipPath(CutPaths.For(Kind, paper));
        canvas.DrawRect(left, top, right, bottom, paperFill);
        for (var x = left; x < right; x += pitch)
        {
            canvas.DrawRect(x, top, x + GridLineDp, bottom, grid);
        }

        for (var y = top; y < bottom; y += pitch)
        {
            canvas.DrawRect(left, y, right, y + GridLineDp, grid);
        }

        canvas.DrawRect(left, top, right, bottom, edge);
        canvas.Restore();
    }
}
