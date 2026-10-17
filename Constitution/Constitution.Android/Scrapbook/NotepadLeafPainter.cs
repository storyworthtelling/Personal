using Android.Graphics;
using Codex.Ui;
using Constitution.Domain.Scrapbook;
using Constitution.Domain.Scraps;
using Storyworthtelling.Paper;

namespace Constitution.Scrapbook;

/// <summary>
/// The notepad leaf (`.scrap.note`): the card's cream, torn along the top, ruled every 24 dp, with
/// a hairline down both sides and along the foot but none on top, where it was torn off the pad.
/// </summary>
public sealed class NotepadLeafPainter : IScrapPainter
{
    private const float HairlineDp = 1;

    private readonly Paint leaf = ScrapInk.Fill(ScrapInk.NotepadLeaf);
    private readonly Paint rule = ScrapInk.Fill(ScrapInk.NotepadRule);
    private readonly Paint border = ScrapInk.Fill(PaperTheme.Hairline);

    public ScrapKind Kind => ScrapKind.NotepadLeaf;

    public void DrawPaper(Canvas canvas, ScrapSurface surface)
    {
        var paper = ScrapBox.Paper(Kind, surface.WidthDp, surface.HeightDp);
        var left = (float)paper.X;
        var right = (float)(paper.X + paper.Width);

        canvas.Save();
        canvas.ClipPath(CutPaths.For(Kind, paper));
        canvas.DrawRect(left, (float)paper.Y, right, (float)(paper.Y + paper.Height), leaf);
        foreach (var top in ScrapRuling.NotepadRuleTops(surface.HeightDp, MaterialTokens.NotepadRulePitchDp))
        {
            canvas.DrawRect(left, (float)(paper.Y + top), right, (float)(paper.Y + top + ScrapRuling.RuleThicknessDp), rule);
        }

        canvas.Restore();

        // The border element is not clipped by the tear: the sides run the full height, the top is bare.
        canvas.DrawRect(0, 0, HairlineDp, surface.HeightDp, border);
        canvas.DrawRect(surface.WidthDp - HairlineDp, 0, surface.WidthDp, surface.HeightDp, border);
        canvas.DrawRect(0, surface.HeightDp - HairlineDp, surface.WidthDp, surface.HeightDp, border);
    }
}
