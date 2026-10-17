using Android.Graphics;
using Path = Android.Graphics.Path;
using Codex.Ui;
using Constitution.Domain.Scrapbook;
using Constitution.Domain.Scraps;
using Storyworthtelling.Paper;

namespace Constitution.Scrapbook;

/// <summary>
/// The index card (`.scrap.index`): Index's own card, drawn from the same shared tokens, so it is
/// one object in both apps. Note-card stock, a hairline edge and a 3 dp corner, a red rule closing
/// the label row, and pale body rules every 22 dp that the lines of text are written ON.
/// </summary>
public sealed class IndexCardPainter : IScrapPainter
{
    /// <summary>The card's corner (`border-radius: 3px`), a flat sheet of stock rather than a rounded panel.</summary>
    private const float CornerDp = 3;

    private const float HairlineDp = 1;

    private readonly Paint stock = ScrapInk.Fill(ScrapInk.NoteCard);
    private readonly Paint headRule = ScrapInk.Fill(ScrapInk.CardHeadRule);
    private readonly Paint bodyRule = ScrapInk.Fill(ScrapInk.CardBodyRule);
    private readonly Paint border = ScrapInk.Stroke(PaperTheme.Hairline, HairlineDp);

    public ScrapKind Kind => ScrapKind.IndexCard;

    public void DrawPaper(Canvas canvas, ScrapSurface surface)
    {
        var width = surface.WidthDp;
        var height = surface.HeightDp;
        canvas.DrawRoundRect(0, 0, width, height, CornerDp, CornerDp, stock);

        var inset = (float)ScrapBox.Border(Kind).Left;
        var clip = new Path();
        clip.AddRoundRect(inset, inset, width - inset, height - inset, CornerDp, CornerDp, Path.Direction.Cw!);
        canvas.Save();
        canvas.ClipPath(clip);

        var headTop = (float)ScrapRuling.IndexHeadRuleTop();
        canvas.DrawRect(inset, headTop, width - inset, headTop + (float)ScrapRuling.RuleThicknessDp, headRule);
        foreach (var top in ScrapRuling.IndexBodyRuleTops(height, (double)MaterialTokens.CardRulePitchDp))
        {
            canvas.DrawRect(inset, (float)top, width - inset, (float)top + (float)ScrapRuling.RuleThicknessDp, bodyRule);
        }

        canvas.Restore();

        var half = HairlineDp / 2;
        canvas.DrawRoundRect(half, half, width - half, height - half, CornerDp, CornerDp, border);
    }
}
