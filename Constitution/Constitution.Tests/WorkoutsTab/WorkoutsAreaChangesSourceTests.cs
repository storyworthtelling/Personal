using System.Text.RegularExpressions;

namespace Constitution.Tests.WorkoutsTab;

/// <summary>
/// S2, in the head. The area is compile-checked, not run here, so what can be said about how it handles a change is said by reading it:
/// it goes through <c>WorkoutChanges</c> (which keeps the order and reads the calendar's answer), it never asks the calendar itself and drops
/// the answer, a skip is let go of only when it has been recorded and is retried while it has not, and the sentence about a change reaches the
/// line under the header. The behaviour itself is tested on the models.
/// </summary>
public class WorkoutsAreaChangesSourceTests
{
    private static string Area() =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), "Constitution.Android", "WorkoutsArea.cs"));

    [Fact]
    public void EveryChangeGoesThroughTheModelThatReadsTheCalendarsAnswer()
    {
        var area = Area();

        Assert.Contains("WorkoutChanges", area, StringComparison.Ordinal);
        Assert.Contains(".SwapAsync(", area, StringComparison.Ordinal);
        Assert.Contains(".SkipAsync(", area, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAreaNeverAsksTheCalendarItselfAndDropsTheAnswer()
    {
        var area = Area();

        Assert.DoesNotContain("FollowSwapAsync", area, StringComparison.Ordinal);
        Assert.DoesNotContain("FollowSkipAsync", area, StringComparison.Ordinal);
        Assert.DoesNotContain("source.Record(", area, StringComparison.Ordinal);
    }

    [Fact]
    public void ASkipIsHandedOverAloneAndNeverAsALedgerBuiltFromTheSnapshot()
    {
        var area = Area();

        // The page may show the day skipped from the snapshot it already holds; what goes to the source is the skip, by WorkoutChanges.
        Assert.DoesNotContain("SkipCommitted(", area, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"var ledger\s*=", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), area);
    }

    [Fact]
    public void ASkipIsRecordedWhetherOrNotTheTabHasLoaded()
    {
        var area = Area();
        var start = area.IndexOf("private void StartRecording", StringComparison.Ordinal);
        Assert.True(start >= 0, "The area should start the recording of a skip in one place.");
        var body = area[start..area.IndexOf("private void RecordingDone", start, StringComparison.Ordinal)];

        Assert.Contains("SkipAsync(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("WorkoutsRead.Ready", body, StringComparison.Ordinal);
        Assert.DoesNotContain("return;\n        }\n\n        var ledger", area, StringComparison.Ordinal);
    }

    [Fact]
    public void ASkipIsLetGoOfOnlyOnceItIsRecordedAndIsRetriedUntilThen()
    {
        var area = Area();

        Assert.Contains("undo.Recorded(", area, StringComparison.Ordinal);
        Assert.Contains("undo.Unrecorded", area, StringComparison.Ordinal);
        Assert.Contains("undo.Shown", area, StringComparison.Ordinal);
        Assert.Contains("if (result.Kept)", area, StringComparison.Ordinal);
    }

    [Fact]
    public void ASwapThatWasNotKeptIsTakenBackOnThePage()
    {
        var area = Area();

        Assert.Contains("!outcome.Kept", area, StringComparison.Ordinal);
        Assert.Contains("previous", area, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSentenceAboutAChangeIsTheLineUnderTheHeader()
    {
        var area = Area();

        Assert.Contains("WorkoutsTabModel.StatusWith(notice)", area, StringComparison.Ordinal);
        Assert.Contains("notice =", area, StringComparison.Ordinal);
    }

    [Fact]
    public void ACalendarThatDidNotChangeIsAskedAgainWhenTheTabIsOpened()
    {
        var area = Area();

        Assert.Contains("RetryAsync(", area, StringComparison.Ordinal);
        Assert.Contains("HasOwed", area, StringComparison.Ordinal);
    }

    [Fact]
    public void ASkipIsNamedByTheNumbersTheRunIssued()
    {
        var area = Area();

        Assert.Matches(new Regex(@"WorkoutKeys\.Of\([^;]*snapshot\.Assignments", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), area);
    }

    [Fact]
    public void NoTrailPhraseNamesADayAPersonOrAnEvent()
    {
        var phrases = Regex.Matches(Area(), @"log\.Outcome\(\$?""([^""]*)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)).Select(match => match.Groups[1].Value).ToList();

        Assert.NotEmpty(phrases);
        Assert.All(phrases, phrase =>
        {
            Assert.DoesNotContain("Day", phrase.Replace("{e.GetType().Name}", string.Empty), StringComparison.Ordinal);
            Assert.DoesNotContain("{day", phrase, StringComparison.Ordinal);
            Assert.DoesNotContain("{commit", phrase, StringComparison.Ordinal);
        });
    }
}
