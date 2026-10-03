using System.Text.RegularExpressions;
using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Source-level halves of Phase 4's cosmetic sweep for Snowball, Budget and Savings (mockups 6, 7, 8, 9, 16).
/// The Android head cannot be loaded by this desktop-TFM project, so what has to be true of the pages and
/// their charts is read out of the files, the way <c>SavingsPageSourceTests</c> and
/// <c>SnowballHistorySourceTests</c> do. The behavioural halves are <c>PlanMoneyTests</c>, <c>PlanTextTests</c>,
/// <c>GrowthChartAxisTests</c> and <c>PlanSpacingTests</c>.
/// </summary>
public class PlanCosmeticsSourceTests
{
    private const string Snowball = "Ledger.Android/Pages/SnowballPage.xaml.cs";
    private const string Budget = "Ledger.Android/Pages/BudgetPage.xaml.cs";
    private const string Savings = "Ledger.Android/Pages/SavingsPage.xaml.cs";
    private const string SavingsChart = "Ledger.Android/Views/SavingsGrowthChart.cs";
    private const string StackedChart = "Ledger.Android/Views/SnowballStackedBarsChart.cs";
    private const string SmallMultiples = "Ledger.Android/Views/SnowballSmallMultiplesChart.cs";
    private const string IndexCard = "Ledger.Android/Views/IndexCardView.cs";
    private const string BinderPage = "Ledger.Android/Pages/LedgerBinderPage.cs";

    private static readonly string[] Pages = [Snowball, Budget, Savings];
    private static readonly string[] PagesAndCharts = [Snowball, Budget, Savings, SavingsChart, StackedChart, SmallMultiples];

    // ---- money and type ----

    [Theory]
    [InlineData(Snowball)]
    [InlineData(Budget)]
    [InlineData(Savings)]
    [InlineData(SavingsChart)]
    [InlineData(StackedChart)]
    [InlineData(SmallMultiples)]
    public void TheseScreensFormatMoneyThroughPlanMoney_NotTheAccountsPagesBracketedCurrency(string path)
    {
        var source = Source(path);

        Assert.DoesNotContain("MainPage.Money(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"C2\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"C0\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NoStringLiteralHoldsAStraightApostropheInAWord()
    {
        var offenders = StraightApostropheScan.Offenders();

        Assert.True(offenders.Count == 0,
            "Straight apostrophes in copy (use U+2019; log lines, PDF page text and model instructions are exempt, see StraightApostropheScan):\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void TheDebtFreeDateIsDrawnInMono()
    {
        var source = Source(Snowball);

        var declaration = Regex.Match(source, @"debtFreeDateLabel\s*=\s*new\(\)\s*\{[^}]*\}").Value;
        Assert.Contains("FontFamily = \"Mono\"", declaration, StringComparison.Ordinal);
        Assert.Contains("PlanText.DebtFreeDate(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"MMMM d, yyyy\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLimitsAndPaymentsGoThroughThePlanTextFormatters()
    {
        Assert.Contains("PlanText.WeeklyNow(", Source(Snowball), StringComparison.Ordinal);
        Assert.Contains("PlanText.AsFocus(", Source(Snowball), StringComparison.Ordinal);
        Assert.Contains("PlanText.OfLimit(", Source(Budget), StringComparison.Ordinal);
        Assert.Contains("PlanText.MonthOfAgreed(", Source(Budget), StringComparison.Ordinal);
        Assert.Contains("PlanText.Pace(", Source(Savings), StringComparison.Ordinal);
        Assert.Contains("PlanText.HistoryNote(", Source(Savings), StringComparison.Ordinal);
    }

    // ---- chevrons ----

    [Theory]
    [InlineData(Snowball)]
    [InlineData(Budget)]
    [InlineData(Savings)]
    public void NoTriangleGlyphIsUsedAsAChevron(string path)
    {
        var source = Source(path);

        Assert.DoesNotContain("\u25BE", source, StringComparison.Ordinal);   // down-pointing small triangle
        Assert.DoesNotContain("\u25B8", source, StringComparison.Ordinal);   // right-pointing small triangle
        Assert.DoesNotContain("\u25B4", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Snowball)]
    [InlineData(Budget)]
    [InlineData(Savings)]
    public void TheCardsAskForARealChevron_DownWhenClosedAndUpWhenOpen(string path)
    {
        var source = Source(path);

        // CardChevron (Domain) names the shape; IndexCardView draws it. The pages pass the name, never a glyph.
        Assert.Contains("CardChevron.For(", source, StringComparison.Ordinal);
    }

    // ---- materials ----

    [Fact]
    public void AnIndexCardsSecondaryTextIsSoftInk_NotMutedInk()
    {
        var card = Source(IndexCard);
        var row = MethodBody(card, "private static View BuildRow(");

        // The left phrase (a due date, "Paid off ...", "to go") is the card's secondary text.
        Assert.Contains("InkSoft", row.Split("var right")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ThePostItsFootersAreSoftInk()
    {
        // Budget's and Savings' "n more" / "Earlier months" lines and Snowball's weeks lines sit on a tinted
        // post-it, where muted ink falls under the contrast floor (mockup 3's measurement).
        Assert.Contains("InkSoft", MethodBody(Source(Budget), "private View BuildMoreLine("), StringComparison.Ordinal);
        var loanPostIt = StripComments(MethodBody(Source(Snowball), "private View BuildLoanPostIt("));
        Assert.DoesNotContain("SecondaryLabel", loanPostIt, StringComparison.Ordinal);
        Assert.Contains("PostItLine(", loanPostIt, StringComparison.Ordinal);
    }

    // ---- spacing ----

    [Theory]
    [InlineData(Snowball)]
    [InlineData(Budget)]
    [InlineData(Savings)]
    public void TheCardsAreSpacedByThePlanSpacingConstants(string path)
    {
        var source = Source(path);

        Assert.Contains("PlanSpacing.CardGapDp", source, StringComparison.Ordinal);
        Assert.Contains("PlanSpacing.WellLeftDp", source, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"new VerticalStackLayout\s*\{\s*Spacing\s*=\s*1[04]\b", source);
    }

    [Theory]
    [InlineData(Snowball)]
    [InlineData(Budget)]
    [InlineData(Savings)]
    public void APostItIsPlacedByPlanPostIt_OverlappingItsCardAndIndented(string path)
    {
        Assert.Contains("PlanPostIt.Place(", Source(path), StringComparison.Ordinal);
    }

    [Fact]
    public void TheGraphPaperRunsBehindThePageHeader_OnAllThreePages()
    {
        var binder = Source(BinderPage);
        Assert.Contains("PaperBehindHeader", binder, StringComparison.Ordinal);
        Assert.Contains("GraphPaperBackground", binder, StringComparison.Ordinal);

        foreach (var page in Pages)
        {
            var source = Source(page);
            Assert.Matches(@"override bool PaperBehindHeader\s*=>\s*true", source);
            Assert.DoesNotContain("new GraphPaperBackground", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheBudgetMonthLineDrawsNoRuleOfItsOwn()
    {
        var line = MethodBody(Source(Budget), "private static View BuildMonthLine(");

        Assert.DoesNotContain("BoxView", line, StringComparison.Ordinal);
    }

    // ---- charts ----

    [Fact]
    public void TheSavingsChartLabelsItsAxesFromTheDomainAndNeverFormatsAMonthItself()
    {
        var chart = Source(SavingsChart);

        // "MMM ''yy": the '' is an empty literal in a .NET custom format, so it printed "Sep 25".
        Assert.DoesNotContain("''yy", chart, StringComparison.Ordinal);
        Assert.DoesNotContain("ToString(\"MMM", chart, StringComparison.Ordinal);
        Assert.Contains("GrowthChartAxis.YearTicks(", chart, StringComparison.Ordinal);
        Assert.Contains("GrowthChartAxis.MoneySteps(", chart, StringComparison.Ordinal);
        Assert.Contains("ChartAxis.MoneyTick(", chart, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProjectedGoalEndIsAHollowMarker()
    {
        var chart = Source(SavingsChart);

        // Filled in the paper, ringed in ink: the goal is where the line is going, not where it has been.
        Assert.Matches(@"var paper = PaperResources\.Colour\(""Paper""\)", chart);
        Assert.Matches(@"FillColor = paper;\s*canvas\.FillCircle\(goalX", chart);
        Assert.Contains("DrawCircle(goalX", chart, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SavingsChart)]
    public void TheSavingsChartTakesItsTextSizeFromATokenNotALiteral(string path)
    {
        Assert.DoesNotMatch(new Regex(@"FontSize\s*=\s*[0-9]"), Source(path));
        Assert.Contains("LabelSize", Source(path), StringComparison.Ordinal);
    }

    [Fact]
    public void TheChartTitlesAreSerif()
    {
        Assert.Contains("PlanChartTitle.", Source(Savings), StringComparison.Ordinal);
        Assert.Contains("PlanChartTitle.", Source(Snowball), StringComparison.Ordinal);
        var title = Source("Ledger.Android/Views/PlanChartTitle.cs");
        Assert.Contains("SerifSemibold", title, StringComparison.Ordinal);
        // The titles used to be the small-caps field label.
        Assert.DoesNotContain("\"Every loan, week by week\", Style = PaperResources.Token<Style>(\"FieldLabel\")", Source(Snowball), StringComparison.Ordinal);
    }

    // ---- helpers ----

    private static string StripComments(string source) => Regex.Replace(source, @"//[^\n]*", string.Empty);

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
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced braces after `{signature}`.");
    }

    private static string Source(string relativePath)
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), relativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {relativePath}");
        return File.ReadAllText(path);
    }
}
