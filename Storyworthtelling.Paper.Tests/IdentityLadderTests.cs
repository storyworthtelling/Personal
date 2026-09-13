using Storyworthtelling.Paper;
using Xunit;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// G10 — Folio's identity ladder stays in its window.
///
/// <para><b>The window is 5° wide and it is the only one Folio fits.</b> DESIGN.md §3 binds an
/// identity against every chromatic role by 20°, and by gate 3 the wheel was nearly full: clearing
/// 20° from every role <i>and</i> every committed app identity leaves four gaps in the whole circle,
/// two of them too narrow to use. Folio takes the olive at <b>107°</b>, the centre of 105–109.</para>
///
/// <para>The art it was sampled from measures 105.4° — a pass against the caution ochre by 20.4°,
/// but a thin one. Committing the ladder at the centre of the window rather than at the measurement
/// is what stops a re-crop or a regenerated source drifting it under the binding, which would break
/// the build with nothing visibly different on screen.</para>
///
/// <para><b>The binding is not the identity.</b> Folio's cloth is a red that sits about 3° from the
/// problem rose — disqualifying for an identity token, and perfectly fine for a binding, which §12
/// does not require to match. So this guard reads the ladder and never the cloth; sampling identity
/// from the binding is Post's exception, taken because Post's art was unusable.</para>
/// </summary>
public class IdentityLadderTests
{
    private const double RequiredSeparation = 20.0;

    /// <summary>The committed hue: the centre of the window, not the art's measurement.</summary>
    private const double CommittedHue = 107.0;

    public static TheoryData<string, string> Ladder =>
        new()
        {
            { "Fill", AppIdentityTokens.FolioFillHex },
            { "Ground", AppIdentityTokens.FolioGroundHex },
            { "Mark", AppIdentityTokens.FolioMarkHex },
            { "Text", AppIdentityTokens.FolioTextHex },
            { "Deep", AppIdentityTokens.FolioDeepHex },
        };

    [Theory]
    [MemberData(nameof(Ladder))]
    public void EveryTierClearsEveryChromaticRole(string tier, string hex)
    {
        var hue = Oklch.FromHex(hex).HueDegrees;

        foreach (var (role, roleHex) in ChromaticRoles())
        {
            var separation = Oklch.HueSeparation(hue, Oklch.FromHex(roleHex).HueDegrees);
            Assert.True(separation >= RequiredSeparation,
                $"Folio's {tier} ({hex}) is {separation:F1}° from {role} ({roleHex}) — under the {RequiredSeparation}° "
                + "DESIGN.md §3 binds an identity to. The window Folio fits is 105–109; there is not another one.");
        }
    }

    // One ladder, one hue. A tier that drifts is the failure this is for — the tiers differ in
    // lightness and chroma, never in hue.
    [Theory]
    [MemberData(nameof(Ladder))]
    public void EveryTierSitsOnTheCommittedHue(string tier, string hex)
    {
        var separation = Oklch.HueSeparation(Oklch.FromHex(hex).HueDegrees, CommittedHue);
        Assert.True(separation <= 2.5,
            $"Folio's {tier} ({hex}) is {separation:F1}° off the committed {CommittedHue}°. The ladder is one hue.");
    }

    [Fact]
    public void TheLadderDescendsInLightness()
    {
        var lightness = new[]
        {
            AppIdentityTokens.FolioFillHex, AppIdentityTokens.FolioGroundHex, AppIdentityTokens.FolioMarkHex,
            AppIdentityTokens.FolioTextHex, AppIdentityTokens.FolioDeepHex,
        }.Select(hex => Oklch.FromHex(hex).L).ToList();

        for (var i = 1; i < lightness.Count; i++)
        {
            Assert.True(lightness[i] < lightness[i - 1],
                $"The ladder stops descending at tier {i}: {lightness[i]:F3} is not below {lightness[i - 1]:F3}.");
        }
    }

    /// <summary>
    /// The binding is a separate decision and deliberately outside the rule above. Asserted so that
    /// its closeness to the problem rose is a recorded fact rather than something rediscovered later
    /// and "fixed" by dragging the identity onto it.
    /// </summary>
    [Fact]
    public void TheBindingIsNotOnTheIdentityHue()
    {
        var separation = Oklch.HueSeparation(
            Oklch.FromHex(AppIdentityTokens.FolioClothHex).HueDegrees, CommittedHue);

        Assert.True(separation > RequiredSeparation,
            "Folio's cloth has drifted onto its identity hue. The binding is not required to match it "
            + "(§12), and this one deliberately does not: it is a red, and the identity is an olive.");
    }

    private static IEnumerable<(string Role, string Hex)> ChromaticRoles() =>
    [
        ("Action", PaperColorTokens.ActionMark),
        ("Caution", PaperColorTokens.CautionMark),
        ("Problem", PaperColorTokens.ProblemMark),
        ("Positive", PaperColorTokens.PositiveMark),
        ("Reference", PaperColorTokens.ReferenceMark),
        ("Progress", PaperColorTokens.ProgressMark),
    ];
}
