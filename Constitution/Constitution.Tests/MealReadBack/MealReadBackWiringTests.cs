using System.Text.RegularExpressions;
using Constitution.WorkoutDelivery;

namespace Constitution.Tests.MealReadBack;

/// <summary>The head's assembly of the read-back, held by reading its source, as the other wiring guards do.</summary>
public class MealReadBackWiringTests
{
    private static string Source(string fileName)
    {
        var file = ConstitutionSource.Files(".cs").SingleOrDefault(path => Path.GetFileName(path) == fileName);
        Assert.True(file is not null, fileName + " was not found under Constitution/.");
        return File.ReadAllText(file!);
    }

    [Fact]
    public void TheReadBackIsToldByTheWorkoutsAndTheMealsInThatOrder()
    {
        var source = Source("ReadBackServices.cs");

        var order = new[] { "new SettledHooks(", "new FileWorkoutLearning(", "new MealReadBackSettled(" };
        Assert.All(order, part => Assert.Contains(part, source, StringComparison.Ordinal));
        Assert.True(source.IndexOf("new FileWorkoutLearning(", StringComparison.Ordinal) < source.IndexOf("new MealReadBackSettled(", StringComparison.Ordinal));
        Assert.Matches(new Regex(@"DateTimeOffset\.Now,\s*\w+\);", RegexOptions.None, TimeSpan.FromSeconds(1)), source);
    }

    [Fact]
    public void TheMealsAreRecordedInTheHouseholdFileServiceTheAppUses()
    {
        var source = Source("ReadBackServices.cs");

        Assert.Contains("ConstitutionHousehold.For(", source, StringComparison.Ordinal);
        Assert.Contains(".Service", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSkipLedgerTheWorkoutsLearnFromIsTheReadBacksOwn()
    {
        var source = Source("ReadBackServices.cs");

        Assert.Contains("new FileWorkoutLearning(LedgerOf(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMealRunRecordsIntoTheReadBacksOwnExpectedFile()
    {
        var meals = Source("MealWeekServices.cs");
        var seams = Source("WorkoutWeekDeliverySeams.cs");

        Assert.Contains("delivery.Expected", meals, StringComparison.Ordinal);
        Assert.DoesNotContain("new FileExpectedDocuments", meals, StringComparison.Ordinal);
        Assert.Contains("ReadBackServices.ExpectedOf(", seams, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLearningFolderIsOneSpellingForBothUsers()
    {
        const string root = "/invented/no-backup";

        Assert.Equal(Path.Combine(root, "workout-week", "learning"), WorkoutWeekPaths.Learning(root));
        Assert.Contains("WorkoutWeekPaths.LearningFolder", Source("WorkoutWeekCalendarSeams.cs"), StringComparison.Ordinal);
        Assert.Contains("WorkoutWeekPaths.Learning(", Source("ReadBackServices.cs"), StringComparison.Ordinal);
    }
}
