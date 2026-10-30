using System.Text.RegularExpressions;

namespace Constitution.Tests.MealWeekRun;

/// <summary>What the new files must and must not do, read from the files themselves (a test that loads the types cannot see a call that was never made).</summary>
public class MealWeekSourceGuardTests
{
    private static string Text(string suffix) =>
        File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => ConstitutionSource.Relative(file).EndsWith(suffix, StringComparison.Ordinal)));

    private static IEnumerable<string> NewFiles() => ConstitutionSource.Files(".cs")
        .Where(file =>
        {
            var relative = ConstitutionSource.Relative(file);
            return relative.Contains("/Meals/Pipeline/", StringComparison.Ordinal)
                || relative.StartsWith("Constitution.Android/MealWeek/", StringComparison.Ordinal)
                || (relative.StartsWith("Constitution.Infrastructure/Meals/", StringComparison.Ordinal)
                    && !relative.EndsWith("ClaudeAteEstimator.cs", StringComparison.Ordinal)
                    && !relative.EndsWith("FileAteEstimates.cs", StringComparison.Ordinal)
                    && !relative.EndsWith("JpegScrub.cs", StringComparison.Ordinal));
        });

    [Fact]
    public void TheRunNeverWritesTheCookedHistory()
    {
        var run = Text("Constitution.Infrastructure/Meals/MealWeekRun.cs");

        Assert.DoesNotContain("History.Append", run, StringComparison.Ordinal);
        Assert.DoesNotContain(".Append(new CookedMeal", run, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRunAsksTheLeaseBeforeItReadsAnything()
    {
        var run = Text("Constitution.Infrastructure/Meals/MealWeekRun.cs");

        Assert.True(run.IndexOf("seams.Lease(", StringComparison.Ordinal) < run.IndexOf("seams.Recipes.Read(", StringComparison.Ordinal));
    }

    [Fact]
    public void NoNewFileSwallowsAnException()
    {
        var offences = NewFiles()
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"catch\s*(\([^)]*\))?\s*(when\s*\([^)]*\))?\s*\{\s*\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.True(offences.Count == 0, "Empty catch in: " + string.Join(", ", offences));
    }

    [Fact]
    public void NoNewFileNamesAnIdentifierWithAnUnderscore()
    {
        var offences = NewFiles()
            .Where(file => Regex.IsMatch(Regex.Replace(File.ReadAllText(file), "\"[^\"\\r\\n]*\"|//[^\\r\\n]*", string.Empty), @"\b[A-Za-z]+_[A-Za-z]+\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
            .Select(ConstitutionSource.Relative)
            .ToList();

        Assert.True(offences.Count == 0, "Underscore in an identifier in: " + string.Join(", ", offences));
    }

    [Fact]
    public void TheHeadGivesBothScreensTheSavedWeek()
    {
        var activity = Text("Constitution.Android/ConstitutionActivity.cs");

        Assert.Contains("mealsArea.WeekSource = ", activity, StringComparison.Ordinal);
        Assert.Contains("MealWeek = ", activity, StringComparison.Ordinal);
        Assert.Contains("MealWeekServices.Source(", activity, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadSchedulesNoWorkerForTheMealRunYet()
    {
        var services = Text("Constitution.Android/MealWeek/MealWeekServices.cs");

        Assert.DoesNotContain("WorkManager", services, StringComparison.Ordinal);
        Assert.DoesNotContain("Schedule(", services, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadUsesTheWorkoutRunsCalendarAndKeyStores()
    {
        var services = Text("Constitution.Android/MealWeek/MealWeekServices.cs");

        Assert.Contains("WorkoutWeekCalendarSeams.Create(", services, StringComparison.Ordinal);
        Assert.Contains("calendar.WrittenKeys", services, StringComparison.Ordinal);
        Assert.Contains("calendar.KeyAssignments", services, StringComparison.Ordinal);
        Assert.Contains("household.Planning.AskAsync", services, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadKeepsNoSecretAndAsksNoNewPermission()
    {
        var services = Text("Constitution.Android/MealWeek/MealWeekServices.cs");

        Assert.DoesNotContain("Passphrase", services, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Manifest.Permission", services, StringComparison.Ordinal);
    }
}
