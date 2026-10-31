using Android.Graphics;
using Codex.Ui;
using Constitution.Domain.Scrapbook;
using Constitution.Domain.Scraps;
using Constitution.Scrapbook;
using Storyworthtelling.Paper;

namespace Constitution.WorkoutsTab;

/// <summary>
/// The paper of the Workouts tab's clippings. Today's is the shared magazine clipping, untouched; each later day is the same
/// stock cut smaller (`.scrap.mag.small`), which has no sliver of the next column and no page number, so it is drawn here
/// from the same tokens and the same scissor-cut edge. A pressed clipping takes the state matrix's inset paper (DESIGN.md
/// section 8): a hairline just inside the cut and a pale band inside that, no change of colour.
/// </summary>
internal sealed class ClippingPaper
{
    private const float EdgeDp = 1.5f;
    private const float InsetBandDp = 4f;
    private const float DotDiameterDp = 1.3f;
    private const decimal InsetBandAlpha = 0.9m;

    private readonly bool isLarge;
    private readonly MagazineClippingPainter large = new();
    private readonly Paint stock = ScrapInk.Fill(ScrapInk.MagazineStock);
    private readonly Paint edge = ScrapInk.Stroke(ScrapInk.MagazineEdge, EdgeDp * 2);
    private readonly Paint dots = ScrapInk.Fill(ScrapInk.HalftoneInk, ScrapInk.Alpha(MaterialTokens.HalftoneAlpha));
    private readonly Paint pressedRule = ScrapInk.Stroke(ScrapInk.MagazineEdge, EdgeDp);
    private readonly Paint pressedBand = ScrapInk.Stroke(PaperTheme.PaperDeep, InsetBandDp, ScrapInk.Alpha(InsetBandAlpha));

    public ClippingPaper(bool isLarge)
    {
        this.isLarge = isLarge;
        dots.StrokeWidth = DotDiameterDp;
        dots.StrokeCap = Paint.Cap.Round;
    }

    /// <summary>Draws the paper into a canvas already in dp with its origin at the clipping's top-left.</summary>
    public void Draw(Canvas canvas, ScrapSurface surface, bool pressed)
    {
        if (isLarge)
        {
            large.DrawPaper(canvas, surface);
        }
        else
        {
            DrawSmall(canvas, surface);
        }

        if (pressed)
        {
            DrawInset(canvas, surface);
        }
    }

    private void DrawSmall(Canvas canvas, ScrapSurface surface)
    {
        var paper = ScrapBox.Paper(ScrapKind.MagazineClipping, surface.WidthDp, surface.HeightDp);
        var right = (float)(paper.X + paper.Width);
        var bottom = (float)(paper.Y + paper.Height);
        canvas.Save();
        canvas.ClipPath(CutPaths.For(ScrapKind.MagazineClipping, paper));
        canvas.DrawRect((float)paper.X, (float)paper.Y, right, bottom, stock);
        var pitch = (float)MaterialTokens.HalftonePitchDp;
        var points = new List<float>();
        for (var y = (float)paper.Y + (pitch / 2); y < bottom; y += pitch)
        {
            for (var x = (float)paper.X + (pitch / 2); x < right; x += pitch)
            {
                points.Add(x);
                points.Add(y);
            }
        }

        canvas.DrawPoints(points.ToArray(), dots);
        canvas.DrawRect((float)paper.X, (float)paper.Y, right, bottom, edge);
        canvas.Restore();
    }

    private void DrawInset(Canvas canvas, ScrapSurface surface)
    {
        var paper = ScrapBox.Paper(ScrapKind.MagazineClipping, surface.WidthDp, surface.HeightDp);
        canvas.Save();
        canvas.ClipPath(CutPaths.For(ScrapKind.MagazineClipping, paper));
        var left = (float)paper.X;
        var top = (float)paper.Y;
        var right = (float)(paper.X + paper.Width);
        var bottom = (float)(paper.Y + paper.Height);
        canvas.DrawRect(left + (InsetBandDp / 2), top + (InsetBandDp / 2), right - (InsetBandDp / 2), bottom - (InsetBandDp / 2), pressedBand);
        canvas.DrawRect(left + (EdgeDp / 2), top + (EdgeDp / 2), right - (EdgeDp / 2), bottom - (EdgeDp / 2), pressedRule);
        canvas.Restore();
    }
}
