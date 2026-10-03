using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// R-G28's source-level half (remediation 3.6). The Android head cannot be loaded by this desktop-TFM
/// project, so what must be true of her pages is read out of the files themselves (the pattern
/// <c>SnowballHistorySourceTests</c> and <c>SavingsPageSourceTests</c> use). What is held:
///
/// <para>Her History and Growth sides are drawn from the household SNAPSHOT — never from the live service,
/// which on her phone reads an empty local database — and by the SAME drawing code as the owner's, so the
/// legend, axis, footnote and touch behaviour Phase 3.1 gave the owner's History are hers too rather than a
/// fork that drifts.</para>
///
/// <para>The old sentence survives for a version 1 file only; and a file newer than this build can read is
/// said so on every page that reads it, instead of her pages quietly falling back to an empty local
/// database.</para>
/// </summary>
public class HouseholdHistorySourceTests
{
    private const string SnowballPath = "Ledger.Android/Pages/SnowballPage.xaml.cs";
    private const string SavingsPath = "Ledger.Android/Pages/SavingsPage.xaml.cs";
    private const string AccountsPath = "Ledger.Android/Pages/MainPage.xaml.cs";
    private const string BudgetPath = "Ledger.Android/Pages/BudgetPage.xaml.cs";
    private const string HouseholdPagesPath = "Ledger.Android/HouseholdPages.cs";

    // ---- Snowball: her History side ----

    [Theory]
    [InlineData("protected override async void OnAppearing(")]
    [InlineData("private async void OnSideChanged(")]
    public void HerSnowballBranchesNeverCallTheLiveHistoryRender(string signature)
    {
        var body = MethodBody(Source(SnowballPath), signature);

        // The original called RenderHistoryAsync() from inside the household branch, which reads
        // SnowballService.GetHistoryAsync — her empty local database — so her History was always empty.
        Assert.DoesNotContain("RenderHistoryAsync(", body, StringComparison.Ordinal);
        Assert.Contains("RenderHistoryFromHousehold(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void HerSnowballHistoryReadsTheSnapshotAndNothingLive()
    {
        var body = MethodBody(Source(SnowballPath), "private void RenderHistoryFromHousehold(");

        Assert.DoesNotContain("snowball.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("GetHistoryAsync", body, StringComparison.Ordinal);
        Assert.Contains("HouseholdChartSeries.ToSnowballHistory(", body, StringComparison.Ordinal);
        Assert.Contains("DrawHistory(", body, StringComparison.Ordinal);
        Assert.Contains("HouseholdChartCopy.SnowballNotInSummary", body, StringComparison.Ordinal);
    }

    [Fact]
    public void BothInstallsDrawTheHistoryThroughTheSameMethod()
    {
        var source = Source(SnowballPath);

        Assert.Contains("DrawHistory(", MethodBody(source, "private async Task RenderHistoryAsync("), StringComparison.Ordinal);
        Assert.Contains("DrawHistory(", MethodBody(source, "private void RenderHistoryFromHousehold("), StringComparison.Ordinal);
        // The chart views, legend, footnote and captions are built in exactly one place.
        Assert.Equal(1, Count(source, "SnowballLegend.Build("));
        Assert.Equal(1, Count(source, "SnowballHistoryCopy.Footnote("));
        Assert.Equal(1, Count(source, "stackedBarsChart.SetWeeks(weeklyTotals)"));
    }

    // ---- Savings: her Growth side ----

    [Fact]
    public void ThePlaceholderSentenceIsGoneFromTheSavingsPage()
    {
        var source = Source(SavingsPath);

        Assert.DoesNotContain("Growth isn't part of this summary yet.", source, StringComparison.Ordinal);
        Assert.Contains("HouseholdChartCopy.GrowthNotInSummary", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HerSavingsGrowthReadsTheSnapshotAndDrawsWithTheOwnersChartCode()
    {
        var source = Source(SavingsPath);
        var hers = MethodBody(source, "private Task RenderGrowthFromHouseholdAsync(");
        var owners = MethodBody(source, "private async Task RenderGrowthAsync(");

        Assert.DoesNotContain("savings.", hers, StringComparison.Ordinal);
        Assert.DoesNotContain("new SavingsGrowthChart", hers, StringComparison.Ordinal);
        Assert.Contains("HouseholdChartSeries.ToSavingsGrowth(", hers, StringComparison.Ordinal);
        Assert.Contains("BuildGrowthColumn(", hers, StringComparison.Ordinal);
        Assert.Contains("BuildGrowthColumn(", owners, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, "new SavingsGrowthChart()"));
    }

    // ---- a file newer than this build ----

    [Theory]
    [InlineData(AccountsPath)]
    [InlineData(BudgetPath)]
    [InlineData(SnowballPath)]
    [InlineData(SavingsPath)]
    public void EveryPageThatReadsTheHouseholdFileSaysSoWhenItIsNewerThanThisBuild(string path)
    {
        var source = Source(path);

        Assert.Contains("FileIsNewer", source, StringComparison.Ordinal);
        Assert.Contains("HouseholdPages.NewerFileNotice(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSharedLoaderReportsANewerFileInsteadOfSwallowingItAsNothing()
    {
        var source = Source(HouseholdPagesPath);

        Assert.Contains("HouseholdSnapshotStatus.NewerVersion", source, StringComparison.Ordinal);
        Assert.Contains("FileIsNewer", source, StringComparison.Ordinal);
        Assert.Contains("HouseholdChartCopy.NewerFile", source, StringComparison.Ordinal);
    }

    // ---- helpers ----

    private static int Count(string source, string needle)
    {
        var count = 0;
        for (var at = source.IndexOf(needle, StringComparison.Ordinal); at >= 0;
             at = source.IndexOf(needle, at + needle.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

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
