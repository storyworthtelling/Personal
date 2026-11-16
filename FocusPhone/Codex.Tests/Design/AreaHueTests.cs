using System.Text.RegularExpressions;
using Codex.Domain.Design;
using Xunit;
using Cloths = Codex.Domain.Design.DesignTokens.AreaCloths.Constitution;

namespace Codex.Tests.Design;

/// <summary>
/// Every area of an app has its <b>own</b> tab hue (DESIGN.md §9).
///
/// <para>The rule has three parts and each one is load-bearing for a different reason. The
/// <b>home area takes the app's identity hue</b>, so the tab you land on says which app you are in.
/// <b>Settings is always the neutral grey</b>, in every app, so the one area that means the same
/// thing everywhere looks the same everywhere. And the middle areas are <b>colour-coded per
/// area</b> — an inactive tab is cut from its own cloth, which is what makes the strip read as a
/// set of coded dividers at rest, the way a real binder does.</para>
///
/// <para>Score shipped six areas in one hue. That is not a slightly-off palette: it is the strip
/// doing none of its job, because a divider that is the same colour as every other divider is not a
/// divider. It was recorded as scheduled work from P7 and this is the guard that closes it.</para>
///
/// <para><b>Constitution is the one app that may share (D84).</b> Its five tabs are deep cloths
/// the owner chose by colour (D40, D45): Workouts' red is the Problem role's mark and Body's green
/// is in the Positive family, which is exactly the overlap the rule above forbids Score and Post.
/// The sharing is allowed, not accidental: a tab is a divider, not a state, and a fold wearing its
/// own tab's hue on Workouts or Body passes. The other rules (Today carries the identity, Settings
/// alone is grey) still hold, and are checked against Constitution's tokens below.</para>
///
/// <para>Read from source rather than by loading types, because these live in Android heads that
/// the test project cannot reference.</para>
/// </summary>
public class AreaHueTests
{
    /// <summary>The apps with a binder strip, and the identity hue each one's home area takes.</summary>
    private static readonly (string Head, string Identity)[] Apps =
    [
        ("Score.Android", "Score"),
        ("Post.Android", "Post"),
    ];

    [Theory]
    [InlineData("Score.Android")]
    [InlineData("Post.Android")]
    public void NoTwoAreasShareAHue(string head)
    {
        var byHue = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var (area, mark, _) in AreasOf(head))
        {
            if (!byHue.TryGetValue(mark, out var sharing))
            {
                byHue[mark] = sharing = [];
            }

            sharing.Add(area);
        }

        var clashes = byHue
            .Where(pair => pair.Value.Count > 1)
            .Select(pair => $"{pair.Key} is shared by {string.Join(", ", pair.Value)}")
            .ToArray();

        Assert.True(clashes.Length == 0,
            $"{head}'s binder strip cannot code anything — a divider the same colour as every "
            + "other divider is not a divider (§9):\n" + string.Join("\n", clashes));
    }

    [Theory]
    [InlineData("Score.Android", "Score")]
    [InlineData("Post.Android", "Post")]
    public void TheHomeAreaTakesTheAppsIdentityHue(string head, string identity)
    {
        var areas = AreasOf(head);

        // The home area is the first one the shell lists, which is the one the app opens on.
        var withIdentity = areas
            .Where(area => area.Mark == $"PaperTheme.{identity}Mark")
            .Select(area => area.Name)
            .ToArray();

        Assert.True(withIdentity.Length == 1,
            $"exactly one area of {head} carries the identity hue; found "
            + (withIdentity.Length == 0 ? "none" : string.Join(", ", withIdentity)));
    }

    [Theory]
    [InlineData("Score.Android")]
    [InlineData("Post.Android")]
    public void SettingsIsAlwaysTheNeutralGrey(string head)
    {
        var settings = AreasOf(head).Single(area => area.Name == "Settings");

        Assert.Equal("PaperTheme.ResolvedMark", settings.Mark);
        Assert.Equal("PaperTheme.ResolvedFill", settings.Fill);
    }

    /// <summary>
    /// And the neutral grey belongs to Settings alone. Giving a content area the resolved hue would
    /// make it read as the settled, done-with one — which is a status, and §3 forbids an area tint
    /// reading as one.
    /// </summary>
    [Theory]
    [InlineData("Score.Android")]
    [InlineData("Post.Android")]
    public void NothingButSettingsTakesTheNeutralGrey(string head)
    {
        var wearingIt = AreasOf(head)
            .Where(area => area.Mark == "PaperTheme.ResolvedMark")
            .Select(area => area.Name)
            .ToArray();

        Assert.Equal(["Settings"], wearingIt);
    }

    /// <summary>
    /// The mark and the fill are the two tiers of one hue, so an area that mixes them is a tab
    /// whose label does not match its own cloth. Easy to do and impossible to see in a diff.
    /// </summary>
    [Theory]
    [InlineData("Score.Android")]
    [InlineData("Post.Android")]
    public void EachAreasInkAndClothAreTheSameHue(string head)
    {
        foreach (var (area, mark, fill) in AreasOf(head))
        {
            Assert.Equal(
                mark.Replace("Mark", string.Empty, StringComparison.Ordinal),
                fill.Replace("Fill", string.Empty, StringComparison.Ordinal));

            Assert.EndsWith("Mark", mark, StringComparison.Ordinal);
            Assert.EndsWith("Fill", fill, StringComparison.Ordinal);
            _ = area;
        }
    }

    /// <summary>
    /// <b>A page's accents wear its tab's colour</b> (owner's ruling, W8, one approach for the whole suite; DESIGN.md
    /// §9). This reverses the restraint rule this test used to hold, under which a role-hued tab's page kept the
    /// app's identity for its filter field, Dial and tempo bands. Now a page sitting behind a role tab must not
    /// reach for the identity hue: its accents take the tab's (<c>Tab.Hue</c>). The home area's tab IS the
    /// identity, so its page wearing it is the same rule, not an exception.
    /// </summary>
    [Theory]
    [InlineData("Score.Android", "Score")]
    [InlineData("Post.Android", "Post")]
    public void EveryAccentOnThePageWearsItsOwnTabsHue(string head, string identity)
    {
        string[] Roles = ["Reference", "Progress", "Positive", "Caution", "Problem", "Resolved", "Action"];

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(DesignTestHelpers.FocusPhoneRoot(), head), "*Area.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);

            var tab = lines.FirstOrDefault(line => line.Contains("BinderArea", StringComparison.Ordinal)
                && line.Contains("PaperTheme.", StringComparison.Ordinal))
                ?? string.Join(" ", lines);

            var match = Regex.Match(tab, @"PaperTheme\.(?<hue>\w+?)(Mark|Fill)");
            if (!match.Success || !Roles.Contains(match.Groups["hue"].Value))
            {
                continue;
            }

            foreach (var line in lines)
            {
                if (line.Contains("BinderArea", StringComparison.Ordinal) || line.Contains("TabGlyphs.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.Contains($"PaperTheme.{identity}Mark", StringComparison.Ordinal)
                    || line.Contains($"PaperTheme.{identity}Fill", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)} is a {match.Groups["hue"].Value} tab: {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A page's accents take its tab's hue (§9, owner's ruling); use Tab.Hue, not the identity:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>Apps whose cloths may share a hue with each other or with a role (D84).</summary>
    private static readonly string[] SharingAllowed = ["Constitution"];

    [Fact]
    public void OnlyConstitutionMaySharePerD84()
    {
        Assert.Equal(["Constitution"], SharingAllowed);
        Assert.DoesNotContain(Apps, app => SharingAllowed.Contains(app.Identity));
    }

    [Fact]
    public void ConstitutionsClothsMayShareARoleAndStillHoldTheFixedRules()
    {
        // Shared with a role: Workouts' red IS the Problem role's mark. Allowed under D84, and a
        // fold wearing that hue on the Workouts page passes for the same reason.
        Assert.Equal(DesignTokens.ProblemMark, Cloths.Workouts, ignoreCase: true);

        // Settings alone is the neutral grey; every other cloth is chromatic.
        Assert.True(Spread(Cloths.Settings) <= 12, "Settings must be the neutral grey.");
        foreach (var cloth in new[] { Cloths.Workouts, Cloths.Meals, Cloths.Body })
        {
            Assert.False(string.Equals(Cloths.Settings, cloth, StringComparison.OrdinalIgnoreCase));
        }

        Assert.True(Spread(Cloths.Workouts) > 30 && Spread(Cloths.Body) > 30);

        // Today wears the cerulean identity, not a role.
        Assert.Equal(DesignTokens.Identity.Constitution.Deep, Cloths.Today, ignoreCase: true);
    }

    private static int Spread(string hex)
    {
        var (r, g, b) = DesignTestHelpers.ParseHex(hex);
        return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b));
    }

    private static IReadOnlyList<(string Name, string Mark, string Fill)> AreasOf(string head)
    {
        var directory = Path.Combine(DesignTestHelpers.FocusPhoneRoot(), head);
        Assert.True(Directory.Exists(directory), $"{head} has moved; this guard is watching nothing.");

        var areas = new List<(string, string, string)>();

        foreach (var file in Directory.EnumerateFiles(directory, "*Area.cs", SearchOption.AllDirectories))
        {
            var match = Regex.Match(
                File.ReadAllText(file),
                @"BinderArea\s+Tab\s*=>\s*new\(\s*""(?<name>[^""]+)""\s*,\s*[^,]+,\s*(?<mark>[\w.]+)\s*,\s*(?<fill>[\w.]+)\s*\)");

            if (match.Success)
            {
                areas.Add((
                    match.Groups["name"].Value,
                    match.Groups["mark"].Value,
                    match.Groups["fill"].Value));
            }
        }

        Assert.True(areas.Count >= 4, $"found only {areas.Count} areas in {head}; the pattern has moved.");

        return areas;
    }
}
