using System.Text.RegularExpressions;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>
/// The Workouts tab's wiring in the head cannot run here, so it is held by reading it: every swap, skip and retry reaches the calendar through
/// the shared gate, off the UI thread, and nothing else builds the change object that touches the written keys.
/// </summary>
public class GatedChangesSourceTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);

    private static string Area() =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "WorkoutsArea.cs"));

    private static string Gated() =>
        File.ReadAllText(ConstitutionSource.Files(".cs").Single(path => Path.GetFileName(path) == "GatedWorkoutChanges.cs"));

    private static IEnumerable<string> ProductionFiles() =>
        ConstitutionSource.Files(".cs").Where(path => !path.Contains("Constitution.Tests", StringComparison.Ordinal));

    [Fact]
    public void TheAreaGatesItsChangesOnTheSharedGateWithTheFiveMinuteWait()
    {
        var area = Area();

        Assert.Contains("new GatedWorkoutChanges(", area, StringComparison.Ordinal);
        Assert.Contains("RunGate.Shared", area, StringComparison.Ordinal);
        Assert.Contains("RunGate.DefaultWait", area, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheAreaBuildsTheChangesAndItWrapsThemInTheGate()
    {
        var builders = ProductionFiles()
            .Where(path => File.ReadAllText(path).Contains("new WorkoutChanges(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["WorkoutsArea.cs"], builders);
        Assert.Contains("new GatedWorkoutChanges(new WorkoutChanges(", Area(), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheChangesModelAsksTheCalendarToFollow()
    {
        var callers = ProductionFiles()
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"\.Follow(Swap|Skip)Async\(", RegexOptions.None, Limit))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["WorkoutChanges.cs"], callers);
    }

    [Fact]
    public void EveryChangeTheAreaStartsRunsOffTheUiThread()
    {
        var area = Area();
        var calls = Regex.Matches(area, @"\.(SwapAsync|SkipAsync|RetryAsync)\(", RegexOptions.None, Limit);

        Assert.Equal(3, calls.Count);
        foreach (Match call in calls)
        {
            var before = area[Math.Max(0, call.Index - 400)..call.Index];
            Assert.True(before.Contains("Task.Run(", StringComparison.Ordinal), $"{call.Value} is not inside Task.Run.");
        }

        Assert.DoesNotMatch(new Regex(@"\.Wait\(|\.Result;|\.Result\)|GetAwaiter\(\)\.GetResult", RegexOptions.None, Limit), area);
    }

    [Fact]
    public void EveryGatedMethodTakesItsTurnAndAnswersBusyInWords()
    {
        var gated = Gated();

        Assert.Equal(3, Regex.Matches(gated, @"gate\.RunAsync\(", RegexOptions.None, Limit).Count);
        Assert.Equal(3, Regex.Matches(gated, @"Answer\(await gate\.RunAsync\(", RegexOptions.None, Limit).Count);
        Assert.Contains("GateResult<ChangeResult>.Busy", gated, StringComparison.Ordinal);
        Assert.Contains("FollowWords.GateBusy", gated, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGatedChangesSwallowNothingAndUseNoUnderscores()
    {
        var gated = Gated();

        Assert.DoesNotMatch(new Regex(@"catch\s*(\([^)]*\))?\s*(when\s*\([^)]*\)\s*)?\{\s*\}", RegexOptions.None, Limit), gated);
        Assert.DoesNotMatch(new Regex(@"\b[A-Za-z]+_[A-Za-z_]+\b", RegexOptions.None, Limit), gated);
    }
}
