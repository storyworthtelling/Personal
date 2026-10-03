using System.Text.RegularExpressions;
using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Source-level halves of remediation 2.5 (mockups 9 and 16). The Android head cannot be loaded by this
/// desktop-TFM project, so what has to be true of the Savings page and its chart is read out of the files
/// themselves — the pattern <c>ManualPostItSourceTests</c> and <c>HouseholdGateSourceTests</c> use. The
/// behavioural halves are <c>GrowthChartScaleTests</c> (R-G21) and <c>SavingsGoalCopyTests</c>.
/// </summary>
public class SavingsPageSourceTests
{
    private const string PagePath = "Ledger.Android/Pages/SavingsPage.xaml.cs";
    private const string ChartPath = "Ledger.Android/Views/SavingsGrowthChart.cs";

    // ---- item 1: the chart takes its x-scale from the pure function ----

    [Fact]
    public void TheChartDoesNotClampTheMonthOffsetItself()
    {
        var chart = Source(ChartPath);

        Assert.DoesNotContain("static int MonthsBetween", chart, StringComparison.Ordinal);
        Assert.Contains("GrowthChartScale.FractionAcross(", chart, StringComparison.Ordinal);
        // Both the full chart and the plain no-goal line.
        Assert.True(
            Regex.Matches(chart, @"GrowthChartScale\.FractionAcross\(").Count >= 2,
            "The goal chart and DrawPlainLine must both position points with GrowthChartScale.");
    }

    // ---- item 2: the empty state is the whole page, in Serif italic, centred ----

    [Fact]
    public void TheEmptyStateIsSerifItalicAndCentredInThePage()
    {
        var empty = MethodBody(Source(PagePath), "View BuildEmptyState(");

        Assert.Contains("EmptyStateLabel", empty, StringComparison.Ordinal);
        Assert.DoesNotContain("SecondaryLabel", empty, StringComparison.Ordinal);
        Assert.Contains("LayoutOptions.Center", empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("private async Task RenderAccountsAsync(")]
    [InlineData("private Task RenderAccountsFromHouseholdAsync(")]
    [InlineData("private async Task RenderGrowthAsync(")]
    [InlineData("private Task RenderGrowthFromHouseholdAsync(")]
    public void EveryEmptyBranchShowsTheEmptyPageAndEveryOtherBranchRestoresTheSides(string signature)
    {
        var render = MethodBody(Source(PagePath), signature);

        Assert.Contains("ShowEmptyPage(", render, StringComparison.Ordinal);
        Assert.Contains("ShowSides(", render, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildEmptyState()", render, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEmptyPageHidesTheSwitchAndTheWholeSavedBlock()
    {
        var page = Source(PagePath);
        var show = MethodBody(page, "private void ShowEmptyPage(");
        var restore = MethodBody(page, "private void ShowSides(");

        // The "Saved" caption lives inside the accounts side, which lives in sideHost.
        Assert.Contains("sideSwitch.IsVisible = false", show, StringComparison.Ordinal);
        Assert.Contains("sideHost.IsVisible = false", show, StringComparison.Ordinal);
        Assert.Contains("sideSwitch.IsVisible = true", restore, StringComparison.Ordinal);
        Assert.Contains("sideHost.IsVisible = true", restore, StringComparison.Ordinal);
    }

    // ---- item 3: the goal is kept when you leave the field, and when you leave the page ----

    [Fact]
    public void TheOpenGoalIsPersistedWhenThePageDisappears()
    {
        var page = Source(PagePath);

        var disappearing = MethodBody(page, "protected override async void OnDisappearing(");
        Assert.Contains("PersistOpenGoalAsync(", disappearing, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOpenGoalIsPersistedBeforeThePostItClosesOrTheSideChanges()
    {
        var page = Source(PagePath);

        var toggle = MethodBody(page, "private async void ToggleExpanded(");
        Assert.True(
            toggle.IndexOf("PersistOpenGoalAsync(", StringComparison.Ordinal) is var persist && persist >= 0
            && persist < toggle.IndexOf("expandedAccountId =", StringComparison.Ordinal),
            "ToggleExpanded must persist the typed goal before it collapses the post-it.");

        Assert.Contains("PersistOpenGoalAsync(", MethodBody(page, "private async void OnSideChanged("), StringComparison.Ordinal);
    }

    [Fact]
    public void TheUnfocusedHandlerUsesTheSamePersistPath()
    {
        var postIt = MethodBody(Source(PagePath), "private View BuildGoalPostIt(");

        Assert.Contains("PersistOpenGoalAsync(", postIt, StringComparison.Ordinal);
    }

    [Fact]
    public void UndoingARemovedGoalIsObservedAndLoggedNotFireAndForget()
    {
        var page = Source(PagePath);

        Assert.DoesNotContain("_ = UndoRemoveGoalAsync", page, StringComparison.Ordinal);
        var undo = MethodBody(page, "private async Task UndoRemoveGoalAsync(");
        Assert.Contains("catch (Exception", undo, StringComparison.Ordinal);
        Assert.Contains("logger.LogError(", undo, StringComparison.Ordinal);
    }

    [Fact]
    public void TheKeystrokeHandlerNeverForcesTheExpandedCardHost()
    {
        Assert.DoesNotContain("expandedCardHost!", Source(PagePath), StringComparison.Ordinal);
    }

    // ---- item 4: a met goal says so ----

    [Fact]
    public void TheGoalSentenceComesFromTheDomainNotAPrivateCopyInThePage()
    {
        var page = Source(PagePath);

        Assert.DoesNotContain("CardReachesItText(", page, StringComparison.Ordinal);
        Assert.DoesNotContain("PostItReachesItText(", page, StringComparison.Ordinal);
        Assert.Contains("SavingsGoalCopy.Card(", page, StringComparison.Ordinal);
        Assert.Contains("SavingsGoalCopy.PostIt(", page, StringComparison.Ordinal);
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

    private static string Source(string ledgerRelativePath)
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), ledgerRelativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {ledgerRelativePath}");
        return File.ReadAllText(path);
    }
}
