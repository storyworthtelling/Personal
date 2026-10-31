using System.Text.RegularExpressions;
using System.Xml.Linq;
using Constitution.Domain;
using Constitution.Tests.Body;

namespace Constitution.Tests.MealWorker;

/// <summary>
/// P13 — the meal-week worker held to its frozen names and its wiring by reading its source, in the style of the workout worker's guard.
/// Nothing here has run on a phone: WorkManager has not yet woken this worker anywhere.
/// </summary>
public class MealWeekWorkerGuardTests
{
    private static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";

    private static string Source(string fileName)
    {
        var file = ConstitutionSource.Files(".cs").SingleOrDefault(path => Path.GetFileName(path) == fileName);
        Assert.True(file is not null, fileName + " was not found under Constitution/.");
        return File.ReadAllText(file!);
    }

    private static IEnumerable<string> MealWeekHeadFiles() =>
        ConstitutionSource.Files(".cs").Where(path => ConstitutionSource.Relative(path).Contains("Constitution.Android/MealWeek/", StringComparison.Ordinal));

    // ---- the frozen names ----

    [Fact]
    public void TheNamesAreTheFrozenLiterals()
    {
        // The full literal is pinned in ConstitutionInvariantsTests (the one test file allowed to carry the application id).
        Assert.Equal(ConstitutionIdentity.ApplicationId + ".MealWeekWorker", ConstitutionIdentity.MealWeekWorkerName);
        Assert.Equal("constitution-meal-week", ConstitutionIdentity.MealWeekWorkName);
    }

    [Fact]
    public void BothAreInTheFrozenLists()
    {
        Assert.Contains(ConstitutionIdentity.ApplicationId + ".MealWeekWorker", ConstitutionIdentity.Workers);
        Assert.Contains("constitution-meal-week", ConstitutionIdentity.WorkNames);
    }

    [Fact]
    public void TheWorkerRegistersUnderTheFrozenName()
    {
        Assert.Equal(ConstitutionIdentity.MealWeekWorkerName, BodySyncWorkerGuardTests.RegisteredName(Source("MealWeekWorker.cs")));
    }

    [Fact]
    public void TheWorkerKeepsUnderTheFrozenWorkName()
    {
        var source = Source("MealWeekWorker.cs");

        Assert.Contains("EnqueueUniquePeriodicWork(ConstitutionIdentity.MealWeekWorkName", source, StringComparison.Ordinal);
        Assert.Contains("ExistingPeriodicWorkPolicy.Keep", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExistingPeriodicWorkPolicy.Replace", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExistingPeriodicWorkPolicy.Update", source, StringComparison.Ordinal);
        Assert.Contains("NetworkType.Connected", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkerUsesItsOwnScheduleAndIntervalNotTheWorkoutOnes()
    {
        var source = Source("MealWeekWorker.cs");

        Assert.Contains("MealPassSchedule.DelayUntilNext(", source, StringComparison.Ordinal);
        Assert.Contains("MealPassSchedule.Interval", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkoutWeekSchedule", source, StringComparison.Ordinal);
    }

    // ---- the wiring ----

    [Fact]
    public void TheMainThreadNeverWaitsOnTheNetwork()
    {
        var source = Source("MealWeekWorker.cs");

        Assert.Contains("Task.Run(", source, StringComparison.Ordinal);
        var open = source[source.IndexOf("RunOnOpen", StringComparison.Ordinal)..];
        Assert.DoesNotContain(".GetAwaiter().GetResult()", open, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkerLogsOnlyTheTypeNameAndSwallowsNothing()
    {
        var source = Source("MealWeekWorker.cs");

        Assert.Contains("e.GetType().Name", source, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Message", source, StringComparison.Ordinal);
        Assert.DoesNotContain("e.ToString()", source, StringComparison.Ordinal);
        Assert.Contains("ThrowIfFaulted()", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"catch\s*(\([^)]*\))?\s*(when\s*\([^)]*\)\s*)?\{\s*\}", RegexOptions.None, TimeSpan.FromSeconds(2)), source);
    }

    [Fact]
    public void TheWorkerGoesThroughTheServicesAndTheirGate()
    {
        var worker = Source("MealWeekWorker.cs");
        var services = Source("MealWeekServices.cs");

        Assert.Contains("MealWeekServices.PassAsync(", worker, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "MealWeekRun", "MealDeadlineStep", "PlacementRunner", "RunGate" })
        {
            Assert.DoesNotContain(forbidden, worker, StringComparison.Ordinal);
        }

        // Both halves of the pass take their turn on the one process-wide gate the workout run uses.
        Assert.Contains("RunGate.Shared.RunAsync(token => RunAloneAsync(", services, StringComparison.Ordinal);
        Assert.Contains("RunGate.Shared.RunAsync(token => DecideAloneAsync(", services, StringComparison.Ordinal);
        Assert.DoesNotContain("new RunGate(", services, StringComparison.Ordinal);
    }

    [Fact]
    public void TheActivitySchedulesMealsBeforeWorkouts()
    {
        var activity = Source("ConstitutionActivity.cs");

        var mealSchedule = activity.IndexOf("MealWeekWorker.Schedule(this)", StringComparison.Ordinal);
        var workoutSchedule = activity.IndexOf("WorkoutWeekWorker.Schedule(this)", StringComparison.Ordinal);
        var mealOpen = activity.IndexOf("MealWeekWorker.RunOnOpen(this)", StringComparison.Ordinal);
        var workoutOpen = activity.IndexOf("WorkoutWeekWorker.RunOnOpen(this)", StringComparison.Ordinal);

        Assert.True(mealSchedule >= 0 && mealOpen >= 0, "The activity does not schedule the meal worker and run it on open.");
        Assert.True(mealSchedule < workoutSchedule, "Meals are scheduled after workouts.");
        Assert.True(mealOpen < workoutOpen, "The meal run on open starts after the workout run.");
    }

    // ---- what it must not touch ----

    [Fact]
    public void NoMealWeekFileTouchesTheVaultPassphrase()
    {
        var files = MealWeekHeadFiles().ToList();
        Assert.True(files.Count >= 2, "The meal week's head folder looks empty; the guard is not reading it.");

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var word in new[] { "assphrase", "VaultSync", "SecretsVault", "VaultPassphrase" })
            {
                Assert.False(text.Contains(word, StringComparison.OrdinalIgnoreCase), $"{ConstitutionSource.Relative(file)} names {word}");
            }
        }
    }

    [Fact]
    public void TheFolderOpensNoWayOffThePhoneAndAsksForNoPermission()
    {
        foreach (var file in MealWeekHeadFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var word in new[] { "RequestPermissions", "UsesPermission", "uses-permission", "Intent(", "StartActivity", "SendBroadcast", "ClipboardManager", "NotificationCompat" })
            {
                Assert.False(text.Contains(word, StringComparison.Ordinal), $"{ConstitutionSource.Relative(file)} names {word}");
            }
        }
    }

    [Fact]
    public void TheMergedReleaseManifestHasSeventeenPermissions()
    {
        var path = Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "obj", "Release", "net10.0-android", "android", "AndroidManifest.xml");
        if (!File.Exists(path))
        {
            // No Release build here: the source half above is what runs. The recipe makes one and reads this file.
            return;
        }

        var requested = XDocument.Load(path).Descendants("uses-permission")
            .Select(element => (string?)element.Attribute(Android + "name"))
            .OfType<string>()
            .ToList();

        // Sixteen, plus POST_NOTIFICATIONS for the brief's short list of notices (C-9.1.3, D155): the one new permission, nothing else.
        Assert.Equal(17, requested.Count);
        Assert.Contains("android.permission.POST_NOTIFICATIONS", requested);
        Assert.Equal(17, requested.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(requested, name => name.Contains("MealWeek", StringComparison.Ordinal));
    }
}
