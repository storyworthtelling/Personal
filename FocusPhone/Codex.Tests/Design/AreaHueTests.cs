using System.Text.RegularExpressions;
using Xunit;

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
    /// <b>The page does not inherit its own tab's hue</b> (§9, stated outright).
    ///
    /// <para>An area colour that happens to be a role hue is a tab, not a state — so a control on
    /// the page must not take it. The tempting change, once the tabs are coded, is to make the
    /// filter field in Podcasts match the Podcasts tab; that is the moment a role hue stops being a
    /// divider and starts looking like a status, which §3 forbids. In-page accents stay the app's
    /// identity hue.</para>
    ///
    /// <para><b>Only the area's own hue, and only when that is a role.</b> This took two goes to
    /// state. The first version flagged every role hue on every page, which named Mailbox's problem
    /// red and Rolodex's caution ochre — genuine <i>statuses</i>, and exactly what role hues exist
    /// for. The second flagged the home areas, whose tab hue is the app's identity and whose pages
    /// are meant to wear it. What is left is the real rule, and it is narrow: a page must not wear
    /// a <b>role</b> hue that is the colour of the tab it is sitting behind.</para>
    /// </summary>
    [Theory]
    [InlineData("Score.Android")]
    [InlineData("Post.Android")]
    public void NoControlOnThePageWearsItsOwnAreasRoleHue(string head)
    {
        string[] Roles = ["Reference", "Progress", "Positive", "Caution", "Problem", "Resolved"];

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(DesignTestHelpers.FocusPhoneRoot(), head), "*Area.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);

            var tab = lines.FirstOrDefault(line => line.Contains("BinderArea", StringComparison.Ordinal)
                && line.Contains("PaperTheme.", StringComparison.Ordinal))
                ?? string.Join(" ", lines);

            var match = Regex.Match(tab, @"PaperTheme\.(?<hue>\w+?)(Mark|Fill)");
            if (!match.Success)
            {
                continue;
            }

            var own = match.Groups["hue"].Value;

            // The home area's tab hue IS the app's identity, and the page is meant to wear that —
            // it is the accent every control in the app already uses. Only a ROLE hue is forbidden
            // on the page beneath it, because only a role hue can be mistaken for a status.
            if (!Roles.Contains(own))
            {
                continue;
            }

            foreach (var line in lines)
            {
                if (line.Contains("BinderArea", StringComparison.Ordinal)
                    || line.Contains("Resource.Drawable.area_", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.Contains($"PaperTheme.{own}Mark", StringComparison.Ordinal)
                    || line.Contains($"PaperTheme.{own}Fill", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)} is an {own} tab: {line.Trim()}");
                }
            }
        }

        // Post's Filed and Rolodex filter fields each take their own tab's hue. Found by this
        // guard rather than by looking, and NOT changed here: Post's areas went through the P7
        // gate, so whether that was approved or missed is the owner's to say, and quietly
        // recolouring another app's approved screens on the strength of one sentence would be
        // worse than leaving it visible. Anything NEW in Post still fails.
        string[] standing =
        [
            "FiledArea.cs is an Caution tab: filter = new FilterField(Host, \"Find something filed\", PaperTheme.CautionMark);",
            "RolodexArea.cs is an Positive tab: filter = new FilterField(Host, \"Find a person\", PaperTheme.PositiveMark);",
        ];

        var unexpected = offenders.Except(standing).ToArray();

        Assert.True(unexpected.Length == 0,
            "A page wearing its own tab's hue reads as a status rather than a divider (§9/§3). "
            + "In-page accents take the app's identity hue:\n" + string.Join("\n", unexpected));
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
