using System.Text.RegularExpressions;

namespace Constitution.Tests.ClaudeTasteJudge;

/// <summary>What the judge's files must not hold and where the key may be read, by content: the head cannot be loaded here.</summary>
public partial class TasteJudgeWiringGuardTests
{
    private const string JudgePath = "Constitution.Infrastructure/Meals/ClaudeTasteJudge.cs";
    private const string ServicesPath = "Constitution.Android/Services/TasteJudgeServices.cs";
    private const string WeekPath = "Constitution.Android/MealWeek/MealWeekServices.cs";
    private const string ReplyPath = "Constitution.Domain/Meals/TasteJudgeReply.cs";
    private const string LimitsPath = "Constitution.Domain/Meals/TasteJudgeLimits.cs";
    private const string SourcePath = "Constitution.Infrastructure/Meals/TasteFromHousehold.cs";

    private static string Read(string relative) => File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative));

    [GeneratedRegex(@"claude-[a-z0-9]|sonnet|haiku|opus", RegexOptions.IgnoreCase)]
    private static partial Regex AModelName();

    [GeneratedRegex(@"catch\s*(\([^)]*\))?\s*(when\s*\([^)]*\)\s*)?\{\s*(//[^\n]*\s*)?\}")]
    private static partial Regex AnEmptyCatch();

    [GeneratedRegex("\"(?:[^\"\\\\]|\\\\.)*\"|//[^\n]*")]
    private static partial Regex StringsAndComments();

    [GeneratedRegex(@"\b_\w|\w_\w|\w_\b")]
    private static partial Regex AnUnderscore();

    private static IEnumerable<string> Files() => [Read(JudgePath), Read(ServicesPath), Read(ReplyPath), Read(LimitsPath), Read(SourcePath)];

    [Fact]
    public void NoModelIdentifierIsNamed()
    {
        foreach (var text in Files())
        {
            Assert.Empty(AModelName().Matches(text));
        }
    }

    [Fact]
    public void NoCatchIsEmpty()
    {
        foreach (var text in Files())
        {
            Assert.Empty(AnEmptyCatch().Matches(text));
        }
    }

    [Fact]
    public void NoIdentifierHoldsAnUnderscore()
    {
        foreach (var text in Files())
        {
            Assert.Empty(AnUnderscore().Matches(StringsAndComments().Replace(text, string.Empty)));
        }
    }

    [Fact]
    public void TheKeyIsReadOnlyInTheServices()
    {
        Assert.DoesNotContain("ClaudeApiKey", Read(JudgePath), StringComparison.Ordinal);
        Assert.DoesNotContain("ConstitutionConnections", Read(JudgePath), StringComparison.Ordinal);
        Assert.DoesNotContain("ClaudeApiKey", Read(WeekPath), StringComparison.Ordinal);
        Assert.Contains("ClaudeApiKey", Read(ServicesPath), StringComparison.Ordinal);
    }

    [Fact]
    public void TheKeyIsNeverLoggedOrBuiltIntoText()
    {
        var judge = Read(JudgePath);
        Assert.DoesNotMatch(@"(Console|Log|Debug|Trace)\w*\.\w+\([^;]*ApiKey", judge);
        Assert.DoesNotMatch(@"\$""[^""]*\{[^}]*ApiKey", judge);
        Assert.DoesNotMatch(@"Detail:?\s*[^;]*ApiKey", judge);
    }

    [Fact]
    public void TheJudgeNeverNamesAPersonTheRosterOrACalendar()
    {
        var judge = Read(JudgePath) + Read(ReplyPath);
        foreach (var word in new[] { "Roster", "Birthdate", "Calendar", "evidence.Person", "PersonId", "TastePerson" })
        {
            Assert.DoesNotContain(word, judge, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRunIsHandedTheRealTasteSourceAndJudge()
    {
        var week = Read(WeekPath);

        Assert.DoesNotContain("new NoMealTaste()", week, StringComparison.Ordinal);
        Assert.DoesNotContain("new UnavailableTasteJudge()", week, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(week, Regex.Escape("new TasteFromHousehold(household.Service)")).Count);
        Assert.Contains("TasteJudgeServices.JudgeOf(", week, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWeekServicesDoNotNameTheProfile()
    {
        var week = Read(WeekPath);

        foreach (var word in new[] { "TasteProfile", "PersonTaste", "TastePlannerInputs" })
        {
            Assert.DoesNotContain(word, week, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheStepAsksOnlyThroughTheBoundedJudge()
    {
        var step = Read("Constitution.Infrastructure/Meals/MealDeadlineStep.cs");

        Assert.Contains("new BoundedTasteJudge(seams.Judge", step, StringComparison.Ordinal);
        Assert.DoesNotContain("LearnAsync(evidence, seams.Judge", step, StringComparison.Ordinal);
    }
}
