using Codex.Domain.Design;
using Storyworthtelling.Paper;
using Xunit;

namespace Codex.Tests.Design;

/// <summary>
/// Codex's <see cref="DesignTokens"/> re-export the shared P4 palette — the generated
/// <see cref="PaperColorTokens"/> in Storyworthtelling.Paper. The pure Domain can't take a
/// runtime reference on Paper (it carries SkiaSharp/PDFsharp), so this TEST-only reference is
/// the seam that keeps Paper the single source of truth: if the generated palette moves, this
/// fails until the committed Codex tokens are re-synced. Terracotta is retired here too.
/// </summary>
public class PaperReExportParityTests
{
    [Fact]
    public void NeutralsAndInksMatchPaper()
    {
        Assert.Equal(PaperColorTokens.Paper, DesignTokens.Paper);
        Assert.Equal(PaperColorTokens.PaperBright, DesignTokens.PaperBright);
        Assert.Equal(PaperColorTokens.PaperDeep, DesignTokens.PaperDeep);
        Assert.Equal(PaperColorTokens.PaperShade, DesignTokens.PaperShade);
        Assert.Equal(PaperColorTokens.Hairline, DesignTokens.Hairline);
        Assert.Equal(PaperColorTokens.Ink, DesignTokens.Ink);
        Assert.Equal(PaperColorTokens.InkSoft, DesignTokens.InkSoft);
        Assert.Equal(PaperColorTokens.InkMuted, DesignTokens.InkMuted);
        Assert.Equal(PaperColorTokens.InkBlue, DesignTokens.InkBlue);
    }

    [Fact]
    public void EveryRoleTierMatchesPaperAppProfile()
    {
        Assert.Equal(PaperColorTokens.ActionFill, DesignTokens.ActionFill);
        Assert.Equal(PaperColorTokens.ActionMark, DesignTokens.ActionMark);
        Assert.Equal(PaperColorTokens.ActionText, DesignTokens.ActionText);
        Assert.Equal(PaperColorTokens.ActionDeep, DesignTokens.ActionDeep);

        Assert.Equal(PaperColorTokens.PositiveFill, DesignTokens.PositiveFill);
        Assert.Equal(PaperColorTokens.PositiveMark, DesignTokens.PositiveMark);
        Assert.Equal(PaperColorTokens.PositiveText, DesignTokens.PositiveText);
        Assert.Equal(PaperColorTokens.PositiveDeep, DesignTokens.PositiveDeep);

        Assert.Equal(PaperColorTokens.ResolvedFill, DesignTokens.ResolvedFill);
        Assert.Equal(PaperColorTokens.ResolvedMark, DesignTokens.ResolvedMark);
        Assert.Equal(PaperColorTokens.ResolvedText, DesignTokens.ResolvedText);
        Assert.Equal(PaperColorTokens.ResolvedDeep, DesignTokens.ResolvedDeep);

        Assert.Equal(PaperColorTokens.ReferenceFill, DesignTokens.ReferenceFill);
        Assert.Equal(PaperColorTokens.ReferenceMark, DesignTokens.ReferenceMark);
        Assert.Equal(PaperColorTokens.ReferenceText, DesignTokens.ReferenceText);
        Assert.Equal(PaperColorTokens.ReferenceDeep, DesignTokens.ReferenceDeep);

        Assert.Equal(PaperColorTokens.CautionFill, DesignTokens.CautionFill);
        Assert.Equal(PaperColorTokens.CautionMark, DesignTokens.CautionMark);
        Assert.Equal(PaperColorTokens.CautionText, DesignTokens.CautionText);
        Assert.Equal(PaperColorTokens.CautionDeep, DesignTokens.CautionDeep);

        Assert.Equal(PaperColorTokens.ProblemFill, DesignTokens.ProblemFill);
        Assert.Equal(PaperColorTokens.ProblemMark, DesignTokens.ProblemMark);
        Assert.Equal(PaperColorTokens.ProblemText, DesignTokens.ProblemText);
        Assert.Equal(PaperColorTokens.ProblemDeep, DesignTokens.ProblemDeep);

        Assert.Equal(PaperColorTokens.ProgressFill, DesignTokens.ProgressFill);
        Assert.Equal(PaperColorTokens.ProgressMark, DesignTokens.ProgressMark);
        Assert.Equal(PaperColorTokens.ProgressText, DesignTokens.ProgressText);
        Assert.Equal(PaperColorTokens.ProgressDeep, DesignTokens.ProgressDeep);
    }

    [Fact]
    public void AppAccentIsTheActionRoleNotTerracotta()
    {
        Assert.Equal(PaperColorTokens.ActionMark, DesignTokens.Accent);
        Assert.Equal(PaperColorTokens.ActionDeep, DesignTokens.AccentDeep);
        // PaperColorTokens.Accent/AccentDeep are the retired terracotta base values; the app
        // accent must no longer equal them (proves the retirement without a hex literal here).
        Assert.NotEqual(PaperColorTokens.Accent, DesignTokens.Accent);
        Assert.NotEqual(PaperColorTokens.AccentDeep, DesignTokens.AccentDeep);
    }
}
