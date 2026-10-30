using Constitution.Tests.Diagnostics;

namespace Constitution.Tests.TemplatesInTheRun;

/// <summary>Where the templates step sits in the run and what the head hands it, read from the source because the head cannot be loaded here.</summary>
public class TemplateWiringGuardTests
{
    private static string Text(string suffix) =>
        File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => ConstitutionSource.Relative(file).EndsWith(suffix, StringComparison.Ordinal)));

    [Fact]
    public void TheStepRunsAfterTheLeaseAndBeforeThePaper()
    {
        var run = Text("Infrastructure/Meals/MealWeekRun.cs");

        var lease = run.IndexOf("seams.Lease(", StringComparison.Ordinal);
        var saved = run.IndexOf("seams.Records.Save(", StringComparison.Ordinal);
        var templates = run.IndexOf("MealTemplateStep.RunAsync(", StringComparison.Ordinal);
        var documents = run.IndexOf("MealDocumentsStep.RunAsync(", StringComparison.Ordinal);

        Assert.True(lease >= 0 && lease < saved && saved < templates && templates < documents);
    }

    [Fact]
    public void TheHeadHandsOverTheHouseholdLibraryAndNoAuthorYet()
    {
        var services = Text("Android/MealWeek/MealWeekServices.cs");

        Assert.Contains("new HouseholdTemplateLibrary(household.Service)", services, StringComparison.Ordinal);
        Assert.Contains("new NoTemplateAuthor()", services, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePaperTakesItsDailyMealsFromTheRequest()
    {
        var step = Text("Infrastructure/Meals/MealDocumentsStep.cs");

        Assert.Contains("Daily = request.Daily", step, StringComparison.Ordinal);
    }
}
