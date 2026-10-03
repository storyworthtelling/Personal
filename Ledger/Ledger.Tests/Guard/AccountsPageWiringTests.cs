using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Remediation 3.3 (mockups 2, 3 and 15): source-level halves of the guards for the Accounts page and its
/// post-it. The Android head cannot be loaded by this desktop-TFM project, so what must be true of the
/// wiring is read out of the files themselves — the same pattern <c>ManualPostItSourceTests</c> uses. The
/// behavioural halves are the pure tests beside them (<c>TransactionRowTextTests</c>, <c>AccountCardTextTests</c>,
/// <c>InstitutionPlacementTests</c>, <c>BalanceDisplayTests</c>, <c>NotePagingTests</c>).
///
/// <para>Each assertion is the reintroduction of one audited bug: the action card handed
/// <c>canMoveUp: true</c> for every bank, the fetch capped at 30, "Hand-entered", an "Account ····7301" label
/// for every depository account, a relative "As of 2h ago", a due cell left blank.</para>
/// </summary>
public class AccountsPageWiringTests
{
    private const string MainPagePath = "Ledger.Android/Pages/MainPage.xaml.cs";
    private const string PostItViewPath = "Ledger.Android/Views/PostItView.cs";

    // ---- item 6: the action card knows where the bank sits ----

    [Fact]
    public void TheActionCardIsHandedThePlacementNotAHardCodedTrue()
    {
        var card = MethodBody(Source(MainPagePath), "private void ShowInstitutionActionCard(");

        Assert.DoesNotContain("canMoveUp: true", card, StringComparison.Ordinal);
        Assert.DoesNotContain("canMoveDown: true", card, StringComparison.Ordinal);
        Assert.Contains("InstitutionPlacement.Of(", card, StringComparison.Ordinal);
        Assert.Contains("canMoveUp: placement.CanMoveUp", card, StringComparison.Ordinal);
        Assert.Contains("canMoveDown: placement.CanMoveDown", card, StringComparison.Ordinal);
        Assert.Contains("placement.Subtitle(", card, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCardsSubtitleIsNoLongerJustTheAccountCount()
    {
        var card = MethodBody(Source(MainPagePath), "private void ShowInstitutionActionCard(");

        Assert.DoesNotContain("account{(openCount == 1", card, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDimmedRowsStayUntappable_SoADimmedMoveUpCannotSilentlyDoNothing()
    {
        // The row helper moved to ActionCardHaze when the chooser and the reorder card began to share one setting
        // (Phase 4); the property is unchanged: a dimmed row is dimmed to 55% AND carries no tap.
        var card = Source("Ledger.Android/Views/InstitutionActionCard.cs");
        var row = Source("Ledger.Android/Views/ActionCardHaze.cs");

        Assert.Contains("Opacity = enabled ? 1.0 : 0.55", row, StringComparison.Ordinal);
        Assert.Contains("if (enabled", row, StringComparison.Ordinal);
        Assert.Contains("canMoveToBottom", card, StringComparison.Ordinal);
    }

    // ---- item 2: "n more" lengthens the note, and the fetch is no longer capped at 30 ----

    [Fact]
    public void TheTransactionFetchIsNotCappedAtThirty()
    {
        var load = MethodBody(Source(MainPagePath), "private async Task LoadTransactionsAsync(");

        Assert.DoesNotContain("count: 30", load, StringComparison.Ordinal);
        Assert.Contains("NotePaging.TransactionFetchCap", load, StringComparison.Ordinal);
        Assert.Contains("NotePaging.TransactionWindowDays", load, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMoreLineIsTappableAndPagesByNotePaging()
    {
        var main = Source(MainPagePath);
        var post = MethodBody(main, "private View BuildPostIt(");

        Assert.Contains("NotePaging.Shown(", post, StringComparison.Ordinal);
        Assert.Contains("NotePaging.AccountTransactionsInitial", post, StringComparison.Ordinal);
        Assert.DoesNotContain("Take(5)", post, StringComparison.Ordinal);
        Assert.DoesNotContain("transactions.Count - 5", post, StringComparison.Ordinal);
        Assert.Contains("onMore", post, StringComparison.Ordinal);

        var view = Source(PostItViewPath);
        var build = MethodBody(view, "public static View Build(");
        Assert.Contains("Action? onMore", view, StringComparison.Ordinal);
        Assert.Contains("TapGestureRecognizer", build, StringComparison.Ordinal);
    }

    [Fact]
    public void OpeningACardResetsTheTapCount_AndNothingElseWritesTheOpenCard()
    {
        var main = Source(MainPagePath);

        Assert.Contains("SetExpanded(", MethodBody(main, "private async void ToggleExpanded("), StringComparison.Ordinal);
        Assert.Contains("noteTaps.Opened(", MethodBody(main, "private void SetExpanded("), StringComparison.Ordinal);

        // SetExpanded is the ONLY writer of the open card, so the tap count cannot outlive the card it belongs to
        // (opening from a notice, removing the account, toggling — all go through it).
        var writes = System.Text.RegularExpressions.Regex.Matches(main, @"expandedAccountId\s*=[^=]").Count;
        Assert.Equal(1, writes);
    }

    // ---- item 1: category + relative date on each row ----

    [Fact]
    public void TheTransactionRowsComeFromTheTestedFormatterAndTheOwnersCategories()
    {
        var main = Source(MainPagePath);

        Assert.DoesNotContain("RelativeDayAndPending", main, StringComparison.Ordinal);
        Assert.Contains("TransactionRowText.WhenAndCategory(", main, StringComparison.Ordinal);
        Assert.Contains("GetCategoryNamesAsync(", MethodBody(main, "private async Task LoadTransactionsAsync("), StringComparison.Ordinal);
    }

    // ---- item 3: the balance line says where the balance came from ----

    [Fact]
    public void TheBalanceLineIsTheSourceAwareOne_NotALocalDateTimeOfAnInstantOrADate()
    {
        var main = Source(MainPagePath);

        Assert.Contains("BalanceDisplay.UpdatedLine(", main, StringComparison.Ordinal);
        Assert.DoesNotContain("$\"Updated {asOf.LocalDateTime", main, StringComparison.Ordinal);
    }

    [Fact]
    public void AReceiptWithNoStatementDateIsDatedOnThePhonesDay()
    {
        var service = Source("Ledger.Infrastructure/Services/AccountService.cs");
        var receipt = MethodBody(service, "public async Task<bool> ApplyBalanceReceiptAsync(");

        Assert.Contains("CalendarDate.PhoneDay(clock.UtcNow)", receipt, StringComparison.Ordinal);
        Assert.Contains("CalendarDate.ToStored(", receipt, StringComparison.Ordinal);
        Assert.Contains("BalanceSource.Post", receipt, StringComparison.Ordinal);
    }

    // ---- item 4: "No payment yet" ----

    [Fact]
    public void ACardOrLoanWithNoDueDateIsNeverLeftWithABlankLeftCell()
    {
        var main = Source(MainPagePath);

        Assert.Contains("AccountCardText.DueLine(", main, StringComparison.Ordinal);
        Assert.DoesNotContain("$\"Due {CalendarDate.Short", main, StringComparison.Ordinal);
    }

    // ---- items 5 and 8: "By hand" and the absolute "As of", for both installs' headings ----

    [Fact]
    public void BothHeadingsAskTheTestedNoteWhatToSay()
    {
        var main = Source(MainPagePath);

        Assert.DoesNotContain("\"Hand-entered\"", main, StringComparison.Ordinal);
        Assert.DoesNotContain("\"As of \" + RelativeTime", main, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(main, @"AccountCardText\.HeadingNote\(").Count);
    }

    // ---- item 7: the subtype labels, on both installs' cards ----

    [Fact]
    public void TheRowLabelsUseThePlaidSubtype_NotOneWordForEveryDepositAccount()
    {
        var main = Source(MainPagePath);

        Assert.DoesNotContain("private static string KindLabel(", main, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountKind.Depository => \"Account\"", main, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(main, @"AccountCardText\.MaskedLabel\(").Count);
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

    private static string Source(string ledgerRelativePath)
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), ledgerRelativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {ledgerRelativePath}");
        return File.ReadAllText(path);
    }
}
