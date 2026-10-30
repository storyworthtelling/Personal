using Constitution.Tests.Diagnostics;

namespace Constitution.Tests.MealDeadlineStep;

/// <summary>What the head hands the deadline step and what nothing yet calls, read from the source because the head cannot be loaded here.</summary>
public class MealDeadlineWiringGuardTests
{
    private static string Text(string suffix) =>
        File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => ConstitutionSource.Relative(file).EndsWith(suffix, StringComparison.Ordinal)));

    [Fact]
    public void TheHeadHandsOverTheHouseholdAnswersLibraryAndNotes()
    {
        var services = Text("Android/MealWeek/MealWeekServices.cs");

        Assert.Contains("new HouseholdMealAnswers(household.Service)", services, StringComparison.Ordinal);
        Assert.Contains("new HouseholdTemplateLibrary(household.Service)", services, StringComparison.Ordinal);
        Assert.Contains("new HouseholdTasteNotes(household.Service)", services, StringComparison.Ordinal);
        Assert.Contains("new FileMealDecisionRecords(", services, StringComparison.Ordinal);
        Assert.Contains("household.Planning.AskAsync", services, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadSaysThereIsNoJudgeYet()
    {
        Assert.Contains("new UnavailableTasteJudge()", Text("Android/MealWeek/MealWeekServices.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void NothingInTheAppCallsTheStepYet()
    {
        var callers = ConstitutionSource.Files(".cs")
            .Select(ConstitutionSource.Relative)
            .Where(path => !path.StartsWith("Constitution.Tests/", StringComparison.Ordinal) && !path.EndsWith("Infrastructure/Meals/MealDeadlineStep.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => ConstitutionSource.Relative(file) == path))
                .Contains("MealDeadlineStep.RunAsync(", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(callers);
    }

    [Fact]
    public void TheStepNeverReachesTheCalendarOrThePaper()
    {
        var step = Text("Infrastructure/Meals/MealDeadlineStep.cs");

        Assert.DoesNotContain("MealDocumentsStep.RunAsync(", step, StringComparison.Ordinal);
        Assert.DoesNotContain("PlacementRunner", step, StringComparison.Ordinal);
        Assert.DoesNotContain("ICalendarWriter", step, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStepSavesTheDecisionLast()
    {
        var step = Text("Infrastructure/Meals/MealDeadlineStep.cs");

        var notes = step.IndexOf("seams.Notes.Save(", StringComparison.Ordinal);
        var library = step.IndexOf("seams.Library.Save(", StringComparison.Ordinal);
        var decision = step.IndexOf("seams.Decisions.Save(", StringComparison.Ordinal);

        Assert.True(notes >= 0 && notes < library && library < decision);
    }
}
