using System.Reflection;
using System.Text.RegularExpressions;
using Codex.Domain.Design;
using Constitution.Domain.Areas;

namespace Constitution.Tests.Areas;

/// <summary>
/// The five tabs' cloths (D40, D45, book p.11 strip B). A cloth that no registered token file
/// holds is a hex literal waiting to leak (DESIGN.md §16), and the Meals steel was registered
/// nowhere before this release.
/// </summary>
public class ConstitutionAreaTokensTests
{
    [Fact]
    public void EveryClothIsRegistered()
    {
        var registered = RegisteredHexes(typeof(DesignTokens));

        foreach (var area in ConstitutionAreas.All)
        {
            Assert.Contains(area.Cloth, registered);
            Assert.Contains(area.InactiveLettering, registered);
            Assert.Contains(area.ActiveInk, registered);
        }
    }

    [Fact]
    public void TheFourChosenClothsComeFromTheRegisteredTokens()
    {
        string ClothOf(string id) => ConstitutionAreas.All.Single(area => area.Id == id).Cloth;

        Assert.Equal(DesignTokens.AreaCloths.Constitution.Workouts, ClothOf("workouts"));
        Assert.Equal(DesignTokens.AreaCloths.Constitution.Meals, ClothOf("meals"));
        Assert.Equal(DesignTokens.AreaCloths.Constitution.Body, ClothOf("body"));
        Assert.Equal(DesignTokens.AreaCloths.Constitution.Settings, ClothOf("settings"));
    }

    [Fact]
    public void TodayWearsTheCeruleanIdentity()
    {
        var today = ConstitutionAreas.All.Single(area => area.Id == "today");

        Assert.Equal(DesignTokens.Identity.Constitution.Text, today.ActiveInk);
        Assert.Equal(DesignTokens.Identity.Constitution.Deep, today.Cloth);
    }

    [Fact]
    public void InactiveMiddleTabsAreDeepClothsWithPaperLettering()
    {
        foreach (var id in new[] { "today", "workouts", "meals", "body" })
        {
            var area = ConstitutionAreas.All.Single(candidate => candidate.Id == id);

            Assert.Equal(DesignTokens.Paper, area.InactiveLettering);
            Assert.True(DesignTestContrast(area.InactiveLettering, area.Cloth) >= 4.5,
                $"{id}: paper lettering on its cloth is below AA.");
        }
    }

    [Fact]
    public void SettingsIsTheNeutralGreyWithInkLettering()
    {
        var settings = ConstitutionAreas.All.Single(area => area.Id == "settings");

        Assert.Equal(DesignTokens.Ink, settings.InactiveLettering);
        Assert.Equal(DesignTokens.Ink, settings.ActiveInk);
    }

    [Fact]
    public void AreasSourceHoldsNoHexLiteral()
    {
        var directory = Path.Combine(ConstitutionSource.Root(), "Constitution.Domain", "Areas");

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
        {
            Assert.False(Regex.IsMatch(File.ReadAllText(file), "#[0-9A-Fa-f]{6}\\b"),
                $"{Path.GetFileName(file)} holds a hex literal; cloths come from DesignTokens.");
        }
    }

    private static HashSet<string> RegisteredHexes(Type type)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
        {
            if (field.IsLiteral && field.GetRawConstantValue() is string value && Regex.IsMatch(value, "^#[0-9A-Fa-f]{6}$"))
            {
                found.Add(value);
            }
        }

        foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
        {
            found.UnionWith(RegisteredHexes(nested));
        }

        return found;
    }

    private static double DesignTestContrast(string a, string b)
    {
        static double Luminance(string hex)
        {
            double Channel(int shift)
            {
                var value = ((Convert.ToInt32(hex.TrimStart('#'), 16) >> shift) & 0xFF) / 255.0;
                return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(16) + 0.7152 * Channel(8) + 0.0722 * Channel(0);
        }

        var (high, low) = (Math.Max(Luminance(a), Luminance(b)), Math.Min(Luminance(a), Luminance(b)));
        return (high + 0.05) / (low + 0.05);
    }
}
