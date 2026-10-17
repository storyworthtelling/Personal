using System.Text.RegularExpressions;
using Constitution.Domain.Areas;
using Constitution.Domain.Ground;
using Constitution.Tests.Guard;

namespace Constitution.Tests.Ground;

/// <summary>
/// Settings has no crinkle (D71); the other four areas do (D43). The decision is
/// <see cref="PageGround.For"/>; the source-scan half proves the painters honour it: Settings' own
/// file names no ground painter, and the only file that picks a painter does so from the page's
/// ground, so there is no second route by which crinkle could reach Settings.
/// </summary>
public class SettingsIsPlainTests
{
    private const string SettingsFile = "Constitution.Android/SettingsArea.cs";
    private const string AreaBaseFile = "Constitution.Android/ConstitutionAreaBase.cs";

    /// <summary>The names by which a ground (or anything of the scrapbook) is reached.</summary>
    private static readonly Regex GroundNames = new(
        @"\b(?:CrinkleField|CrinkleGround\w*|CrinkleSeed|GroundPainter|PageGround|Scrapbook|ScrapHost|ScrapView)\b",
        RegexOptions.CultureInvariant);

    private static IEnumerable<Finding> FindGroundUse(string path, string text)
    {
        if (!path.EndsWith("SettingsArea.cs", StringComparison.Ordinal))
        {
            yield break;
        }

        foreach (Match match in GroundNames.Matches(text))
        {
            yield return new Finding(GuardScan.LineOf(text, match.Index), match.Value);
        }
    }

    [Fact]
    public void SettingsIsPlain()
    {
        var settings = ConstitutionAreas.All.Single(area => area.Id == "settings");

        Assert.Equal(PageGround.Plain, PageGround.For(settings));
    }

    [Fact]
    public void TheOtherFourAreasAreCrinkled()
    {
        var others = ConstitutionAreas.All.Where(area => area.Id != "settings").ToList();

        Assert.Equal(4, others.Count);
        Assert.All(others, area => Assert.Equal(PageGround.Crinkled, PageGround.For(area)));
    }

    [Fact]
    public void SettingsAreaNamesNoGroundPainter()
    {
        var result = GuardScan.Run(ConstitutionSource.Root(), FindGroundUse);

        Assert.Contains(SettingsFile, result.Scanned);
        Assert.True(result.Hits.Count == 0,
            "SettingsArea must stay plain (D71) and leave the ground to the base class:\n" + string.Join("\n", result.Hits));
    }

    [Theory]
    [InlineData("public override IPageGroundPainter GroundPainter => new CrinkleGroundPainter(Host);")]
    [InlineData("var field = CrinkleField.ForWell(1, 300, 600);")]
    [InlineData("return new CrinkleGroundPainter(Host).Paint(page);")]
    [InlineData("var seed = CrinkleSeed.For(\"settings\");")]
    [InlineData("var host = new ScrapHost(Host);")]
    [InlineData("if (Ground == PageGround.Crinkled) { }")]
    public void ASettingsAreaThatReachesForCrinkleIsCaught(string planted)
    {
        var root = GuardScan.TempRoot();
        try
        {
            GuardScan.Plant(root, SettingsFile, "class SettingsArea {\n  void M() { " + planted + " }\n}\n");

            var result = GuardScan.Run(root.FullName, FindGroundUse);

            Assert.Contains(result.Hits, h => h.File == SettingsFile && h.Line == 2);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void ThePlainSettingsFileAsItIsIsNotFlagged()
    {
        var root = GuardScan.TempRoot();
        try
        {
            GuardScan.Plant(root, SettingsFile,
                "class SettingsArea {\n  protected override View BuildPage() => PageFurniture.Sentence(Host, \"Nothing to set yet.\");\n}\n");

            Assert.Empty(GuardScan.Run(root.FullName, FindGroundUse).Hits);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheBaseClassPicksTheCrinklePainterFromThePagesGround()
    {
        var text = File.ReadAllText(Path.Combine(ConstitutionSource.Root(), AreaBaseFile));

        // The painter is chosen by the page's ground decision, and the plain painter is the other branch.
        Assert.Matches(new Regex(@"PageGround\.Crinkled[\s\S]{0,200}CrinkleGroundPainter|CrinkleGroundPainter[\s\S]{0,200}PageGround\.Crinkled"), text);
        Assert.Contains("PlainGroundPainter", text);
    }

    [Fact]
    public void OnlyTheBaseClassAndTheScrapbookNameTheCrinklePainter()
    {
        var offenders = ConstitutionSource.Files(".cs")
            .Select(ConstitutionSource.Relative)
            .Where(path => path.StartsWith("Constitution.Android/", StringComparison.Ordinal))
            .Where(path => !path.StartsWith("Constitution.Android/Scrapbook/", StringComparison.Ordinal))
            .Where(path => path != AreaBaseFile)
            .Where(path => Regex.IsMatch(File.ReadAllText(Path.Combine(ConstitutionSource.Root(), path)), @"\bCrinkleGroundPainter\b"))
            .ToList();

        Assert.True(offenders.Count == 0, "Crinkle is reached only through the area base class's ground decision:\n" + string.Join("\n", offenders));
    }
}
