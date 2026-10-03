using System.Globalization;
using Index.Domain.Model;
using Index.Infrastructure.Services;
using Index.Tests.Support;
using Xunit;

namespace Index.Tests.Snowball;

/// <summary>
/// Phase 3.1 (mockup 6): the pure halves of the History charts — which week a touch lands on, the
/// axis, the labels, the copy — pulled out of the two painters so they are tested rather than judged
/// by eye. The wiring that uses them is held by <c>SnowballHistorySourceTests</c>.
///
/// <para><b>R-G24</b> is the first group: a touch reports THE TOUCHED WEEK. The small-multiples chart
/// ignored the touch's X and reported the last week for every tap, so the mapping is now one function
/// with a test per edge.</para>
/// </summary>
public class SnowballChartMathTests
{
    // The sheet's 70 recorded weeks: Friday 30 May 2025 to Friday 25 Sep 2026 (finding, Appendix A).
    private static readonly IReadOnlyList<DateOnly> SheetWeeks =
        Enumerable.Range(0, 70).Select(n => new DateOnly(2025, 5, 30).AddDays(7 * n)).ToList();

    // ---- R-G24: x → week ----

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(4.2f, 0)]
    [InlineData(4.3f, 1)]            // the second bar starts at 300/70 = 4.2857
    [InlineData(150f, 35)]
    [InlineData(299.9f, 69)]
    [InlineData(300f, 69)]           // the right edge belongs to the last bar
    public void ABarIsFoundByTheSlotItSitsIn(float x, int expected) =>
        Assert.Equal(expected, ChartHit.AtBar(x, width: 300f, count: 70));

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(300f, 69)]
    [InlineData(150f, 35)]           // 150/300 × 69 = 34.5, rounded away from zero
    [InlineData(4.3f, 1)]            // nearest point to 4.3 is 1 (at 4.35)
    public void ALinePointIsFoundByTheNearestPoint(float x, int expected) =>
        Assert.Equal(expected, ChartHit.AtPoint(x, width: 300f, count: 70));

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(300.1f)]             // out in the axis gutter: not a bar
    [InlineData(float.NaN)]
    public void ATouchOutsideThePlotIsNoWeek(float x)
    {
        Assert.Null(ChartHit.AtBar(x, 300f, 70));
        Assert.Null(ChartHit.AtPoint(x, 300f, 70));
    }

    [Theory]
    [InlineData(100f, 0f, 70)]
    [InlineData(100f, 300f, 0)]
    [InlineData(100f, 300f, -1)]
    public void NothingToHitIsNoWeek(float x, float width, int count)
    {
        Assert.Null(ChartHit.AtBar(x, width, count));
        Assert.Null(ChartHit.AtPoint(x, width, count));
    }

    [Fact]
    public void OneWeekIsTheWholeChart()
    {
        Assert.Equal(0, ChartHit.AtBar(10f, 300f, 1));
        Assert.Equal(0, ChartHit.AtPoint(290f, 300f, 1));
    }

    [Fact]
    public void EveryWeekIsReachableAndNoTwoTouchesAtOppositeEndsAgree()
    {
        // The bug: every touch answered "the last week". Walk the whole width and require the answers
        // to cover all 70 weeks, in order.
        var barWeeks = Enumerable.Range(0, 3000).Select(i => ChartHit.AtBar(i / 10f, 300f, 70)!.Value).Distinct().ToList();
        Assert.Equal(Enumerable.Range(0, 70), barWeeks);

        var pointWeeks = Enumerable.Range(0, 3000).Select(i => ChartHit.AtPoint(i / 10f, 300f, 70)!.Value).Distinct().ToList();
        Assert.Equal(Enumerable.Range(0, 70), pointWeeks);
    }

    // ---- the panel grid: where a touch lands, and which panel it is ----

    private const float Width = 330f;
    private const float PanelHeight = 74f;
    private const float Gap = 14f;

    [Fact]
    public void TheNinthPanelStaysInTheLeftColumnAtHalfWidth()
    {
        var column = SmallMultipleLayout.Panel(0, Width, PanelHeight, Gap);
        var ninth = SmallMultipleLayout.Panel(8, Width, PanelHeight, Gap);

        Assert.Equal(0f, ninth.X);
        Assert.Equal(column.Width, ninth.Width);
        Assert.Equal((Width - Gap) / 2f, ninth.Width);
        Assert.Equal(4 * (PanelHeight + Gap), ninth.Y);
    }

    [Fact]
    public void ThePanelsSitInTwoColumnsReadingAcross()
    {
        Assert.Equal(0f, SmallMultipleLayout.Panel(0, Width, PanelHeight, Gap).X);
        Assert.Equal((Width - Gap) / 2f + Gap, SmallMultipleLayout.Panel(1, Width, PanelHeight, Gap).X);
        Assert.Equal(PanelHeight + Gap, SmallMultipleLayout.Panel(2, Width, PanelHeight, Gap).Y);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(8, 4)]
    [InlineData(9, 5)]
    public void TheGridIsAsTallAsItsRows(int count, int rows) =>
        Assert.Equal(rows * (PanelHeight + Gap), SmallMultipleLayout.TotalHeight(count, PanelHeight, Gap));

    [Fact]
    public void ATouchInAPanelNamesThatPanelAndTheXWithinIt()
    {
        var second = SmallMultipleLayout.Panel(1, Width, PanelHeight, Gap);

        var hit = SmallMultipleLayout.HitTest(second.X + 20f, 10f, 9, Width, PanelHeight, Gap);
        Assert.Equal(new PanelHit(1, 20f, second.Width), hit);

        var ninth = SmallMultipleLayout.Panel(8, Width, PanelHeight, Gap);
        var ninthHit = SmallMultipleLayout.HitTest(7f, ninth.Y + 30f, 9, Width, PanelHeight, Gap);
        Assert.Equal(new PanelHit(8, 7f, ninth.Width), ninthHit);
    }

    [Theory]
    [InlineData(165f, 10f)]    // the gutter between the columns
    [InlineData(10f, 80f)]     // the gap between the rows
    [InlineData(200f, 372f)]   // the empty right half of the ninth panel's row
    [InlineData(-1f, 10f)]
    [InlineData(10f, 1000f)]
    public void ATouchBetweenPanelsOrPastTheLastIsNoPanel(float x, float y) =>
        Assert.Null(SmallMultipleLayout.HitTest(x, y, 9, Width, PanelHeight, Gap));

    // ---- the money axis ----

    [Theory]
    [InlineData(280842.11, new[] { 100000, 200000 })]   // a six-figure total gets 100k steps
    [InlineData(50000, new[] { 20000, 40000 })]
    [InlineData(7500, new[] { 2000, 4000, 6000 })]
    [InlineData(1000000, new[] { 500000, 1000000 })]
    [InlineData(3, new[] { 1, 2, 3 })]
    public void TheAxisStepsAreRoundNumbersUnderTheMaximum(double max, int[] expected) =>
        Assert.Equal(expected.Select(v => (decimal)v), ChartAxis.NiceSteps((decimal)max));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0.4)]
    public void NothingToScaleHasNoSteps(double max) => Assert.Empty(ChartAxis.NiceSteps((decimal)max));

    [Theory]
    [InlineData(280842.11)]
    [InlineData(50000)]
    [InlineData(9999999.99)]
    [InlineData(17)]
    public void TheAxisNeverCrowdsPastTheTickBudget(double max) =>
        Assert.InRange(ChartAxis.NiceSteps((decimal)max).Count, 1, 4);

    [Theory]
    [InlineData(100000, "100k")]
    [InlineData(300000, "300k")]
    [InlineData(2500, "2.5k")]
    [InlineData(500, "500")]
    [InlineData(1000000, "1M")]
    [InlineData(1500000, "1.5M")]
    public void ATickIsLabelledInRoundMoney(int value, string expected) =>
        Assert.Equal(expected, ChartAxis.MoneyTick(value));

    // ---- the month labels ----

    [Fact]
    public void WhatTheCurrentFormatPrintsIsNotWhatTheMockupDraws()
    {
        var july = new DateOnly(2025, 7, 1);
        var invariant = CultureInfo.InvariantCulture;

        // The build's own format: no apostrophe at all.
        Assert.Equal("Jul 25", july.ToString("MMM yy", invariant));
        // And the obvious fix is a trap: in a .NET custom format a quote delimits a literal, so '' is an
        // EMPTY literal and still prints no apostrophe.
        Assert.Equal("Jul 25", july.ToString("MMM ''yy", invariant));
        // An escaped quote does print it.
        Assert.Equal("Jul '25", july.ToString("MMM \\'yy", invariant));
    }

    [Theory]
    [InlineData(2025, 7, 1, "Jul '25")]
    [InlineData(2026, 1, 1, "Jan '26")]
    [InlineData(2099, 12, 31, "Dec '99")]
    [InlineData(2005, 3, 4, "Mar '05")]
    public void AMonthIsLabelledAsTheMockupDrawsIt(int year, int month, int day, string expected) =>
        Assert.Equal(expected, ChartAxis.MonthYearLabel(new DateOnly(year, month, day)));

    [Fact]
    public void TheLabelDoesNotFollowTheDevicesLanguage()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("Jul '25", ChartAxis.MonthYearLabel(new DateOnly(2025, 7, 1)));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void TheSheetsSeventyWeeksAreLabelledAtJulJanJul()
    {
        var ticks = ChartAxis.MonthTicks(SheetWeeks);

        // The week that CONTAINS the first of each month: Tue 1 Jul 2025 is in the week of Fri 27 Jun
        // (index 4), Thu 1 Jan 2026 in the week of Fri 26 Dec (30), Wed 1 Jul 2026 in Fri 26 Jun (56) —
        // which are the mockup's three label positions (19.3, 130.7, 242.1 of 300 = 4.5, 30.5, 56.5 slots).
        Assert.Equal([4, 30, 56], ticks.Select(t => t.WeekIndex));
        Assert.Equal(["Jul '25", "Jan '26", "Jul '26"], ticks.Select(t => t.Label));
    }

    [Fact]
    public void ABoundaryBeyondTheLastWeekIsNotDrawnAtTheLastWeek()
    {
        // 1 Jul 2026 is a Wednesday: its week is Fri 26 Jun, so a series ending Fri 19 Jun has not
        // reached it, and ending Fri 26 Jun has.
        var upTo19 = SheetWeeks.Where(w => w <= new DateOnly(2026, 6, 19)).ToList();
        var upTo26 = SheetWeeks.Where(w => w <= new DateOnly(2026, 6, 26)).ToList();

        Assert.Equal(["Jul '25", "Jan '26"], ChartAxis.MonthTicks(upTo19).Select(t => t.Label));
        Assert.Equal(["Jul '25", "Jan '26", "Jul '26"], ChartAxis.MonthTicks(upTo26).Select(t => t.Label));
    }

    [Fact]
    public void ABoundaryBeforeTheFirstWeekIsNotDrawn()
    {
        // The series starts Fri 2 Jan 2026, after 1 Jan.
        var weeks = Enumerable.Range(0, 30).Select(n => new DateOnly(2026, 1, 2).AddDays(7 * n)).ToList();

        Assert.Equal(["Jul '26"], ChartAxis.MonthTicks(weeks).Select(t => t.Label));
    }

    [Fact]
    public void ARangeWithNoSixMonthBoundaryStillGetsItsFirstWeekLabelled()
    {
        var weeks = Enumerable.Range(0, 4).Select(n => new DateOnly(2026, 9, 4).AddDays(7 * n)).ToList();

        var tick = Assert.Single(ChartAxis.MonthTicks(weeks));
        Assert.Equal(0, tick.WeekIndex);
        Assert.Equal("Sep '26", tick.Label);
    }

    [Fact]
    public void NoWeeksHaveNoLabels() => Assert.Empty(ChartAxis.MonthTicks([]));

    // ---- copy ----

    [Fact]
    public void TheFootnoteIsTheMockupsWithTheRealFirstMonth()
    {
        Assert.Equal(
            "May 2025 to now · each panel runs from $0 to that loan’s own peak",
            SnowballHistoryCopy.Footnote(new DateOnly(2025, 5, 30)));
        Assert.Equal(
            "October 2026 to now · each panel runs from $0 to that loan’s own peak",
            SnowballHistoryCopy.Footnote(new DateOnly(2026, 10, 2)));
    }

    [Fact]
    public void TheFootnoteWithNoWeeksDropsThePrefixInsteadOfInventingOne() =>
        Assert.Equal("Each panel runs from $0 to that loan’s own peak.", SnowballHistoryCopy.Footnote(null));

    [Fact]
    public void TheFootnoteIgnoresTheDevicesLanguage()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.StartsWith("May 2025 to now", SnowballHistoryCopy.Footnote(new DateOnly(2025, 5, 30)), StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void TheTouchedWeekIsNamedByItsDate() =>
        Assert.Equal("Sep 25, 2026", SnowballHistoryCopy.WeekCaption(new DateOnly(2026, 9, 25)));

    [Fact]
    public void TheEmptyStatesAreOnePlainSentenceEach()
    {
        foreach (var sentence in new[] { SnowballHistoryCopy.EmptyHistory, SnowballHistoryCopy.EmptyLoans })
        {
            Assert.EndsWith(".", sentence, StringComparison.Ordinal);
            Assert.DoesNotContain("!", sentence, StringComparison.Ordinal);
            Assert.True(sentence.Length is > 20 and < 140, sentence);
        }
    }

    [Theory]
    [InlineData("Maple Ridge Mortgage", true, "Maple Ridge Mortgage (mortgage)")]
    [InlineData("Maple Ridge Mortgage", false, "Maple Ridge Mortgage")]
    [InlineData("Harbor Auto Finance", false, "Harbor Auto Finance")]
    public void OnlyTheMortgagePanelCarriesTheSuffix(string name, bool isMortgage, string expected) =>
        Assert.Equal(expected, SnowballHistoryCopy.PanelName(name, isMortgage));

    // ---- how the app knows a loan is the mortgage: Plaid's subtype, never the name ----

    [Theory]
    [InlineData(AccountKind.Loan, "mortgage", true)]
    [InlineData(AccountKind.Loan, "Mortgage", true)]
    [InlineData(AccountKind.Loan, "student", false)]
    [InlineData(AccountKind.Loan, null, false)]
    [InlineData(AccountKind.Credit, "mortgage", false)]
    public void AMortgageIsALoanPlaidCallsAMortgage(AccountKind kind, string? subtype, bool expected) =>
        Assert.Equal(expected, AccountStatus.IsMortgage(new Account { Name = "x", Kind = kind, Subtype = subtype }));

    [Fact]
    public void ANameLikeMapleRidgeMortgageIsNotWhatMakesAMortgage() =>
        Assert.False(AccountStatus.IsMortgage(new Account { Name = "Maple Ridge Mortgage mortgage", Kind = AccountKind.Loan }));

    [Fact]
    public async Task TheHistoryFlagsTheMortgageBySubtypeAndNothingElse()
    {
        using var db = new TestDatabase();
        var now = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        await using (var context = db.CreateDbContext())
        {
            var institution = new Institution { Name = "Bank", AccessToken = string.Empty, CreatedAtUtc = now.UtcDateTime };
            institution.Accounts.Add(new Account
            {
                Name = "Home", Kind = AccountKind.Loan, Subtype = "mortgage", CurrentBalance = 100m, Apr = 0.04m, NextPaymentAmount = 10m,
            });
            institution.Accounts.Add(new Account
            {
                Name = "Maple Ridge Mortgage", Kind = AccountKind.Loan, Subtype = "auto", CurrentBalance = 100m, Apr = 0.04m, NextPaymentAmount = 10m,
            });
            context.Institutions.Add(institution);
            await context.SaveChangesAsync();
        }

        var history = await new SnowballService(db, new FakeClock(now)).GetHistoryAsync(default);

        Assert.True(history.LoansInFocusOrder.Single(l => l.Name == "Home").IsMortgage);
        Assert.False(history.LoansInFocusOrder.Single(l => l.Name == "Maple Ridge Mortgage").IsMortgage);
    }

    // ---- the empty history ----

    [Fact]
    public void AHistoryWithNoWeeksOrNoLoansIsEmpty()
    {
        var loan = new SnowballLoanHistory(Guid.NewGuid(), "A", [new SnowballHistoryPoint(new DateOnly(2026, 1, 2), 5m)], 5m, false);

        Assert.True(new SnowballHistory([], [loan]).IsEmpty);
        Assert.True(new SnowballHistory([new DateOnly(2026, 1, 2)], []).IsEmpty);
        Assert.False(new SnowballHistory([new DateOnly(2026, 1, 2)], [loan]).IsEmpty);
    }

    [Fact]
    public async Task ADatabaseWithNoSnapshotsGivesAnEmptyHistory()
    {
        using var db = new TestDatabase();
        var history = await new SnowballService(db, new FakeClock(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero)))
            .GetHistoryAsync(default);

        Assert.True(history.IsEmpty);
    }
}
