using Constitution.Domain.Diagnostics;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;

namespace Constitution.Tests.MealDeadlineStep;

/// <summary>The deadline step's lines in the Body trail: fixed words and counts only, each one past the export's filter whole.</summary>
public class MealDeadlineTrailTests
{
    private const string Stamp = "[Oct 25, 3:05 pm] INFO Body — ";

    private static MealDeadlineReport Report(bool revised = false, bool retry = false, int unjudged = 0) =>
        new(new DateOnly(2026, 10, 25), 6, 1, 2, 1, 1, 2, 1, 0, unjudged, revised, revised ? [new RecipeId("lentil-curry")] : [], retry);

    public static IEnumerable<object[]> EveryPhrase()
    {
        yield return [new MealDeadlineOutcome.NotThePlanner("The other phone holds this week's planning.")];
        foreach (var because in Enum.GetValues<MealDeadlineNothingBecause>())
        {
            yield return [new MealDeadlineOutcome.NothingToDecide(because, "A sentence.")];
        }

        foreach (var because in Enum.GetValues<MealDeadlineStoppedBecause>())
        {
            yield return [new MealDeadlineOutcome.Stopped(because, "A sentence.")];
        }

        yield return [new MealDeadlineOutcome.Decided(Report())];
        yield return [new MealDeadlineOutcome.Decided(Report(revised: true, unjudged: 2))];
        yield return [new MealDeadlineOutcome.Decided(Report(retry: true))];
    }

    [Theory]
    [MemberData(nameof(EveryPhrase))]
    public void EveryOutcomeHasAPhraseTheFilterKeepsWhole(MealDeadlineOutcome outcome)
    {
        var line = Stamp + MealDeadlineTrail.LogPhrase(outcome);

        Assert.StartsWith("meal deadline", MealDeadlineTrail.LogPhrase(outcome), StringComparison.Ordinal);
        Assert.Equal(line, TrailLines.Filter(line));
    }

    [Fact]
    public void ADecidedLineHoldsTheCounts()
    {
        var phrase = MealDeadlineTrail.LogPhrase(new MealDeadlineOutcome.Decided(Report(revised: true, unjudged: 2)));

        Assert.Equal(
            "meal deadline decided, 6 lines approved, 1 lines denied, 2 lines unread, 1 lines late, 1 templates fed, 2 likes taught, 2 words unjudged, revised sheet needed",
            phrase);
    }

    [Fact]
    public void ARetryIsSaidSo()
    {
        var phrase = MealDeadlineTrail.LogPhrase(new MealDeadlineOutcome.Decided(Report(retry: true)));

        Assert.StartsWith("meal deadline asked again", phrase, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPhraseQuotesASentence()
    {
        var phrase = MealDeadlineTrail.LogPhrase(new MealDeadlineOutcome.Stopped(MealDeadlineStoppedBecause.AnswersUnreadable, "Sam's reason was oats."));

        Assert.DoesNotContain("Sam", phrase, StringComparison.Ordinal);
        Assert.DoesNotContain("oats", phrase, StringComparison.Ordinal);
    }
}
