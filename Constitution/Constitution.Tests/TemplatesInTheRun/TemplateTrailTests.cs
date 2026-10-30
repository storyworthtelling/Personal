using Constitution.Domain.Diagnostics;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;

namespace Constitution.Tests.TemplatesInTheRun;

/// <summary>The trail's words for the templates step: fixed phrases and counts, nothing the household wrote.</summary>
public class TemplateTrailTests
{
    private const string Stamp = "[Nov 4, 6:45 am] INFO Body — ";

    private static string Phrase(TemplateRunStatus status, int proposed = 0, int printed = 0, int unreadable = 0) =>
        MealTemplateTrail.LogPhrase(new MealTemplatesResult(status, proposed, printed, null) { UnreadableRecords = unreadable });

    [Fact]
    public void APrintedSheetSaysHowManyOfEach() =>
        Assert.Equal("meal templates: 2 templates proposed, 11 templates printed", Phrase(TemplateRunStatus.Printed, 2, 11));

    [Theory]
    [InlineData(TemplateRunStatus.Incomplete, "meal templates: 3 templates proposed, none printed")]
    [InlineData(TemplateRunStatus.NothingYet, "meal templates: nothing yet, none printed")]
    [InlineData(TemplateRunStatus.NoLibraryYet, "meal templates: no library yet")]
    [InlineData(TemplateRunStatus.LibraryUnreadable, "meal templates: library unreadable")]
    [InlineData(TemplateRunStatus.PastDeadline, "meal templates: past deadline, none printed")]
    [InlineData(TemplateRunStatus.NotSaved, "meal templates: library not saved, none printed")]
    public void EveryOtherEndingHasItsOwnLine(TemplateRunStatus status, string expected) =>
        Assert.Equal(expected, Phrase(status, 3));

    [Fact]
    public void UnreadableRecordsAreCounted() =>
        Assert.EndsWith(", 2 records unreadable", Phrase(TemplateRunStatus.Printed, 0, 11, 2), StringComparison.Ordinal);

    [Fact]
    public void EveryStatusHasAPhraseOfVocabularyWords()
    {
        foreach (var status in Enum.GetValues<TemplateRunStatus>())
        {
            var phrase = Phrase(status, 1, 1);

            Assert.Equal(phrase.ToLowerInvariant(), phrase);
            Assert.Equal(Stamp + phrase, TrailLines.Filter(Stamp + phrase));
        }
    }
}
