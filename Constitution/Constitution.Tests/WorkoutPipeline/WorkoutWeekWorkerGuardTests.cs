using Constitution.Domain;
using Constitution.Tests.Body;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>
/// P13 — the weekly workout worker's registered Java name and unique work name, held to the frozen constants by reading its
/// source, in the style of the read-back worker's guard: the names, the Keep policy, the shared schedule and debounce, the
/// network constraint, nothing on the main thread, and no new way off the phone.
/// </summary>
public class WorkoutWeekWorkerGuardTests
{
    private static string Source(string fileName)
    {
        var file = ConstitutionSource.Files(".cs").SingleOrDefault(path => Path.GetFileName(path) == fileName);
        Assert.True(file is not null, fileName + " was not found under Constitution/.");
        return File.ReadAllText(file!);
    }

    [Fact]
    public void TheNamesAreTheFrozenLiterals()
    {
        Assert.Equal(ConstitutionIdentity.ApplicationId + ".WorkoutWeekWorker", ConstitutionIdentity.WorkoutWeekWorkerName);
        Assert.Equal("constitution-workout-week", ConstitutionIdentity.WorkoutWeekWorkName);
    }

    [Fact]
    public void TheWorkerRegistersUnderTheFrozenName()
    {
        Assert.Equal(ConstitutionIdentity.WorkoutWeekWorkerName, BodySyncWorkerGuardTests.RegisteredName(Source("WorkoutWeekWorker.cs")));
    }

    [Fact]
    public void TheWorkerKeepsUnderTheFrozenWorkName()
    {
        var source = Source("WorkoutWeekWorker.cs");

        Assert.Contains("EnqueueUniquePeriodicWork(ConstitutionIdentity.WorkoutWeekWorkName", source, StringComparison.Ordinal);
        Assert.Contains("ExistingPeriodicWorkPolicy.Keep", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExistingPeriodicWorkPolicy.Replace", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExistingPeriodicWorkPolicy.Update", source, StringComparison.Ordinal);
        Assert.Contains("NetworkType.Connected", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkerUsesTheSharedScheduleAndDebounce()
    {
        var source = Source("WorkoutWeekWorker.cs");

        Assert.Contains("WorkoutWeekSchedule.DelayUntilNext(", source, StringComparison.Ordinal);
        Assert.Contains("WorkoutWeekDebounce.ShouldRun(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FromHours(6", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMainThreadNeverWaitsOnTheNetwork()
    {
        var source = Source("WorkoutWeekWorker.cs");

        // DoWork runs on WorkManager's own thread and may block; the open path must hand off to the pool.
        Assert.Contains("Task.Run(", source, StringComparison.Ordinal);
        var open = source[source.IndexOf("RunOnOpen", StringComparison.Ordinal)..];
        Assert.DoesNotContain(".GetAwaiter().GetResult()", open, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkerLogsOnlyTheTypeName()
    {
        var source = Source("WorkoutWeekWorker.cs");

        Assert.Contains("e.GetType().Name", source, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Message", source, StringComparison.Ordinal);
        Assert.DoesNotContain("e.ToString()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheServicesKeepTheirSignatureAndTrail()
    {
        var source = Source("WorkoutWeekServices.cs");

        Assert.Contains("Task<WorkoutWeekOutcome> RunAsync(Context context, CancellationToken cancellation)", source, StringComparison.Ordinal);
        Assert.Contains("NotPlannedBecause.NotConfigured", source, StringComparison.Ordinal);
        Assert.Contains("WorkoutWeekTrail.LogPhrase(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheActivitySchedulesAndRunsOnOpen()
    {
        var activity = Source("ConstitutionActivity.cs");

        Assert.Contains("WorkoutWeekWorker.Schedule(this)", activity, StringComparison.Ordinal);
        Assert.Contains("WorkoutWeekWorker.RunOnOpen(this)", activity, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFolderOpensNoWayOffThePhone()
    {
        var files = ConstitutionSource.Files(".cs").Where(path => ConstitutionSource.Relative(path).Contains("Constitution.Android/WorkoutWeek/", StringComparison.Ordinal)).ToList();
        Assert.True(files.Count >= 2, "The workout week's head folder looks empty; the guard is not reading it.");

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var word in new[] { "RequestPermissions", "[assembly: UsesPermission", "Intent(", "StartActivity", "SendBroadcast", "ClipboardManager", "NotificationCompat" })
            {
                Assert.False(text.Contains(word, StringComparison.Ordinal), $"{ConstitutionSource.Relative(file)} names {word}");
            }
        }
    }
}
