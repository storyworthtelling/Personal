using System.Text.RegularExpressions;

namespace Constitution.Tests.Shell;

/// <summary>
/// C-3.2.4 / D59: the furniture every tab shares. Loading is a status sentence, never a spinner
/// (DESIGN.md section 8); success is an inline change, never a toast; the head's views are
/// compile-checked only, so what can be said about them is said here by reading the source.
/// </summary>
public class PageFurnitureTests
{
    // Assembled at run time so this guard's own source does not contain what it forbids.
    private static readonly string Spinner = "Progress" + "Bar";
    private static readonly string Toast = "Toa" + "st";

    private static readonly string[] AreaNames = ["Today", "Workouts", "Meals", "Body", "Settings"];

    private static IReadOnlyList<string> HeadSources() =>
        ConstitutionSource.Files(".cs", ".xml", ".axml")
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Android/", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void NoSpinnerAnywhere()
    {
        var files = ConstitutionSource.Files(".cs", ".xml", ".axml", ".xaml");
        Assert.NotEmpty(files);

        var offenders = files
            .Where(file => File.ReadAllText(file).Contains(Spinner, StringComparison.Ordinal))
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "A spinner under Constitution/ (loading is a status sentence):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void NoToastAnywhereInTheHead()
    {
        var offenders = HeadSources()
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\b" + Toast + @"\b"))
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.True(offenders.Count == 0, "A toast in the head (success is an inline state change):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void ASpinnerPlantedInASourceIsCaught()
    {
        var root = Guard.GuardScan.TempRoot();
        try
        {
            Guard.GuardScan.Plant(root, "Constitution.Android/Bad.cs", "var wheel = new " + Spinner + "(this);");
            var scan = Guard.GuardScan.Run(root.FullName, (_, text) =>
                text.Contains(Spinner, StringComparison.Ordinal) ? [new Guard.Finding(1, "spinner")] : []);
            Assert.Single(scan.Hits);
        }
        finally
        {
            root.Delete(true);
        }
    }

    [Theory]
    [MemberData(nameof(Areas))]
    public void EveryTabHasItsOwnAreaClassOnTheFrozenId(string name, string id)
    {
        var file = HeadSources().SingleOrDefault(path => Path.GetFileName(path) == name + "Area.cs");
        Assert.True(file is not null, $"{name}Area.cs was not found under Constitution.Android/.");

        var source = File.ReadAllText(file!);
        Assert.Contains($"class {name}Area", source, StringComparison.Ordinal);
        Assert.Contains($"\"{id}\"", source, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> Areas() =>
        AreaNames.Select(name => new object[] { name, name.ToLowerInvariant() });

    [Fact]
    public void TheShellFiltersTheTabsThroughVisiblePillars()
    {
        var shell = HeadSources().SingleOrDefault(path => Path.GetFileName(path) == "ConstitutionActivity.cs");
        Assert.True(shell is not null, "ConstitutionActivity.cs was not found under Constitution.Android/.");

        Assert.Contains("ConstitutionAreas.Visible(", File.ReadAllText(shell!), StringComparison.Ordinal);
    }

    [Fact]
    public void TheShellLaysThePageOutThroughPageFrameLayout()
    {
        var frame = HeadSources().SingleOrDefault(path => Path.GetFileName(path) == "PageFrame.cs");
        Assert.True(frame is not null, "PageFrame.cs was not found under Constitution.Android/.");

        Assert.Contains("PageFrameLayout.Compute(", File.ReadAllText(frame!), StringComparison.Ordinal);
    }

    [Fact]
    public void PillarsAreStoredUnderTheFrozenFileAndKeys()
    {
        var store = HeadSources().SingleOrDefault(path => Path.GetFileName(path) == "PillarStore.cs");
        Assert.True(store is not null, "PillarStore.cs was not found under Constitution.Android/.");

        var source = File.ReadAllText(store!);
        Assert.Contains("ConstitutionIdentity.SettingsPreferencesName", source, StringComparison.Ordinal);
        foreach (var key in new[] { "ConstitutionIdentity.PillarWorkoutsKey", "ConstitutionIdentity.PillarMealsKey", "ConstitutionIdentity.PillarBodyKey" })
        {
            Assert.Contains(key, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SettingsIsPlainAndTheOthersLeaveASeamForTheirGround()
    {
        var area = HeadSources().Single(path => Path.GetFileName(path) == "IConstitutionArea.cs");
        Assert.Contains("PageGround", File.ReadAllText(area), StringComparison.Ordinal);
    }
}
