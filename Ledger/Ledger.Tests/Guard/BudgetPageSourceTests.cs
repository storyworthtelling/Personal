using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Source-level halves of remediation 3.2 (mockups 7, 8 and 11). The Android head cannot be loaded by this
/// desktop-TFM project, so what has to be true of <c>BudgetPage</c> is read out of the file — the pattern
/// <c>SavingsPageSourceTests</c> and <c>ManualPostItSourceTests</c> use. The behavioural halves are the pure
/// types the page now draws from (<c>BudgetCardLines</c>, <c>NotePaging</c>, <c>HistoryRowText</c>,
/// <c>HouseholdBudgetView</c>) and the services, each with its own tests.
/// </summary>
public class BudgetPageSourceTests
{
    private const string PagePath = "Ledger.Android/Pages/BudgetPage.xaml.cs";

    // ---- item 1 (D142): the tracking-only card's second row ----

    [Fact]
    public void TheTrackingOnlyCardDrawsItsRowsFromTheDomainDecision_LastMonthIncluded()
    {
        var page = Source();
        var rows = MethodBody(page, "private static IReadOnlyList<IndexCardRow> CategoryCardRows(");

        Assert.Contains("BudgetCardLines.ForTracking(", rows, StringComparison.Ordinal);
        Assert.DoesNotContain("not this row", page, StringComparison.Ordinal);
    }

    // ---- item 7: the unreadable cue is the Progress role's own mark, not Snowball's hue ----

    [Fact]
    public void TheSentBackLineIsInTheProgressTealToken_NotBorrowedFromSnowballsAreaHue()
    {
        var page = Source();

        Assert.Contains("PaperResources.Colour(\"ProgressMark\")", page, StringComparison.Ordinal);
        Assert.DoesNotContain("LedgerArea.Snowball", page, StringComparison.Ordinal);
    }

    // ---- item 4: "n more" and "Earlier months" are not dead labels ----

    [Fact]
    public void TheTransactionPostItsMoreLineLengthensTheNoteInPlace()
    {
        var body = MethodBody(Source(), "private async Task<View> BuildTransactionsPostItAsync(");

        Assert.Contains("NotePaging.Shown(", body, StringComparison.Ordinal);
        Assert.Contains("NotePaging.MoreLabel(", body, StringComparison.Ordinal);
        Assert.Contains("BuildMoreLine(", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(4)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMoreLineIsTappableAndBumpsTheCountThatRerendersTheNote()
    {
        var line = MethodBody(Source(), "private View BuildMoreLine(");

        Assert.Contains("TapGestureRecognizer", line, StringComparison.Ordinal);
        Assert.Contains("noteTaps++", line, StringComparison.Ordinal);
        Assert.Contains("ReloadRenderOnlyAsync()", line, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHistoryPostItsEarlierMonthsLineLengthensTheNoteInPlace()
    {
        var body = MethodBody(Source(), "private async Task<View> BuildHistoryPostItAsync(");

        Assert.Contains("NotePaging.Shown(", body, StringComparison.Ordinal);
        Assert.Contains("BuildMoreLine(\"Earlier months\")", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(6)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheExpansionCountResetsWhenADifferentCardOpens()
    {
        var toggle = MethodBody(Source(), "private void ToggleExpandedCategory(");

        Assert.Contains("noteTaps = 0", toggle, StringComparison.Ordinal);
    }

    // ---- item 5: the history row is mockup 8's anatomy ----

    [Fact]
    public void TheHistoryRowIsMonthOverSoFar_ABar_AndAnAmount_InTheMockupsColumns()
    {
        var row = MethodBody(Source(), "private static View BuildHistoryRow(");

        Assert.Contains("HistoryRowText.For(", row, StringComparison.Ordinal);
        Assert.Contains("WidthRequest = 74", row, StringComparison.Ordinal);   // .mh-m
        Assert.Contains("HeightRequest = 6", row, StringComparison.Ordinal);   // .mh-b
        Assert.Contains("WidthRequest = 60", row, StringComparison.Ordinal);   // .mh-v
        Assert.DoesNotContain("\" so far\"", row, StringComparison.Ordinal);
    }

    // ---- item 3: a worksheet that came back unreadable has come back ----

    [Fact]
    public void TheEmptyStateGateIsTheServicesNotARestatementOfPositiveLimitsInThePage()
    {
        var page = Source();

        Assert.Contains("budget.AnyLimitSetAsync(", page, StringComparison.Ordinal);
        Assert.DoesNotContain("LimitAmount > 0", page, StringComparison.Ordinal);
    }

    // ---- item 6: her install draws the same figures and raises the same dog-ear ----

    [Fact]
    public void HerInstallTakesItsFiguresFromTheSharedViewAndRaisesTheDogEar()
    {
        var page = Source();
        var hers = MethodBody(page, "private void RenderMonthFromHousehold(");

        Assert.Contains("HouseholdBudgetView.From(", hers, StringComparison.Ordinal);
        Assert.Contains("RefreshNotices(", hers, StringComparison.Ordinal);
        Assert.DoesNotContain("Sum(c => c.ActualAmount)", hers, StringComparison.Ordinal);
        Assert.DoesNotContain("BudgetPeriod.DaysLeft(", hers, StringComparison.Ordinal);
    }

    [Fact]
    public void HerEmptyMonthClearsAnyStaleNoticeToo()
    {
        var hers = MethodBody(Source(), "private void RenderMonthFromHousehold(");

        Assert.True(
            hers.Split("RefreshNotices(").Length - 1 >= 2,
            "Both her empty state and her month must set the dog-ear (one raises it, the other clears it).");
    }

    /// <summary>The text of a method from its signature to its matching closing brace.</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find `{signature}`.");

        var open = source.IndexOf('{', source.IndexOf(')', start));
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                return source[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced braces after `{signature}`.");
    }

    private static string Source()
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), PagePath);
        Assert.True(File.Exists(path), $"Expected file not found: {PagePath}");
        return File.ReadAllText(path);
    }
}
