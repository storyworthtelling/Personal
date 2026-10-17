using Android.Graphics;
using Codex.Ui;
using Constitution.Domain.Scrapbook;
using Constitution.Domain.Scraps;

namespace Constitution.Scrapbook;

/// <summary>
/// The receipt (`.scrap.receipt`, D46): white stock with a serrated foot, inside the same hairline
/// every plain scrap has. Its text is mono (the content sets that, see <see cref="ScrapContent"/>).
/// </summary>
public sealed class ReceiptPainter : IScrapPainter
{
    private const float HairlineDp = 1;

    private readonly Paint slip = ScrapInk.Fill(ScrapInk.Receipt);
    private readonly Paint border = ScrapInk.Stroke(PaperTheme.Hairline, HairlineDp);

    public ScrapKind Kind => ScrapKind.Receipt;

    public void DrawPaper(Canvas canvas, ScrapSurface surface)
    {
        var paper = ScrapBox.Paper(Kind, surface.WidthDp, surface.HeightDp);

        canvas.Save();
        canvas.ClipPath(CutPaths.For(Kind, paper));
        canvas.DrawRect((float)paper.X, (float)paper.Y, (float)(paper.X + paper.Width), (float)(paper.Y + paper.Height), slip);
        canvas.Restore();

        var half = HairlineDp / 2;
        canvas.DrawRect(half, half, surface.WidthDp - half, surface.HeightDp - half, border);
    }
}
