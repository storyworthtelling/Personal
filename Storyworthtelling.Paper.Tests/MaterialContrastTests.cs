using SkiaSharp;
using Storyworthtelling.Paper;
using Xunit;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// §17 over the drawn furniture, and the two combinations that are deliberately unusable.
///
/// <para>The interesting half of this file is the second one. Muted ink on the desk measures 1.40:1
/// and cream on bronze 2.80:1 — both far under the floor — and neither is a value to darken away.
/// The design already accounts for them: <b>every small label lies on a paper note</b>, which is
/// exactly what makes the contrast legal, and nothing is ever lettered on a bookend. Recording the
/// two failures here turns "we know not to do that" into something that fails a build, so a later
/// screen that letters straight onto the wood is caught rather than shipped unreadable.</para>
/// </summary>
public class MaterialContrastTests
{
    private const double AA = 4.5;
    private const double LargeOrUi = 3.0;

    public static TheoryData<string, string, string, double> Used =>
        new()
        {
            // A book's details, written on the note card that lies on the desk.
            { "Ink on the note card", PaperColorTokens.Ink, MaterialTokens.NoteCardHex, AA },
            { "InkSoft on the note card", PaperColorTokens.InkSoft, MaterialTokens.NoteCardHex, AA },
            { "InkMuted on the note card", PaperColorTokens.InkMuted, MaterialTokens.NoteCardHex, LargeOrUi },

            // A passing state, stuck on a post-it.
            { "InkSoft on the post-it", PaperColorTokens.InkSoft, MaterialTokens.PostItHex, AA },
            { "Ink on the post-it", PaperColorTokens.Ink, MaterialTokens.PostItHex, AA },
            { "A problem state on the post-it", PaperColorTokens.ProblemText, MaterialTokens.PostItHex, AA },
            { "A positive state on the post-it", PaperColorTokens.PositiveText, MaterialTokens.PostItHex, AA },
        };

    [Theory]
    [MemberData(nameof(Used))]
    public void EveryCombinationTheFurnitureActuallyUsesClearsTheFloor(
        string what, string ink, string ground, double floor)
    {
        var ratio = Contrast(SKColor.Parse(ink), SKColor.Parse(ground));
        Assert.True(ratio >= floor, $"{what} is {ratio:F2}:1, under the {floor:F1}:1 §17 requires.");
    }

    /// <summary>
    /// The reason the notes exist. If this ever starts passing, the desk has been repainted and the
    /// note cards may no longer be necessary — which is a design decision, not a silent improvement.
    /// </summary>
    [Fact]
    public void InkOnTheBareDeskIsUnreadable_WhichIsWhyLabelsLieOnPaper()
    {
        var ratio = Contrast(SKColor.Parse(PaperColorTokens.InkMuted), SKColor.Parse(MaterialTokens.DeskWoodLightHex));

        Assert.True(ratio < LargeOrUi,
            $"Muted ink on the desk now measures {ratio:F2}:1, which clears §17. The wood has been "
            + "repainted: the rule that every label lies on a paper note should be revisited rather "
            + "than left as folklore.");
    }

    /// <summary>Nothing is lettered on a bookend or a ledge, and this is why.</summary>
    [Fact]
    public void CreamOnBronzeIsUnreadable_WhichIsWhyBookendsCarryNoLabel()
    {
        var ratio = Contrast(SKColor.Parse(PaperColorTokens.Paper), SKColor.Parse(MaterialTokens.BronzeHex));

        Assert.True(ratio < LargeOrUi,
            $"Cream on bronze now measures {ratio:F2}:1. If the bronze has been darkened, a bookend "
            + "could carry a series name — but that is a decision to take deliberately.");
    }

    // The two papers have to read as different pieces of stationery, or the distinction the design
    // rests on is invisible.
    [Fact]
    public void TheNoteCardAndThePostItAreTellableApart()
    {
        var ratio = Contrast(SKColor.Parse(MaterialTokens.NoteCardHex), SKColor.Parse(MaterialTokens.PostItHex));
        Assert.True(ratio > 1.05, $"The note card and the post-it are {ratio:F3}:1 apart — the same paper.");
    }

    private static double Contrast(SKColor a, SKColor b)
    {
        var (la, lb) = (RelativeLuminance(a), RelativeLuminance(b));
        var (hi, lo) = la >= lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double RelativeLuminance(SKColor c)
    {
        double Ch(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Ch(c.Red)) + (0.7152 * Ch(c.Green)) + (0.0722 * Ch(c.Blue));
    }
}
