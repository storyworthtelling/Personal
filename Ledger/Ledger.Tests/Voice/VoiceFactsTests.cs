using System.Text.RegularExpressions;
using Index.Domain.Household;
using Index.Domain.Voice;

namespace Index.Tests.Voice;

/// <summary>
/// R-G29 (f): the facts a seat may be told, computed from real data and expressed as words, percentages
/// and counts. The percentage is rounded to a whole number and spoken as a word ("about one percent" is
/// the mockup's own Accounts line), so these tests pin the rounding, the anchor month and the cases where
/// there is nothing honest to say — which is the case where the seat stays hidden.
/// </summary>
public class VoiceFactsTests
{
    // ---- Snowball: down from its peak, and how many loans are behind you ----

    [Fact]
    public void SnowballSaysHowFarDownFromThePeakAndWhichMonthThePeakWas()
    {
        var facts = VoiceFactBuilder.Snowball(VoiceTestSupport.TwelvePercentDown());

        Assert.True(facts.HasFacts);
        Assert.Equal(VoiceSeat.Snowball, facts.Seat);
        Assert.Contains(facts.Lines, line => line.Contains("twelve percent", StringComparison.Ordinal)
            && line.Contains("last November", StringComparison.Ordinal));
    }

    [Fact]
    public void SnowballCountsTheLoansAlreadyPaidOffInWords()
    {
        var facts = VoiceFactBuilder.Snowball(VoiceTestSupport.TwelvePercentDown());

        Assert.Contains(facts.Lines, line => line.Contains("Three loans", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePeakIsNamedByYearOnlyAsFarAsTheRelativePhraseGoes()
    {
        // This year, last year, and longer ago each get a phrase; none prints a year digit.
        var thisYear = VoiceTestSupport.History(
            [new(2026, 2, 6), new(2026, 9, 25)], ("A", false, [1000m, 900m]));
        var longAgo = VoiceTestSupport.History(
            [new(2024, 3, 1), new(2026, 9, 25)], ("A", false, [1000m, 900m]));

        Assert.Contains("in February", VoiceFactBuilder.Snowball(thisYear).Lines.First());
        Assert.Contains("more than a year ago", VoiceFactBuilder.Snowball(longAgo).Lines.First());
        Assert.DoesNotMatch(@"\d", string.Join(' ', VoiceFactBuilder.Snowball(longAgo).Lines));
    }

    [Fact]
    public void ALoanCountOfOneIsSingular()
    {
        var history = VoiceTestSupport.History(
            [new(2026, 8, 7), new(2026, 9, 25)], ("A", false, [500m, 500m]), ("B", true, [0m, 0m]));

        Assert.Equal(["One loan is already paid off."], VoiceFactBuilder.Snowball(history).Lines);
    }

    [Fact]
    public void AtThePeakWithNothingPaidOffThereIsNothingToSay()
    {
        var history = VoiceTestSupport.History(
            [new(2026, 8, 7), new(2026, 9, 25)], ("A", false, [500m, 500m]));

        Assert.False(VoiceFactBuilder.Snowball(history).HasFacts);
    }

    [Fact]
    public void LessThanAHalfPercentBelowThePeakIsNotACelebration()
    {
        // 0.4% down rounds to nothing: no "down zero percent" line.
        var history = VoiceTestSupport.History(
            [new(2025, 11, 7), new(2026, 9, 25)], ("A", false, [100000m, 99600m]));

        Assert.False(VoiceFactBuilder.Snowball(history).HasFacts);
    }

    // ---- Accounts: since an anchor month, rounded, and "about one percent" ----

    [Theory]
    [InlineData(100000, 99100, "one percent")]    // 0.9% rounds UP to one — the mockup's "about one percent"
    [InlineData(100000, 98600, "one percent")]    // 1.4% rounds DOWN to one
    [InlineData(100000, 99500, "one percent")]    // exactly half rounds away from zero
    [InlineData(90000, 88000, "two percent")]     // 2.2%
    public void AccountsRoundsTheChangeToAWholeNumberAndSpeaksItInWords(int august, int now, string words)
    {
        var facts = VoiceFactBuilder.Accounts(VoiceTestSupport.TotalsThenNow(august, now));

        Assert.Contains(facts.Lines, line => line.StartsWith("Total owed is down", StringComparison.Ordinal)
            && line.Contains($"down {words} since August", StringComparison.Ordinal));
    }

    [Fact]
    public void AccountsSaysTheDebtIsUpWhenItIsUp()
    {
        var facts = VoiceFactBuilder.Accounts(VoiceTestSupport.TotalsThenNow(100000m, 103000m));

        Assert.Contains(facts.Lines, line => line.Contains("up three percent since August", StringComparison.Ordinal));
    }

    [Fact]
    public void AccountsSaysAboutTheSameWhenTheChangeRoundsToNothing()
    {
        var facts = VoiceFactBuilder.Accounts(VoiceTestSupport.TotalsThenNow(100000m, 99600m));

        Assert.Contains(facts.Lines, line => line.Contains("about the same as it was in August", StringComparison.Ordinal));
        Assert.DoesNotContain(facts.Lines, line => line.Contains("percent", StringComparison.Ordinal));
    }

    [Fact]
    public void TheAnchorIsTheFirstRecordedWeekOfTheMonthBeforeTheLatestWeek()
    {
        // Latest week 25 Sep 2026 -> anchor month August -> the first week on or after 1 Aug is 7 Aug (90,000),
        // NOT the July week (100,000) and not the later August weeks.
        var history = VoiceTestSupport.History(
            [new(2026, 7, 10), new(2026, 8, 7), new(2026, 8, 28), new(2026, 9, 25)],
            ("A", false, [100000m, 90000m, 50000m, 88200m]));

        var facts = VoiceFactBuilder.Accounts(history);

        Assert.Contains(facts.Lines, line => line.Contains("down two percent since August", StringComparison.Ordinal));
    }

    [Fact]
    public void TheAnchorMonthWrapsAcrossTheNewYear()
    {
        var history = VoiceTestSupport.History(
            [new(2025, 12, 5), new(2026, 1, 30)], ("A", false, [1000m, 900m]));

        Assert.Contains(VoiceFactBuilder.Accounts(history).Lines, line => line.Contains("since December", StringComparison.Ordinal));
    }

    [Fact]
    public void AccountsHasNoTrendWhenTheHistoryDoesNotReachBackToTheMonthBefore()
    {
        // History that starts in the latest week's own month has no anchor; the count of loans can still stand.
        var history = VoiceTestSupport.History(
            [new(2026, 9, 4), new(2026, 9, 25)], ("A", false, [1000m, 900m]), ("B", true, [0m, 0m]));

        var facts = VoiceFactBuilder.Accounts(history);

        Assert.Equal(["One loan is already paid off."], facts.Lines);
    }

    [Fact]
    public void AnAnchorLessThanFourWeeksBeforeTheLatestIsNotATrend()
    {
        var history = VoiceTestSupport.History(
            [new(2026, 8, 28), new(2026, 9, 11)], ("A", false, [1000m, 900m]));

        Assert.False(VoiceFactBuilder.Accounts(history).HasFacts);
    }

    // ---- no data, damaged data: no facts, no line ----

    [Fact]
    public void NoHistoryIsNoFactsOnEverySeat()
    {
        Assert.False(VoiceFactBuilder.Accounts(null).HasFacts);
        Assert.False(VoiceFactBuilder.Snowball(null).HasFacts);
        Assert.False(VoiceFactBuilder.Accounts(new HouseholdSnowballHistory([], [])).HasFacts);
        Assert.False(VoiceFactBuilder.Savings([]).HasFacts);
    }

    [Fact]
    public void AHistoryWhoseArraysDoNotLineUpIsNotReadAtAll()
    {
        var damaged = new HouseholdSnowballHistory(
            [new(2025, 11, 7), new(2026, 9, 25)],
            [new HouseholdSnowballLoanHistory("A", false, false, [1000m])]);

        Assert.False(VoiceFactBuilder.Snowball(damaged).HasFacts);
        Assert.False(VoiceFactBuilder.Accounts(damaged).HasFacts);
    }

    // ---- Savings: a rate and nothing else ----

    [Fact]
    public void SavingsIsTheCombinedMonthlyPaceRoundedAsARate()
    {
        var facts = VoiceFactBuilder.Savings(VoiceTestSupport.SavingsAccounts());

        // 187.50 + 32.00 = 219.50 -> nearest ten dollars.
        Assert.Equal(["Taken together, the savings accounts are growing by about 220 dollars a month."], facts.Lines);
    }

    [Theory]
    [InlineData(42.3, 40)]
    [InlineData(97.6, 100)]
    [InlineData(187.5, 190)]
    [InlineData(1234.0, 1200)]
    [InlineData(1250.0, 1300)]
    public void TheRateIsRoundedToAFigureAPersonWouldSay(double pace, int expected)
    {
        Assert.Equal(expected, VoiceFactBuilder.RoundedRate((decimal)pace));
    }

    [Fact]
    public void AnAccountWithNoHistoryContributesNoPace()
    {
        var accounts = new List<HouseholdSavingsAccount>
        {
            new("Redwood Bank", "Savings", 8200m, null, 300m, 0),    // no history: its pace is a placeholder
            new("Lakeside CU", "Share savings", 1350m, null, 50m, 6),
        };

        Assert.Equal(["Taken together, the savings accounts are growing by about 50 dollars a month."],
            VoiceFactBuilder.Savings(accounts).Lines);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-120)]
    [InlineData(2)]     // rounds to nothing worth saying
    public void AFlatFallingOrTinyPaceIsNothingToSay(int pace)
    {
        var accounts = new List<HouseholdSavingsAccount> { new("Redwood Bank", "Savings", 8200m, null, pace, 6) };

        Assert.False(VoiceFactBuilder.Savings(accounts).HasFacts);
    }

    // ---- the hash the cache keys on ----

    [Fact]
    public void TheHashChangesWithTheFactsAndWithTheSeatAndNotWithAnythingElse()
    {
        var one = VoiceFactBuilder.Snowball(VoiceTestSupport.TwelvePercentDown());
        var same = VoiceFactBuilder.Snowball(VoiceTestSupport.TwelvePercentDown());
        var other = VoiceFactBuilder.Snowball(
            VoiceTestSupport.History([new(2025, 11, 7), new(2026, 9, 25)], ("A", false, [1000m, 700m])));
        var otherSeat = new VoiceFacts(VoiceSeat.Accounts, one.Lines);

        Assert.Equal(one.Hash, same.Hash);
        Assert.NotEqual(one.Hash, other.Hash);
        Assert.NotEqual(one.Hash, otherSeat.Hash);
        Assert.Matches("^[0-9a-f]{16}$", one.Hash);
    }

    // ---- the words ----

    [Theory]
    [InlineData(0, "zero")]
    [InlineData(1, "one")]
    [InlineData(12, "twelve")]
    [InlineData(21, "twenty-one")]
    [InlineData(40, "forty")]
    [InlineData(99, "ninety-nine")]
    [InlineData(100, "100")]
    public void NumberWordsSpellsWholeNumbersUpToNinetyNine(int number, string words)
    {
        Assert.Equal(words, NumberWords.Of(number));
    }

    [Fact]
    public void AccountsAndSnowballFactsCarryNoDigitsAtAll()
    {
        var all = VoiceFactBuilder.Accounts(VoiceTestSupport.TotalsThenNow(100000m, 99100m)).Lines
            .Concat(VoiceFactBuilder.Snowball(VoiceTestSupport.TwelvePercentDown()).Lines);

        Assert.DoesNotMatch(new Regex(@"[\d$]"), string.Join(' ', all));
    }
}
