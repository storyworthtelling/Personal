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
    public void OnlyTheMealServicesCallTheStep()
    {
        // The meal worker's pass is the step's one caller, through MealWeekServices.TryDecideAsync and so through the shared run gate (D114).
        var callers = ConstitutionSource.Files(".cs")
            .Select(ConstitutionSource.Relative)
            .Where(path => !path.StartsWith("Constitution.Tests/", StringComparison.Ordinal) && !path.EndsWith("Infrastructure/Meals/MealDeadlineStep.cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => ConstitutionSource.Relative(file) == path))
                .Contains("MealDeadlineStep.RunAsync(", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(["Constitution.Android/MealWeek/MealWeekServices.cs"], callers);
    }

    [Fact]
    public void TheStepNeverReachesThePaperOrPlacesAnything()
    {
        var step = Text("Infrastructure/Meals/MealDeadlineStep.cs");

        Assert.DoesNotContain("MealDocumentsStep.RunAsync(", step, StringComparison.Ordinal);
        Assert.DoesNotContain("PlacementRunner", step, StringComparison.Ordinal);
        Assert.DoesNotContain("ICalendarWriter", step, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAsync(", step, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOnlyCalendarDoorTheStepHasOnlyLooksUpAndRemoves()
    {
        var door = Text("Infrastructure/Meals/DeniedEveningCalendar.cs");

        Assert.Contains("FindAsync(", door, StringComparison.Ordinal);
        Assert.Contains("RemoveAsync(", door, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateAsync(", door, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveAsync(", door, StringComparison.Ordinal);
        Assert.DoesNotContain("PlacementRunner", door, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadHandsTheStepTheMealCalendar()
    {
        var services = Text("Android/MealWeek/MealWeekServices.cs");

        Assert.Contains("new MealDeadlineCalendar(", services, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStepSavesTheDecisionLast()
    {
        var step = Text("Infrastructure/Meals/MealDeadlineStep.cs");

        var notes = step.IndexOf("seams.Notes.Save(", StringComparison.Ordinal);
        var library = step.IndexOf("seams.Library.Save(", StringComparison.Ordinal);
        var week = step.IndexOf("seams.Weeks.Save(", StringComparison.Ordinal);
        var decision = step.IndexOf("seams.Decisions.Save(", week, StringComparison.Ordinal);

        Assert.True(notes >= 0 && notes < library && library < week && week < decision);
    }
}
