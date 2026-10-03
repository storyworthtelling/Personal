using System.Text.RegularExpressions;
using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Remediation 3.8 (mockup 4), the halves a desktop-TFM project can only read from the files: that the Add
/// page lets <c>ManualAccountForm</c> decide its fields, keeps the "Blank: its own name" hint on the caption
/// row rather than in a placeholder that vanishes on typing, saves a cadence only when one was chosen, writes
/// the mockup's shape on blur, and that a card's limit reaches the Accounts card. The behavioural halves are
/// <c>ManualAccountFormTests</c>, <c>AccountServiceManualAddTests</c> and <c>FigureParserTests</c>.
/// </summary>
public class AddManualAccountFormSourceTests
{
    private const string AddPagePath = "Ledger.Android/Pages/AddManualAccountPage.xaml.cs";
    private const string MainPagePath = "Ledger.Android/Pages/MainPage.xaml.cs";

    // ---- R-G30: Kind re-lays out the form in place ----

    [Fact]
    public void KindDecidesWhichRowsShowAndChangingItRelaysOutInPlace()
    {
        var add = Source(AddPagePath);

        Assert.Contains("ManualAccountForm.FieldsFor(", add, StringComparison.Ordinal);
        Assert.Contains("kindSegmented.Picked +=", add, StringComparison.Ordinal);
        Assert.Contains("ManualField.CreditLimit", add, StringComparison.Ordinal);
        Assert.Contains("ManualField.Apr", add, StringComparison.Ordinal);
        Assert.Contains("ManualField.Every", add, StringComparison.Ordinal);
        Assert.Contains("ManualField.NextDue", add, StringComparison.Ordinal);

        // In place: the rows are toggled, never rebuilt, so what was typed is kept. (The Wrap guard already
        // forbids a second Content assignment after the frame is up.)
        Assert.Contains(".IsVisible = ", MethodBody(add, "private void ApplyKind("), StringComparison.Ordinal);
        Assert.DoesNotContain("Entry.Text = ", MethodBody(add, "private void ApplyKind("), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheFieldsOnShowAreCheckedAndSaved()
    {
        var add = Source(AddPagePath);
        var onAdd = MethodBody(add, "private async void OnAddAccountClicked(");

        // A figure typed before switching Kind, now hidden, must not block Add or be saved.
        Assert.Contains("ManualField.", onAdd, StringComparison.Ordinal);
        Assert.Contains("creditLimit:", onAdd, StringComparison.Ordinal);
    }

    // ---- R-G32: a cadence nobody chose is not stored ----

    [Fact]
    public void TheAddPageStoresACadenceOnlyThroughTheRule()
    {
        var add = Source(AddPagePath);

        Assert.Contains("ManualAccountForm.CadenceToStore(", add, StringComparison.Ordinal);
        Assert.Contains("everyTouched", add, StringComparison.Ordinal);
        // The old shape: the always-picked control read as "chosen".
        Assert.DoesNotContain("Enum.TryParse<PaymentCadence>", add, StringComparison.Ordinal);
    }

    // ---- R-G35: the hint stays visible ----

    [Fact]
    public void TheBankHintIsALabelOnTheCaptionRow_NotAPlaceholderThatVanishesOnTyping()
    {
        var add = Source(AddPagePath);

        // The hint is handed to the caption row, which draws it as a Label beside the field's name ...
        Assert.Contains("FieldRow(\"Bank or lender\", bankBox, ManualAccountForm.BankHint)", add, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"new Label\s*\{\s*Text = hint,"), MethodBody(add, "private static View FieldRow("));
        // ... and never as the Entry's placeholder, which disappears the moment the owner types.
        Assert.DoesNotMatch(new Regex(@"Placeholder\s*=\s*ManualAccountForm\.BankHint"), add);
        Assert.DoesNotContain("Blank: its own name", add, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlaceholdersAreTheFormsOwnInventedExamples_NeverLiteralsInThePage()
    {
        var add = Source(AddPagePath);

        foreach (var example in new[]
                 {
                     "NameExample", "BankExample", "BalanceExample", "LimitExample", "AprExample", "PaymentExample",
                 })
        {
            Assert.Matches(new Regex(@"Placeholder\s*=\s*ManualAccountForm\." + example + @"\b"), add);
        }

        Assert.DoesNotContain("\"6.25\"", add, StringComparison.Ordinal);
        Assert.DoesNotContain("\"$0.00\"", add, StringComparison.Ordinal);
    }

    // ---- R-G36: a valid figure is written back in the mockup's shape on blur ----

    [Fact]
    public void LeavingAValidFigureWritesItBackInTheMockupsShape()
    {
        var add = Source(AddPagePath);

        Assert.Contains("FigureParser.FormatForBox(", add, StringComparison.Ordinal);
    }

    // ---- the segmented controls fill the width (mockup .seg span{flex:1}) ----

    [Fact]
    public void KindAndEveryFillTheWidth()
    {
        var add = Source(AddPagePath);

        Assert.Matches(new Regex(@"kindSegmented\s*=\s*new\([^;]*fillWidth:\s*true\)"), add);
        Assert.Matches(new Regex(@"everySegmented\s*=\s*new\([^;]*fillWidth:\s*true\)"), add);
    }

    // ---- R-G37: a hand-entered card's limit reaches its Accounts card, as a Plaid card's does ----

    [Fact]
    public void TheAccountsCardDrawsTheLimitFromTheAccountWhateverItsSource()
    {
        var rows = MethodBody(Source(MainPagePath), "private static IReadOnlyList<IndexCardRow> AccountCardRows(");

        Assert.Contains("AccountCardText.LimitLine(", rows, StringComparison.Ordinal);
        Assert.Contains("account.CreditLimit", rows, StringComparison.Ordinal);
        Assert.DoesNotContain("AccountSource", rows, StringComparison.Ordinal);
        Assert.DoesNotContain("$\"of ", rows, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePostItOffersACardItsLimitThroughThePerFieldPatch()
    {
        var postIt = MethodBody(Source(MainPagePath), "private View BuildManualPostIt(");

        Assert.Contains("ManualField.CreditLimit", postIt, StringComparison.Ordinal);
        Assert.Contains("new ManualAccountPatch { CreditLimit = v }", postIt, StringComparison.Ordinal);
        Assert.Contains("\"Credit limit\"", postIt, StringComparison.Ordinal);
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
