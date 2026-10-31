using System.Text.RegularExpressions;
using Storyworthtelling.Paper;
using Xunit;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// No two apps in the suite wear the same colour.
///
/// <para><b>DESIGN.md §3 binds an identity against the semantic roles, and says nothing about other
/// identities.</b> That is why nothing ever failed while Flyleaf and Foreword drifted to 5.9° apart —
/// both clear every role, so both are legal, and the rule that would have caught it does not exist.
/// It only shows when the two sit on one shelf, which is exactly where the owner sees them: adjacent
/// books, adjacent icons on a home screen, two covers on a desk.</para>
///
/// <para>So the pair is recorded here rather than corrected. Changing a shipped identity means
/// repainting an icon the owner recognises and a cover already printed, which is a decision, not a
/// tidy-up. What this guard buys is that it cannot happen <b>again</b>: every other pair clears 20°
/// with room to spare, and a seventh app has to clear them all.</para>
///
/// <para>Read from <c>Tools/IconStudio/Tokens.cs</c>, the registered source where identities are
/// chosen — and named one by one rather than found by pattern, so adding an app to this list is a
/// deliberate edit instead of something a regex quietly picks up or quietly misses.</para>
/// </summary>
public class IdentitySeparationTests
{
    private const double RequiredSeparation = 20.0;

    // The identity mark of each app. The constants are not uniformly named — Foreword's is its
    // sunrise — which is itself a reason to list them rather than match them.
    private static readonly (string App, string Constant)[] Identities =
    [
        ("Flyleaf", "FlyleafMarkHex"),
        ("Score", "ScoreMarkHex"),
        ("Post", "PostMarkHex"),
        ("Plate", "PlateMarkHex"),
        ("Foreword", "ForewordSunriseHex"),
        ("Folio", "FolioMarkHex"),
        ("Index", "IndexMarkHex"),
        ("Pulp", "PulpMarkHex"),
        ("Constitution", "ConstitutionMarkHex"),
    ];

    /// <summary>
    /// The one pair that predates the rule. Listed so that it is a known, accepted state rather than
    /// a failure nobody can act on — and so that anything NEW still fails.
    /// </summary>
    private static readonly HashSet<string> Recorded = ["Flyleaf|Foreword"];

    [Fact]
    public void EveryPairOfAppIdentitiesIsTellableApart()
    {
        var hues = Identities.ToDictionary(id => id.App, id => Oklch.FromHex(HexOf(id.Constant)).HueDegrees);

        var tooClose = new List<string>();
        for (var i = 0; i < Identities.Length; i++)
        {
            for (var j = i + 1; j < Identities.Length; j++)
            {
                var (a, b) = (Identities[i].App, Identities[j].App);
                var separation = Oklch.HueSeparation(hues[a], hues[b]);
                if (separation < RequiredSeparation && !Recorded.Contains($"{a}|{b}") && !Recorded.Contains($"{b}|{a}"))
                {
                    tooClose.Add($"{a} and {b} are {separation:F1}° apart");
                }
            }
        }

        Assert.True(tooClose.Count == 0,
            "Two apps in the suite are wearing near enough the same colour to be confused on one "
            + $"shelf. Every pair must clear {RequiredSeparation}°:\n" + string.Join("\n", tooClose));
    }

    private static readonly string[] ConstitutionTiers =
    [
        "ConstitutionFillHex", "ConstitutionGroundHex", "ConstitutionMarkHex",
        "ConstitutionTextHex", "ConstitutionDeepHex",
    ];

    /// <summary>
    /// Every Constitution tier, not only its mark, clears every other app's identity mark and every
    /// chromatic role by the floor. Constitution records no departure, so none is excepted.
    /// </summary>
    [Fact]
    public void EveryConstitutionTierClearsEverySiblingAndEveryChromaticRole()
    {
        var tooClose = new List<string>();
        foreach (var tier in ConstitutionTiers)
        {
            var hue = Oklch.FromHex(HexOf(tier)).HueDegrees;

            foreach (var (app, constant) in Identities.Where(id => id.App != "Constitution"))
            {
                var separation = Oklch.HueSeparation(hue, Oklch.FromHex(HexOf(constant)).HueDegrees);
                if (separation < RequiredSeparation && !Recorded.Contains($"{app}|Constitution"))
                {
                    tooClose.Add($"{tier} is {separation:F1}° from {app}");
                }
            }

            foreach (var (role, (roleHue, roleChroma)) in PaletteSpec.RoleWheel)
            {
                if (roleChroma < 0.02)
                {
                    continue;
                }
                var separation = Oklch.HueSeparation(hue, roleHue);
                if (separation < RequiredSeparation)
                {
                    tooClose.Add($"{tier} is {separation:F1}° from the {role} role");
                }
            }
        }

        Assert.True(tooClose.Count == 0,
            $"Constitution's ladder must clear every sibling and role by {RequiredSeparation}°:\n"
            + string.Join("\n", tooClose));
    }

    /// <summary>
    /// The recorded pair is still what it was. If someone repaints either one, this fails — which is
    /// the moment to delete the exception rather than leave a stale note claiming a problem that has
    /// been fixed, or hiding one that has got worse.
    /// </summary>
    [Fact]
    public void TheRecordedPairIsStillWhereItWasRecorded()
    {
        var separation = Oklch.HueSeparation(
            Oklch.FromHex(HexOf("FlyleafMarkHex")).HueDegrees,
            Oklch.FromHex(HexOf("ForewordSunriseHex")).HueDegrees);

        Assert.True(Math.Abs(separation - 5.9) < 0.5,
            $"Flyleaf and Foreword are now {separation:F1}° apart, not the 5.9° recorded here. If one of "
            + "them was repainted, this exception should go rather than be widened.");
    }

    private static string HexOf(string constant)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Tools", "IconStudio", "Tokens.cs"));
        var match = Regex.Match(source, $@"{constant}\s*=\s*""(?<hex>#[0-9A-Fa-f]{{6,8}})""");
        Assert.True(match.Success, $"IconStudio no longer declares {constant}.");
        return match.Groups["hex"].Value;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DESIGN.md")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
