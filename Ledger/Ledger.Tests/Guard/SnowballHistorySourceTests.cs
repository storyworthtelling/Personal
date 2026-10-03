using System.Text.RegularExpressions;
using Index.Tests.Design;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Phase 3.1 (mockup 6): the source-level halves of the Snowball guards. The Android head cannot be
/// loaded by this desktop-TFM project, so what must be true of its wiring is read out of the files
/// themselves (the pattern <c>ManualPostItSourceTests</c> established). The behavioural halves — the
/// x → week mapping, the axis steps, the labels, the copy — live in <c>SnowballChartMathTests</c>.
///
/// <para><b>R-G24</b>: touching a bar or a panel reports THAT week. The small-multiples chart used to
/// ignore <c>point.X</c> and report the last week whatever was touched; nothing failed, the caption
/// just always said "now". The guard is that neither chart computes a week from anything but the one
/// pure mapping.</para>
/// </summary>
public class SnowballHistorySourceTests
{
    private const string SmallMultiples = "Ledger.Android/Views/SnowballSmallMultiplesChart.cs";
    private const string Stacked = "Ledger.Android/Views/SnowballStackedBarsChart.cs";
    private const string Page = "Ledger.Android/Pages/SnowballPage.xaml.cs";
    private const string Card = "Ledger.Android/Views/IndexCardView.cs";
    private const string Legend = "Ledger.Android/Views/SnowballLegend.cs";

    // ---- R-G24: the touched week, from the one pure mapping ----

    [Fact]
    public void TheSmallMultiplesChartReportsTheTouchedWeekNotTheLastOne()
    {
        var source = Source(SmallMultiples);

        Assert.DoesNotContain("lastIndex", source, StringComparison.Ordinal);
        Assert.Contains("ChartHit.AtPoint(", source, StringComparison.Ordinal);
        Assert.Contains("SmallMultipleLayout.HitTest(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStackedChartHitTestsThroughTheSameMappingAndOnlyOverThePlot()
    {
        var source = Source(Stacked);

        Assert.Contains("ChartHit.AtBar(", source, StringComparison.Ordinal);
        // The old inline arithmetic, which divided by the full width (the axis gutter included).
        Assert.DoesNotContain("point.X / (Width / painter.Weeks.Count)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("painter.Weeks.Count - 1;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCaptionsAreEmptyUntilTheFirstTouch()
    {
        var page = Source(Page);

        Assert.Matches(new Regex(@"historyCaptionLabel\s*=\s*new\(\)\s*\{[^}]*IsVisible\s*=\s*false"), page);
        Assert.Matches(new Regex(@"panelCaptionLabel\s*=\s*new\(\)\s*\{[^}]*IsVisible\s*=\s*false"), page);
        Assert.Contains("SnowballHistoryCopy.WeekCaption(", page, StringComparison.Ordinal);
    }

    // ---- the page paints first; the Drive import runs behind it ----

    [Fact]
    public void OnAppearingDoesNotAwaitTheHistoryImport()
    {
        var body = MethodBody(Source(Page), "protected override async void OnAppearing(");

        // A slow or hanging Drive used to hold the whole page blank for up to the 30-second read cap.
        Assert.DoesNotMatch(new Regex(@"await\s+snowball\.ImportHistoryIfNeededAsync"), body);
        Assert.DoesNotMatch(new Regex(@"await\s+\w+\.ImportHistoryIfNeededAsync\("), body);
        // The import is started through the one helper that paints first and imports behind the page.
        Assert.Contains("SnowballOpening.OpenAsync(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOnlyThingTheSnowballPageDoesWithTheImportIsHandItToTheOpeningHelper()
    {
        var page = Source(Page);

        Assert.Equal(1, Regex.Matches(page, @"ImportHistoryIfNeededAsync").Count);
        Assert.Matches(new Regex(@"SnowballOpening\.OpenAsync\(\s*snowball\.RecordCurrentWeekSnapshotAsync,\s*snowball\.ImportHistoryIfNeededAsync"), page);
    }

    // ---- legend, axis, labels ----

    [Fact]
    public void EveryLoanHasASwatchAndANameInALegend()
    {
        Assert.True(File.Exists(Path.Combine(DesignTestHelpers.LedgerRoot(), Legend)), "SnowballLegend.cs is missing.");
        var legend = Source(Legend);
        Assert.Contains("SnowballChartPalette.RampHex(", legend, StringComparison.Ordinal);

        var page = MethodBody(Source(Page), "private void DrawHistory(");
        Assert.Contains("SnowballLegend.Build(", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStackedChartDrawsALabelledMoneyAxisAndCalendarMonthLabels()
    {
        var source = Source(Stacked);

        Assert.Contains("ChartAxis.NiceSteps(", source, StringComparison.Ordinal);
        Assert.Contains("ChartAxis.MoneyTick(", source, StringComparison.Ordinal);
        Assert.Contains("ChartAxis.MonthTicks(", source, StringComparison.Ordinal);
        // The old labels: 0 / 50 / 100% of the weeks, "MMM yy" (which prints "Jul 25", no apostrophe).
        Assert.DoesNotContain("\"MMM yy\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new[] { 0.0, 0.5, 1.0 }", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Stacked)]
    [InlineData(SmallMultiples)]
    public void TheChartsTakeTheirTextSizeFromATokenNotALiteral(string path)
    {
        Assert.DoesNotMatch(new Regex(@"FontSize\s*=\s*[0-9]"), Source(path));
        Assert.Contains("LabelSize", Source(path), StringComparison.Ordinal);
    }

    // ---- empty state, footnote, panel names ----

    [Fact]
    public void AnEmptyHistorySaysSoInOneSentenceInTheKitsEmptyStateStyle()
    {
        var page = Source(Page);

        Assert.Contains("SnowballHistoryCopy.EmptyHistory", page, StringComparison.Ordinal);
        Assert.Contains("EmptyStateLabel", page, StringComparison.Ordinal);
        Assert.Contains("history.IsEmpty", MethodBody(page, "private void DrawHistory("), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFootnoteCarriesTheFirstRecordedMonth()
    {
        var page = Source(Page);

        Assert.DoesNotContain("Each panel runs from $0 to that loan's own peak.", page, StringComparison.Ordinal);
        Assert.Contains("SnowballHistoryCopy.Footnote(", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNinthPanelKeepsItsColumnAndTheMortgageIsNamedByWhatItIsNotByItsName()
    {
        var chart = Source(SmallMultiples);
        Assert.DoesNotContain("isLastAndAlone", chart, StringComparison.Ordinal);

        var page = MethodBody(Source(Page), "private void DrawHistory(");
        Assert.Contains("SnowballHistoryCopy.PanelName(", page, StringComparison.Ordinal);

        // No lender is named in the page or the chart: every name on screen arrives as data, and the
        // mortgage is recognised by a flag on the loan, never by a literal. Checked against every loan
        // name the history carries, and against any string literal that spells out a mortgage lender.
        foreach (var loanName in SyntheticSnowballHistory.LoanNames)
        {
            Assert.DoesNotContain($"\"{loanName}\"", Source(Page), StringComparison.Ordinal);
            Assert.DoesNotContain($"\"{loanName}\"", chart, StringComparison.Ordinal);
        }

        var lenderLiteral = new Regex("\"[^\"\\n]*[A-Za-z]+ Mortgage[^\"\\n]*\"");
        Assert.DoesNotMatch(lenderLiteral, CodeOnly(Source(Page)));
        Assert.DoesNotMatch(lenderLiteral, CodeOnly(chart));
    }

    // ---- the Loans side's cards ----

    [Fact]
    public void TheFocusLoanWearsAChipNotAPlainSuffix()
    {
        var page = Source(Page);

        Assert.DoesNotContain("·  Focus", page, StringComparison.Ordinal);
        Assert.Contains("PhaseChip", page, StringComparison.Ordinal);
        Assert.Contains("ChipLabelActive", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOrderNumberLivesInsideTheCardAndStillOpensTheReorderCard()
    {
        var page = Source(Page);
        var row = MethodBody(page, "private View BuildLoanRow(");

        // The old shape: a two-column Grid with the badge in column 0 and the card in column 1.
        Assert.DoesNotContain("ColumnDefinition", row, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.SetColumn", row, StringComparison.Ordinal);
        Assert.Contains("ShowLoanActionCard(", page, StringComparison.Ordinal);

        Assert.Matches(new Regex(@"View\s+Build\([^)]*View\?\s+leading"), Source(Card));
    }

    [Fact]
    public void APaidOffLoanIsCrossedOutInGreyWithNoEmptySecondRow()
    {
        var page = Source(Page);
        var card = Source(Card);

        Assert.Contains("TextDecorations.Strikethrough", card, StringComparison.Ordinal);
        Assert.DoesNotContain("card.Opacity = 0.55", page, StringComparison.Ordinal);
        Assert.DoesNotContain("new IndexCardRow(string.Empty, string.Empty, Muted: true)", page, StringComparison.Ordinal);
    }

    // ---- helpers ----

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

    /// <summary>The source with whole-line comments dropped, so prose that mentions a sheet tab is not
    /// mistaken for a string literal in code.</summary>
    private static string CodeOnly(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static string Source(string ledgerRelativePath)
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), ledgerRelativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {ledgerRelativePath}");
        return File.ReadAllText(path);
    }
}
