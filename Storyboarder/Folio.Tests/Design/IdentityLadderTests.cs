using Storyworthtelling.Paper;
using Xunit;

namespace Folio.Tests.Design;

/// <summary>
/// G10 — Folio's five identity tiers stay clear of every semantic role, and of every other app's
/// identity.
///
/// <para><b>The bug this reintroduces is moving a tier toward caution.</b> DESIGN.md §3's rule is
/// that an identity hue is sampled from the art and then <i>checked</i> against the roles it will
/// sit near, and moved if it crowds one — because an identity that close reads as a status. A tab in
/// the caution ochre is not a tab any more; it is a warning about the screen behind it.</para>
///
/// <para><b>Why this guard is Folio's rather than the suite's.</b> The suite's copy lives in
/// FocusPhone's own test project and enumerates Flyleaf, Score, Post and Plate by hand — the
/// per-app ladders are hand-committed rather than emitted by <c>PaletteSpec</c>, so
/// generated-versus-committed parity does not reach them, and a ladder nobody adds to the list is
/// simply unguarded. Folio's was not on that list. It is now checked here, in the project the
/// ladder belongs to, which is also where a future app will look.</para>
///
/// <para><b>The second half the suite's copy does not do:</b> distance from every <i>other</i> app's
/// identity. Two apps on the same hue are two tabs that look like each other on one shelf, which is
/// a different failure from a tab that looks like a status, and nothing measured it before.</para>
///
/// <para>Measured at the time of writing: Folio's tightest tier sits <b>21.4°</b> from the Positive
/// role — clear of the 20° floor, and now the tightest ladder in the suite (Flyleaf's mark at 24°
/// was the previous record). That margin is the reason this guard is worth having rather than a
/// formality.</para>
/// </summary>
public class IdentityLadderTests
{
    /// <summary>
    /// The floor, and the exclusion. A near-neutral role at 0.010 chroma is a warm gray, and an
    /// angle measured off almost no chroma is not a hue anyone can see.
    /// </summary>
    private const double MinimumSeparation = 20.0;

    private const double NearNeutralChroma = 0.02;

    /// <summary>
    /// Folio's committed ladder: olive, OKLCH 107°, locked at gate 3. Written as literals on
    /// purpose — this guard exists to catch a value being changed, so reading the value from the
    /// same place the change would be made would catch nothing.
    /// </summary>
    public static TheoryData<string> FolioTiers =>
        ["#C1C197", "#919059", "#6B681E", "#575410", "#3B3902"];

    /// <summary>Every other app's committed identity, as DESIGN.md §3's table records them.</summary>
    private static readonly (string App, string Hex)[] Siblings =
    [
        ("Flyleaf", "#C9803A"), ("Flyleaf", "#B4552F"), ("Flyleaf", "#8A4A22"),
        ("Score", "#AFC0BC"), ("Score", "#93ABA5"), ("Score", "#436A61"),
        ("Score", "#35544D"), ("Score", "#263B36"),
        ("Post", "#B9B9D4"), ("Post", "#A1A1C7"), ("Post", "#5C5894"),
        ("Post", "#494676"), ("Post", "#333252"),
        ("Plate", "#C8B5C8"), ("Plate", "#B69BB6"), ("Plate", "#7A4F7B"),
        ("Plate", "#613F62"), ("Plate", "#442D44"),
        ("Pulp", "#CEB7A3"), ("Pulp", "#BF9E82"), ("Pulp", "#86541F"),
        ("Pulp", "#6B4219"), ("Pulp", "#4B3015"), ("Pulp", "#9B9A55"),
    ];

    [Theory]
    [MemberData(nameof(FolioTiers))]
    public void NoTierCrowdsASemanticRole(string tier)
    {
        var (_, _, hue) = Oklch.FromHex(tier);

        foreach (var (role, (roleHue, roleChroma)) in PaletteSpec.RoleWheel)
        {
            if (roleChroma < NearNeutralChroma)
            {
                continue;
            }

            var separation = Oklch.HueSeparation(hue, roleHue);
            Assert.True(separation >= MinimumSeparation,
                $"{tier} sits {separation:F1}° from the {role} role (minimum {MinimumSeparation}) — "
                + "an identity that close reads as a status rather than as an app.");
        }
    }

    [Theory]
    [MemberData(nameof(FolioTiers))]
    public void NoTierCrowdsAnotherAppsIdentity(string tier)
    {
        var (_, _, hue) = Oklch.FromHex(tier);

        foreach (var (app, sibling) in Siblings)
        {
            var (_, _, siblingHue) = Oklch.FromHex(sibling);
            var separation = Oklch.HueSeparation(hue, siblingHue);

            Assert.True(separation >= MinimumSeparation,
                $"{tier} sits {separation:F1}° from {app}'s {sibling} (minimum {MinimumSeparation}) — "
                + "two apps on one hue are two tabs that look like each other on one shelf.");
        }
    }

    /// <summary>
    /// The five tiers are one ladder, not five samples: they share a hue and differ in lightness.
    /// A tier that drifted in hue would still pass the two guards above while no longer belonging
    /// to the family it is named for.
    /// </summary>
    [Fact]
    public void TheLadderIsOneHue()
    {
        var hues = new[] { "#C1C197", "#919059", "#6B681E", "#575410", "#3B3902" }
            .Select(tier => Oklch.FromHex(tier).HueDegrees).ToList();

        Assert.True(hues.Max() - hues.Min() < 2.0,
            $"The ladder spans {hues.Max() - hues.Min():F1}° of hue ({hues.Min():F1}–{hues.Max():F1}). "
            + "Five tiers of one identity are one hue at five lightnesses.");
    }
}
