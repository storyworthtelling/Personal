using System.Text.RegularExpressions;

namespace Constitution.Tests.MealWeekDocuments;

/// <summary>What the documents step must and must not do, read from the files themselves.</summary>
public class MealDocumentGuardTests
{
    private static string Text(string suffix) =>
        File.ReadAllText(ConstitutionSource.Files(".cs").Single(file => ConstitutionSource.Relative(file).EndsWith(suffix, StringComparison.Ordinal)));

    [Fact]
    public void TheStepRecordsForTheReadBackOnlyAfterTheTabletTookTheFile()
    {
        var step = Text("Constitution.Infrastructure/Meals/MealDocumentsStep.cs");

        var recording = Assert.Single(Regex.Matches(step, @"seams\.Expected\.Record\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)));
        var before = step[..recording.Index];
        Assert.True(
            before.LastIndexOf("if (!sent.Result.Delivered)", StringComparison.Ordinal) > before.LastIndexOf("delivery.SendAsync(", StringComparison.Ordinal),
            "The result of the upload is checked between the send and the record.");
        Assert.True(before.LastIndexOf("MealDocumentVerdict.NotDelivered", StringComparison.Ordinal) > before.LastIndexOf("sent.Kept", StringComparison.Ordinal));
        Assert.Contains("Delivered.Record(", step[recording.Index..], StringComparison.Ordinal);
    }

    [Fact]
    public void TheStepNeverTouchesTheMealAnswers()
    {
        var step = Text("Constitution.Infrastructure/Meals/MealDocumentsStep.cs");

        Assert.DoesNotContain("MealAnswerRecords", step, StringComparison.Ordinal);
        Assert.DoesNotContain("DownloadByTitle", step, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadReusesTheWorkoutRunsDeliverySide()
    {
        var services = Text("Constitution.Android/MealWeek/MealWeekServices.cs");

        Assert.Contains("WorkoutWeekDeliverySeams.Create(", services, StringComparison.Ordinal);
        Assert.Contains("MealDocumentSeams(", services, StringComparison.Ordinal);
        Assert.Contains("new FileDeliveredMealDocuments(", services, StringComparison.Ordinal);
        Assert.Contains("new UnavailableHolidayCalendar()", services, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadKeepsNoSecretAndAddsNoWorker()
    {
        var services = Text("Constitution.Android/MealWeek/MealWeekServices.cs");

        Assert.DoesNotContain("Passphrase", services, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ApiKey", services, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkManager", services, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDeliveryPutsNoModelOrKeyInTheFiles()
    {
        var files = new[] { "MealDocumentsStep.cs", "MealDocumentDelivery.cs", "FileDeliveredMealDocuments.cs", "MealDocumentSeams.cs" };
        foreach (var file in files)
        {
            var text = Text("Constitution.Infrastructure/Meals/" + file);
            Assert.DoesNotContain("claude-", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ApiKey", text, StringComparison.Ordinal);
        }
    }
}
