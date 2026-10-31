using Constitution.Domain;
using Constitution.Infrastructure.Meals;
using Constitution.Domain.Placement;

namespace Constitution.Tests.RecipeHour;

/// <summary>The recipe hour held to its frozen name and its wiring by reading the source, as the meal worker's own guard does. Nothing here has run on a phone.</summary>
public class RecipeHourGuardTests
{
    private static string Source(string fileName)
    {
        var file = ConstitutionSource.Files(".cs").SingleOrDefault(path => Path.GetFileName(path) == fileName);
        Assert.True(file is not null, fileName + " was not found under Constitution/.");
        return File.ReadAllText(file!);
    }

    // ---- the frozen name ----

    [Fact]
    public void TheSecondWorkNameIsTheFrozenLiteral()
    {
        Assert.Equal("constitution-meal-recipe-hour", ConstitutionIdentity.MealRecipeHourWorkName);
        Assert.Contains("constitution-meal-recipe-hour", ConstitutionIdentity.WorkNames);
    }

    [Fact]
    public void TheWorkerClassAndItsRegisteredNameAreUnchanged()
    {
        Assert.Equal(ConstitutionIdentity.ApplicationId + ".MealWeekWorker", ConstitutionIdentity.MealWeekWorkerName);
        Assert.Equal(5, ConstitutionIdentity.Workers.Count);
        Assert.Equal("constitution-meal-week", ConstitutionIdentity.MealWeekWorkName);
    }

    // ---- the worker ----

    [Fact]
    public void BothRequestsAreEnqueuedByTheOneScheduleCallUnderKeep()
    {
        var source = Source("MealWeekWorker.cs");

        Assert.Contains("EnqueueUniquePeriodicWork(ConstitutionIdentity.MealWeekWorkName", source, StringComparison.Ordinal);
        Assert.Contains("EnqueueUniquePeriodicWork(ConstitutionIdentity.MealRecipeHourWorkName", source, StringComparison.Ordinal);
        Assert.Equal(2, CountOf(source, "ExistingPeriodicWorkPolicy.Keep"));
        Assert.DoesNotContain("ExistingPeriodicWorkPolicy.Replace", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExistingPeriodicWorkPolicy.Update", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSecondRequestIsAimedAtItsOwnMinute()
    {
        var source = Source("MealWeekWorker.cs");

        Assert.Contains("Request(MealPassSchedule.DelayUntilNextRecipeLook(", source, StringComparison.Ordinal);
        Assert.Contains("Request(MealPassSchedule.DelayUntilNext(", source, StringComparison.Ordinal);
        Assert.Contains("MealPassSchedule.Interval", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BothRequestsAreBuiltTheSameWayAndNeedTheNetworkBecauseTheTabletIsOverIt()
    {
        var source = Source("MealWeekWorker.cs");

        Assert.Equal(1, CountOf(source, "new PeriodicWorkRequest.Builder("));
        Assert.Equal(1, CountOf(source, "NetworkType.Connected"));
        Assert.Equal(2, CountOf(source, " = Request("));
    }

    // ---- the services ----

    [Fact]
    public void TheRecipeEntryTakesItsTurnOnTheSharedGate()
    {
        var services = Source("MealWeekServices.cs");

        Assert.Contains("RunGate.Shared.RunAsync(token => RecipesAloneAsync(", services, StringComparison.Ordinal);
        Assert.DoesNotContain("new RunGate(", services, StringComparison.Ordinal);
    }

    // ---- the run ----

    [Fact]
    public void TheRunReusesTheDocumentsStepAndNothingThatPlansOrWrites()
    {
        var source = Source("MealRecipeHourRun.cs");

        Assert.Contains("MealDocumentsStep.RunAsync(", source, StringComparison.Ordinal);
        Assert.Contains("RecipesOnly = true", source, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "Planner", "PlacementRunner", "CalendarWriter", "WrittenKeys", "KeyAssignments", "Records.Save", "Lease", "MealTemplateStep", "MealWeekJoin", "Calendars" })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRunsSeamsHaveNoWayToReachACalendarOrSaveAWeek()
    {
        var members = typeof(MealRecipeSeams).GetProperties().Select(property => property.PropertyType).ToList();

        Assert.DoesNotContain(members, type => typeof(ICalendarWriter).IsAssignableFrom(type) || typeof(IWrittenKeyStore).IsAssignableFrom(type) || typeof(IKeyAssignmentStore).IsAssignableFrom(type));
        Assert.Contains(typeof(MealDocumentSeams), members);
    }

    [Fact]
    public void NothingThrowsAwayAFailureWithAnEmptyCatch()
    {
        foreach (var file in new[] { "MealRecipeHourRun.cs", "MealRecipeHour.cs" })
        {
            Assert.DoesNotMatch(@"catch\s*(\([^)]*\))?\s*(when\s*\([^)]*\)\s*)?\{\s*\}", Source(file));
        }
    }

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        for (var at = text.IndexOf(needle, StringComparison.Ordinal); at >= 0; at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
