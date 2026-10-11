using System.Text.RegularExpressions;
using Pulp.Domain.Shell;

namespace Pulp.Tests.Design;

/// <summary>
/// DESIGN.md §9 for Pulp's binder strip, plus the one rule only Pulp has.
///
/// <para>The strip is decided in <see cref="PulpAreas"/> (pure, so it is tested here for real) and
/// painted by the Android head's <c>Shell/AreaPalette.cs</c>, which this guard reads by content
/// because the test project cannot load an Android head. Each half can break the rule on its own:
/// the Domain table can give two areas one hue, and the palette can paint a role's cloth under
/// another role's ink.</para>
///
/// <para>The mapping (D212, as drawn on mockup 01): Watching is the amber identity, Playlists the
/// Reference slate, Discover the Positive leaf, Sports the Action pine, Calendar the in-progress
/// teal, Settings the Resolved grey. The Caution ochre is kept off the strip — and off every Pulp
/// page — because the amber identity sits 14.3 degrees from it (D275).</para>
/// </summary>
public class PulpAreaHueTests
{
    private static string PalettePath() =>
        Path.Combine(PulpSource.Root(), "Pulp.Android", "Shell", "AreaPalette.cs");

    [Fact]
    public void TheStripHasTheSixAreasOfMockupOneInOrder()
    {
        Assert.Equal(
            ["Watching", "Playlists", "Discover", "Sports", "Calendar", "Settings"],
            PulpAreas.All.Select(area => area.Label).ToArray());
        Assert.Equal(Enum.GetValues<PulpArea>(), PulpAreas.All.Select(area => area.Area).ToArray());
    }

    [Fact]
    public void NoTwoAreasShareAHue()
    {
        Assert.NotEmpty(PulpAreas.All);

        var clashes = PulpAreas.All
            .GroupBy(area => area.Hue)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} is shared by {string.Join(", ", group.Select(area => area.Label))}")
            .ToArray();

        Assert.True(clashes.Length == 0,
            "A divider the same colour as another divider is not a divider (§9):\n" + string.Join("\n", clashes));
    }

    [Fact]
    public void ExactlyOneAreaCarriesTheIdentityAndItIsTheHome()
    {
        var withIdentity = PulpAreas.All.Where(area => area.Hue == AreaHue.Identity).ToArray();

        Assert.Single(withIdentity);
        Assert.Equal(PulpArea.Watching, withIdentity[0].Area);
        Assert.Equal(PulpArea.Watching, PulpAreas.All[0].Area);
    }

    [Fact]
    public void SettingsAloneIsTheNeutralGrey()
    {
        var grey = PulpAreas.All.Where(area => area.Hue == AreaHue.Resolved).Select(area => area.Area).ToArray();

        Assert.Equal([PulpArea.Settings], grey);
    }

    /// <summary>
    /// Structural: the hue a tab can be given has no Caution member at all, so no table edit can
    /// put the ochre on the strip without first adding it here — which is a decision, not a drift.
    /// </summary>
    [Fact]
    public void NoAreaCanBeGivenTheCautionHue()
    {
        var names = Enum.GetNames<AreaHue>();
        Assert.NotEmpty(names);
        Assert.DoesNotContain(names, name => name.Contains("Caution", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EveryAreaHasAHeaderAGlyphAndAQuietStatusSentence()
    {
        Assert.NotEmpty(PulpAreas.All);
        foreach (var area in PulpAreas.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(area.Title), $"{area.Label} has no header");
            Assert.Matches("^[a-z][a-z0-9]*$", area.Glyph);
            Assert.EndsWith(".", area.Status, StringComparison.Ordinal);
        }
    }

    /// <summary>"Continue watching" is too long for a 66dp tab, so the tab says Watching (mockup 01).</summary>
    [Fact]
    public void TheHomeTabIsShortForItsHeader()
    {
        var home = PulpAreas.For(PulpArea.Watching);
        Assert.Equal("Watching", home.Label);
        Assert.Equal("Continue watching", home.Title);
    }

    /// <summary>
    /// §9's restraint: a tab's role hue is a tab, not a state, so the page does not wear it. The
    /// header's marker underline is the amber identity on every content area (mockups 03, 04, 07,
    /// 08 draw it so), and Settings' is its own grey (mockup 09).
    /// </summary>
    [Fact]
    public void TheHeaderRuleIsTheIdentityExceptOnSettings()
    {
        Assert.NotEmpty(PulpAreas.All);
        foreach (var area in PulpAreas.All)
        {
            var expected = area.Area == PulpArea.Settings ? AreaHue.Resolved : AreaHue.Identity;
            Assert.Equal(expected, PulpAreas.RuleHue(area.Area));
        }
    }

    [Fact]
    public void EveryGlyphIsAPulpDrawableWithNoColourOfItsOwn()
    {
        var drawables = Path.Combine(PulpSource.Root(), "Pulp.Android", "Resources", "drawable");
        Assert.NotEmpty(PulpAreas.All);

        foreach (var area in PulpAreas.All)
        {
            var file = Path.Combine(drawables, area.Glyph + ".xml");
            Assert.True(File.Exists(file), $"{area.Label}'s glyph {area.Glyph}.xml is missing");

            // The binder tints the glyph (ink on cloth, the area's hue when active); a colour baked
            // into the vector would be a hex literal outside the token files.
            Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", File.ReadAllText(file));

            // And the head draws the drawable the table names, not a neighbour's.
            Assert.Contains($"PulpArea.{area.Area} => Resource.Drawable.{area.Glyph},", File.ReadAllText(PalettePath()));
        }
    }

    /// <summary>
    /// The palette paints each hue's ink and cloth from ONE role — mixing them is a tab whose label
    /// does not match its own cloth, impossible to see in a diff — and never names Caution.
    /// </summary>
    [Fact]
    public void ThePaletteMapsEveryHueToItsOwnTiers()
    {
        Assert.True(File.Exists(PalettePath()), "Pulp.Android/Shell/AreaPalette.cs is missing");
        var source = File.ReadAllText(PalettePath());

        // One arm per line: "AreaHue.X => (ink, cloth, rule),".
        var arms = Regex.Matches(source, @"^\s*AreaHue\.(\w+)\s*=>\s*(.+)$", RegexOptions.Multiline);
        Assert.Equal(Enum.GetValues<AreaHue>().Length, arms.Count);

        foreach (Match arm in arms)
        {
            var hue = arm.Groups[1].Value;
            var tokens = Regex.Matches(arm.Groups[2].Value, @"DesignTokens\.([\w.]+)")
                .Select(token => token.Groups[1].Value)
                .ToArray();
            Assert.True(tokens.Length == 3, $"{hue} should name ink, cloth and rule tokens: {arm.Value}");

            var prefix = hue == nameof(AreaHue.Identity) ? "Identity.Pulp." : hue;
            Assert.All(tokens, token => Assert.StartsWith(prefix, token, StringComparison.Ordinal));
            Assert.DoesNotContain(tokens, token => token.Contains("Caution", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// And the page does not inherit its tab: an area's own file must not name its tab's role, which
    /// is the tempting change once the strip is coded (matching a filter field to its tab).
    /// </summary>
    [Fact]
    public void NoAreaPageWearsItsOwnTabsRoleHue()
    {
        var shell = Path.Combine(PulpSource.Root(), "Pulp.Android", "Shell");
        var checkedAreas = 0;

        foreach (var area in PulpAreas.All.Where(area => area.Hue != AreaHue.Identity))
        {
            var file = Path.Combine(shell, area.Area + "Area.cs");
            Assert.True(File.Exists(file), $"{area.Area}Area.cs is missing");
            Assert.DoesNotMatch($@"\b(DesignTokens|PaperTheme)\.{area.Hue}\w*", File.ReadAllText(file));
            checkedAreas++;
        }

        Assert.Equal(5, checkedAreas);
    }
}
