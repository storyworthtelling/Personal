using System.Text.RegularExpressions;
using Constitution.Domain.Household;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Meals;

namespace Constitution.Tests.SharedWeek;

/// <summary>
/// The other phone's week held to its rules by reading the source: the kind is the frozen word, only the lease holder's run writes a week-plan record,
/// and the path that serves the other adult's tablet has no way to plan, place or save. Nothing here has run on a phone.
/// </summary>
public class SharedWeekGuardTests
{
    private static string Source(string fileName)
    {
        var file = ConstitutionSource.Files(".cs").SingleOrDefault(path => Path.GetFileName(path) == fileName);
        Assert.True(file is not null, fileName + " was not found under Constitution/.");
        return File.ReadAllText(file!);
    }

    private static IEnumerable<string> Sources() =>
        ConstitutionSource.Files(".cs").Where(path => !ConstitutionSource.Relative(path).StartsWith("Constitution.Tests/", StringComparison.Ordinal));

    [Fact]
    public void TheKindIsTheFrozenLiteral()
    {
        Assert.Equal("week-plan", ConstitutionKinds.WeekPlan);
        Assert.Contains("const string WeekPlan = \"week-plan\";", Source("ConstitutionKinds.cs"), StringComparison.Ordinal);
        Assert.Equal("week-plan", Domain.Household.Records.WeekPlanMaps.WeekPlan.Kind);
    }

    [Fact]
    public void TheWordsOfTheRecordAreLiteralsNotNameof()
    {
        var source = Source("WeekPlanMaps.cs");

        Assert.DoesNotContain("nameof(", source, StringComparison.Ordinal);
        foreach (var field in new[] { "\"format\"", "\"weekStart\"", "\"week\"", "\"planned\"", "\"dishes\"", "\"deadline\"", "\"extra\"", "\"daily\"" })
        {
            Assert.Contains(field, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheOtherAdultsPathHasNoWayToPlanPlaceOrSave()
    {
        var source = Source("MealSharedWeekRun.cs");

        foreach (var forbidden in new[]
        {
            "ICalendarWriter", "PlacementRunner", "CalendarWriter", "WrittenKeys", "KeyAssignments", "IWrittenKeyStore", "Planner", "Lease", "Records.Save", "MealTemplateStep", "MealWeekJoin",
            "Calendars", "History", "HouseholdRecords.Put", "EditAsync", "IWeekPlanPublisher",
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }

        Assert.Contains("MealDocumentsStep.RunAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ItsSeamsHoldNothingToWriteWith()
    {
        var members = typeof(MealSharedSeams).GetProperties().Select(property => property.PropertyType).ToList();

        Assert.DoesNotContain(members, type => typeof(Domain.Placement.ICalendarWriter).IsAssignableFrom(type) || typeof(IWeekPlanPublisher).IsAssignableFrom(type) || typeof(IMealWeekRecords).IsAssignableFrom(type));
        Assert.Contains(typeof(IWeekPlanSource), members);
    }

    [Fact]
    public void TheReadersOfWeekPlansCannotWrite()
    {
        Assert.DoesNotContain("EditAsync", Source("HouseholdWeekPlans.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("HouseholdRecords.Put", Source("HouseholdWeekPlans.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("HouseholdRecords.Remove", Source("HouseholdWeekPlans.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("HouseholdRecords.Put", Source("KeptWeeksBesideTheHousehold.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("EditAsync", Source("KeptWeeksBesideTheHousehold.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyThePublisherPutsAWeekPlanIntoTheFile()
    {
        var writers = Sources()
            .Where(path => File.ReadAllText(path).Contains("WeekPlanMaps.WeekPlan", StringComparison.Ordinal))
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"HouseholdRecords\.(Put|Remove)\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal(["HouseholdWeekPlanPublisher.cs"], writers);
    }

    [Fact]
    public void OnlyTheWeeklyRunCallsThePublisherAndOnlyAfterTheLease()
    {
        var callers = Sources()
            .Where(path => File.ReadAllText(path).Contains("seams.Publisher", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToList();
        var run = Source("MealWeekRun.cs");

        Assert.Equal(["MealWeekRun.cs"], callers);
        Assert.True(run.IndexOf("seams.Lease(", StringComparison.Ordinal) < run.IndexOf("publisher.PublishAsync(", StringComparison.Ordinal));
        Assert.True(run.IndexOf("seams.Records.Save(", StringComparison.Ordinal) < run.IndexOf("publisher.PublishAsync(", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePublisherNeverCreatesTheFileOrBypassesTheServiceThatRefusesAnUnreadableOne()
    {
        var source = Source("HouseholdWeekPlanPublisher.cs");

        Assert.Contains("BlockedSentence", source, StringComparison.Ordinal);
        Assert.Contains("service.EditAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DriveHouseholdStore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IDriveTextFile", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingHereThrowsAwayAFailureWithAnEmptyCatch()
    {
        foreach (var file in new[]
        {
            "MealSharedWeekRun.cs", "HouseholdWeekPlans.cs", "HouseholdWeekPlanPublisher.cs", "KeptWeeksBesideTheHousehold.cs", "SharedWeeks.cs", "WeekPlanMaps.cs",
        })
        {
            Assert.DoesNotMatch(@"catch\s*(\([^)]*\))?\s*(when\s*\([^)]*\)\s*)?\{\s*\}", Source(file));
        }
    }

    [Fact]
    public void TheNewFilesHoldNoUnderscoreIdentifierAndNoModelName()
    {
        foreach (var file in new[]
        {
            "MealSharedWeekRun.cs", "HouseholdWeekPlans.cs", "HouseholdWeekPlanPublisher.cs", "KeptWeeksBesideTheHousehold.cs", "SharedWeeks.cs", "WeekPlanMaps.cs", "WeekPlanPublication.cs",
        })
        {
            var source = Source(file);
            Assert.DoesNotMatch(@"\b_[A-Za-z]\w*", Regex.Replace(source, "\"[^\"]*\"|//[^\n]*", string.Empty));
            Assert.DoesNotContain("claude-", source, StringComparison.OrdinalIgnoreCase);
        }
    }
}
