using System.Text.RegularExpressions;
using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Source-level half of Guard R-G10 (the behavioural half is <c>CalendarDateTests</c>). The Android head
/// cannot be loaded by this desktop-TFM project, so what has to be true of the display and entry sites is
/// read out of the files themselves.
///
/// <para>A date-only value (a next payment due date, a transaction's posted date) is stored as UTC
/// midnight, so converting it to the phone's zone shows the evening before in every UTC-minus zone —
/// "Due Oct 2" for an Oct 3 bill. Genuine instants (last refreshed, a balance's "as of") are the ones
/// that should be converted, and are not touched here.</para>
/// </summary>
public class DateOnlyDisplayTests
{
    // The identifiers that carry a date-only value at the display and entry sites. A new date-only
    // field that is read through a different name is a decision for this list, not a silent gap.
    private static readonly Regex DateOnlyConvertedToLocal = new(
        @"\b(dueDate|nextDue\w*|NextPaymentDueDate|transaction\.Date|\w+\.Date)\s*\??\.(LocalDateTime|ToLocalTime\(\)|DateTime\b)",
        RegexOptions.Compiled);

    private static readonly Regex PickerStoredAsOffset = new(
        @"new DateTimeOffset\([^;]*TimeSpan\.Zero", RegexOptions.Compiled);

    [Fact]
    public void NoDateOnlyFieldIsConvertedToLocalTimeForDisplay()
    {
        var violations = new List<string>();
        foreach (var file in PageSources())
        {
            var text = File.ReadAllText(file);
            foreach (Match match in DateOnlyConvertedToLocal.Matches(text))
            {
                // "asOf.Date" etc. are not in play; a DateTimeOffset named for an instant is exempt by name.
                if (match.Value.StartsWith("asOf", StringComparison.Ordinal))
                    continue;
                violations.Add($"{Path.GetFileName(file)}:{LineOf(text, match.Index)}  {match.Value}");
            }
        }

        Assert.True(violations.Count == 0,
            "A date-only value (due date / transaction date) is shown through the phone's zone, so it reads "
            + "a day early in the US. Read it with CalendarDate instead:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void NoDatePickerValueIsStoredWithAnOffsetOfItsOwn()
    {
        var violations = new List<string>();
        foreach (var file in PageSources())
        {
            var text = File.ReadAllText(file);
            foreach (Match match in PickerStoredAsOffset.Matches(text))
                violations.Add($"{Path.GetFileName(file)}:{LineOf(text, match.Index)}  {match.Value}");
        }

        Assert.True(violations.Count == 0,
            "A date is wrapped in a zero-offset DateTimeOffset by hand in the head (and DateTime.Today, of kind "
            + "Local, throws when paired with a zero offset). Store a picked day with CalendarDate.ToStored:\n"
            + string.Join("\n", violations));
    }

    [Theory]
    [InlineData("Ledger.Android/Pages/MainPage.xaml.cs", 1)]       // stored due into DayChooser (the picked day goes out through ManualAccountPatch.OfNextDue -> CalendarDate.ToStored, in Domain). The household due, card due and transaction row moved into Domain formatters in remediation 3.3 — see TheDateFormattersTheAccountsPageDelegatesToReadCalendarDate
    [InlineData("Ledger.Android/Pages/BudgetPage.xaml.cs", 3)]     // list row + recategorize subtitle (two)
    [InlineData("Ledger.Android/Pages/AddManualAccountPage.xaml.cs", 1)]
    public void TheNamedSitesReadTheirDatesThroughCalendarDate(string ledgerRelativePath, int minimumUses)
    {
        var text = File.ReadAllText(Path.Combine(DesignTestHelpers.LedgerRoot(), ledgerRelativePath));

        var uses = Regex.Matches(text, @"CalendarDate\.").Count;

        Assert.True(uses >= minimumUses,
            $"{ledgerRelativePath} reads CalendarDate {uses} time(s); the date-only sites need at least {minimumUses}.");
    }

    /// <summary>
    /// Remediation 3.3 moved the due line, the transaction row's day and the balance's "From Post" day out of
    /// MainPage into Domain formatters, each tested in UTC-7 / UTC+10 beside its behaviour. The page must still
    /// go through them (never print the date itself), and they must still read the date-only values through
    /// <c>CalendarDate</c>.
    /// </summary>
    [Theory]
    [InlineData("Ledger.Domain/Model/AccountCardText.cs", "AccountCardText.DueLine(")]
    [InlineData("Ledger.Domain/Model/TransactionRowText.cs", "TransactionRowText.WhenAndCategory(")]
    [InlineData("Ledger.Domain/Model/BalanceDisplay.cs", "BalanceDisplay.UpdatedLine(")]
    public void TheDateFormattersTheAccountsPageDelegatesToReadCalendarDate(string formatterPath, string pageCall)
    {
        var formatter = File.ReadAllText(Path.Combine(DesignTestHelpers.LedgerRoot(), formatterPath));
        var page = File.ReadAllText(Path.Combine(DesignTestHelpers.LedgerRoot(), "Ledger.Android/Pages/MainPage.xaml.cs"));

        Assert.Contains("CalendarDate.", formatter, StringComparison.Ordinal);
        Assert.Contains(pageCall, page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGuardsOwnPatternMatchesTheOldShapes()
    {
        Assert.Matches(DateOnlyConvertedToLocal, "TimeFormats.Stamp(dueDate.LocalDateTime)");
        Assert.Matches(DateOnlyConvertedToLocal, "transaction.Date.LocalDateTime:MMM d");
        Assert.Matches(DateOnlyConvertedToLocal, "account.NextPaymentDueDate?.LocalDateTime");
        Assert.Matches(PickerStoredAsOffset, "new DateTimeOffset(nextDuePicker.Date ?? DateTime.Today, TimeSpan.Zero)");
        Assert.Matches(PickerStoredAsOffset, "new DateTimeOffset(chosenDate, TimeSpan.Zero)");
        Assert.DoesNotMatch(DateOnlyConvertedToLocal, "asOf.LocalDateTime");
    }

    private static IEnumerable<string> PageSources()
    {
        var pages = Path.Combine(DesignTestHelpers.LedgerRoot(), "Ledger.Android");
        return Directory.EnumerateFiles(pages, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/obj/") && !f.Replace('\\', '/').Contains("/bin/"));
    }

    private static int LineOf(string text, int index) => text[..index].Count(c => c == '\n') + 1;
}
