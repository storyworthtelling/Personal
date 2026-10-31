using System.Text.RegularExpressions;

namespace Constitution.Tests.CrossRun;

/// <summary>
/// The wiring the head cannot run here, held by reading its source: each run reads the calendars as its own family, and every path that
/// can place an event goes through the one shared gate. A third path added without the gate fails here before it can lose a key.
/// </summary>
public class CrossRunGuardTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);

    private static string Text(string fileName)
    {
        var file = ConstitutionSource.Files(".cs").Single(path => Path.GetFileName(path) == fileName);
        return File.ReadAllText(file);
    }

    private static IEnumerable<string> ProductionFiles() =>
        ConstitutionSource.Files(".cs").Where(path => !path.Contains("Constitution.Tests", StringComparison.Ordinal));

    [Fact]
    public void TheWorkoutSeamsReadAsWorkouts()
    {
        var seams = Text("WorkoutWeekCalendarSeams.cs");

        Assert.Contains("KeyFamily ownFamily = KeyFamily.Workout", seams, StringComparison.Ordinal);
        Assert.True(
            Regex.IsMatch(seams, @"new GoogleHouseholdCalendars\(\s*service,\s*setup,\s*ownFamily\s*\)", RegexOptions.None, Limit),
            "The calendar source is built without the family it drops.");
    }

    [Fact]
    public void TheMealRunReadsAsTheMealFamily()
    {
        var services = Text("MealWeekServices.cs");

        Assert.Contains("WorkoutWeekCalendarSeams.Create(appContext, KeyFamily.HouseholdMeal)", services, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySourceNamesItsFamily()
    {
        var builders = ProductionFiles()
            .Where(path => Path.GetFileName(path) != "GoogleHouseholdCalendars.cs")
            .Where(path => File.ReadAllText(path).Contains("new GoogleHouseholdCalendars(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["WorkoutWeekCalendarSeams.cs"], builders);
    }

    [Fact]
    public void BothServicesUseTheSharedGate()
    {
        foreach (var file in new[] { "WorkoutWeekServices.cs", "MealWeekServices.cs" })
        {
            var source = Text(file);

            Assert.Contains("RunGate.Shared.RunAsync(", source, StringComparison.Ordinal);
            Assert.Contains("RunGate.DefaultWait", source, StringComparison.Ordinal);
            Assert.Contains("RunGateBusyException", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OnlyTheTwoServicesStartAPlacementRun()
    {
        var weekRuns = ProductionFiles()
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"new WorkoutWeekRun\(|MealWeekRun\.RunAsync\(", RegexOptions.None, Limit))
            .Select(Path.GetFileName)
            .Order()
            .ToList();

        Assert.Equal(["MealWeekServices.cs", "WorkoutWeekServices.cs"], weekRuns);
    }

    [Fact]
    public void OnlyTheTwoRunsBuildARunner()
    {
        var runners = ProductionFiles()
            .Where(path => File.ReadAllText(path).Contains("new PlacementRunner(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order()
            .ToList();

        Assert.Equal(["MealWeekRun.cs", "WorkoutWeekRun.cs"], runners);
    }

    [Fact]
    public void TheGateAndTheFamilySwallowNothing()
    {
        foreach (var file in new[] { "RunGate.cs", "KeyFamily.cs" })
        {
            var source = Text(file);

            Assert.DoesNotMatch(new Regex(@"catch\s*(\([^)]*\))?\s*(when\s*\([^)]*\)\s*)?\{\s*\}", RegexOptions.None, Limit), source);
            Assert.DoesNotMatch(new Regex(@"\b[A-Za-z]+_[A-Za-z_]+\b", RegexOptions.None, Limit), source);
        }
    }
}
