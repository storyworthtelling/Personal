using System.Text.RegularExpressions;
using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Phase 4 (cosmetic drift), the Accounts family of screens: Accounts, a card's post-it, the Add chooser and
/// action cards, the hand-entered form and its edit fields, first-run, Settings (hers too) and the Waiting
/// page (mockups 2, 3, 4, 10, 11, 14, 15, 17).
///
/// <para>The Android head cannot be loaded by this desktop-TFM project, so what must be true of the drawing
/// code is read out of the files themselves, the way the sibling guards do. Each test is the reintroduction of
/// one audited drift: a limit printed with cents, a hyphen for a minus, a straight apostrophe in copy, a
/// "▸" for a chevron, a card gap of 22 where 8 is drawn, a black scrim where the paper-deep haze is drawn, a
/// muted secondary line where soft ink is drawn, an unboxed post-it field, a Remove row that is a bare label.
/// The behavioural halves are the pure tests beside them (<c>AccountsMoneyTests</c>, <c>CardChevronTests</c>,
/// <c>AccountsLayoutTests</c>, <c>FigureBareFormatTests</c>).</para>
/// </summary>
public class AccountsCosmeticSourceTests
{
    private const string MainPagePath = "Ledger.Android/Pages/MainPage.xaml.cs";

    // Every file whose strings the owner reads on one of these screens. Developer-facing log lines are skipped.
    private static readonly string[] CopyFiles =
    [
        "Ledger.Android/Pages/MainPage.xaml.cs",
        "Ledger.Android/Pages/SetupPage.xaml.cs",
        "Ledger.Android/Pages/SetupPage.xaml",
        "Ledger.Android/Pages/AddManualAccountPage.xaml.cs",
        "Ledger.Android/Pages/LedgerWaitingPage.cs",
        "Ledger.Android/HouseholdPages.cs",
        "Ledger.Android/Views/AddAccountChooserCard.cs",
        "Ledger.Android/Views/InstitutionActionCard.cs",
        "Ledger.Android/Views/IndexCardView.cs",
        "Ledger.Android/Views/PostItView.cs",
        "Ledger.Domain/Model/AccountNotices.cs",
        "Ledger.Domain/Model/ManualAccountEditing.cs",
        "Ledger.Domain/Model/ManualAccountForm.cs",
        "Ledger.Domain/Model/AccountCardText.cs",
        "Ledger.Domain/Household/SettingsCopy.cs",
        "Ledger.Domain/Household/FirstRun.cs",
    ];

    // ---- Money and type ----

    [Fact]
    public void TheOneMoneyFormatterIsTheDomainsAndNeverATwoDecimalCurrencyFormatHere()
    {
        var main = Source(MainPagePath);
        var money = MethodBody(main, "internal static string Money(");

        Assert.Contains("AccountsMoney.Cents(", money, StringComparison.Ordinal);
        Assert.DoesNotContain("\"C2\"", main, StringComparison.Ordinal);
    }

    [Fact]
    public void ALimitIsWholeDollarsAndAPaymentRoundsUpOnEveryAccountCard()
    {
        var main = Source(MainPagePath);

        foreach (var method in new[] { "private static IReadOnlyList<IndexCardRow> AccountCardRows(", "private View BuildHouseholdAccountCard(" })
        {
            var body = MethodBody(main, method);
            var wholes = Regex.Matches(body, @"AccountsMoney\.Whole\(").Count;
            var payments = Regex.Matches(body, @"AccountsMoney\.Payment\(").Count;
            Assert.True(wholes >= 1, $"{method} must print the limit through AccountsMoney.Whole (found {wholes}).");
            Assert.True(payments >= 1, $"{method} must print the payment through AccountsMoney.Payment, which rounds up (found {payments}).");
            Assert.DoesNotContain("NextPaymentAmount is { } amt ? AccountsMoney.Whole(", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ACardsBalanceStillKeepsItsCents()
    {
        var rows = MethodBody(Source(MainPagePath), "private static IReadOnlyList<IndexCardRow> AccountCardRows(");

        Assert.Contains("account.CurrentBalance is { } current ? Money(current", rows, StringComparison.Ordinal);
    }

    [Fact]
    public void NoUserVisibleStringCarriesAStraightApostrophe()
    {
        var offenders = StraightApostropheScan.Offenders();

        Assert.True(offenders.Count == 0,
            "Copy must use the curly apostrophe (\\u2019), as the mockups set it (log lines, PDF page text and model instructions are exempt, see StraightApostropheScan):\n"
            + string.Join("\n", offenders));
    }

    [Fact]
    public void TheInventedPlaidDoesntCallItSavingsLineIsGone()
    {
        var main = Source(MainPagePath);

        Assert.DoesNotContain("call it savings.\"", main.Replace("Plaid calls it savings", string.Empty), StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("Plaid doesn.t call it savings"), main);
    }

    // ---- Chevrons ----

    [Fact]
    public void NoTextTriangleStandsInForAChevronOnAnAccountsCard()
    {
        foreach (var path in new[] { MainPagePath, "Ledger.Android/Views/IndexCardView.cs" })
        {
            var code = StripComments(Source(path));
            Assert.DoesNotContain("\u25B8", code, StringComparison.Ordinal);
            Assert.DoesNotContain("\u25BE", code, StringComparison.Ordinal);
            Assert.DoesNotContain("\"▸\"", code, StringComparison.Ordinal);
            Assert.DoesNotContain("\"▾\"", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheCardDrawsItsChevronAsAShapeFromTheDomainsPaths()
    {
        var card = Source("Ledger.Android/Views/IndexCardView.cs");
        var drawable = Source("Ledger.Android/Views/ChevronDrawable.cs");

        Assert.Contains("CardChevron.Resolve(", card, StringComparison.Ordinal);
        Assert.Contains("ChevronDrawable", card, StringComparison.Ordinal);
        Assert.Contains("CardChevron.PathOf(", drawable, StringComparison.Ordinal);
        Assert.Contains("DrawPath", drawable, StringComparison.Ordinal);
        Assert.Contains("CardChevron.StrokeDp", drawable, StringComparison.Ordinal);

        var accounts = MethodBody(Source(MainPagePath), "private View BuildAccountCard(");
        Assert.Contains("CardChevron.For(expanded)", accounts, StringComparison.Ordinal);
    }

    // ---- Spacing ----

    [Fact]
    public void CardsAreEightApartAndTheHeadingIsEightFromItsFirstCard_WithTwentyTwoBetweenBanks()
    {
        var main = Source(MainPagePath);
        var render = MethodBody(main, "private void RenderInstitutions(");

        Assert.Contains("AccountsLayout.CardGapDp", render, StringComparison.Ordinal);
        Assert.Contains("AccountsLayout.GroupMarginDp", main, StringComparison.Ordinal);
        Assert.DoesNotContain("Spacing = 22", main, StringComparison.Ordinal);
    }

    [Fact]
    public void AHouseholdBankKeepsTheSameSpacingAsTheOwners()
    {
        var snapshot = MethodBody(Source(MainPagePath), "private void RenderFromHouseholdSnapshot(");

        Assert.Contains("AccountsLayout.CardGapDp", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("Spacing = 22", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePostItOverlapsItsCardAndIsIndentedByTheMockupsMargin()
    {
        var post = Source("Ledger.Android/Views/PostItView.cs");

        foreach (var side in new[] { "Left", "Top", "Right", "Bottom" })
        {
            Assert.Contains($"AccountsLayout.PostItMargin{side}Dp", post, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheWellIsPaddedTwentyFourAndSixteen()
    {
        var main = Source(MainPagePath);
        var chrome = MethodBody(main, "private void BuildChrome(");

        Assert.Contains("AccountsLayout.WellPaddingLeftDp", chrome, StringComparison.Ordinal);
        Assert.Contains("AccountsLayout.WellPaddingRightDp", chrome, StringComparison.Ordinal);
        Assert.DoesNotContain("new Thickness(16, 0, 0, 0)", chrome, StringComparison.Ordinal);

        var setup = Source("Ledger.Android/Pages/SetupPage.xaml");
        Assert.Contains("x:Name=\"SettingsWell\"", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("Padding=\"{StaticResource ScreenPadding}\"", setup, StringComparison.Ordinal);
        var code = Source("Ledger.Android/Pages/SetupPage.xaml.cs");
        Assert.Contains("SettingsWell.Padding", code, StringComparison.Ordinal);
        Assert.Contains("AccountsLayout.WellPaddingRightDp", code, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphPaperLiesBehindTheHeaderToo_FromTheFrameNotFromEachWell()
    {
        var binder = Source("Ledger.Android/Pages/LedgerBinderPage.cs");
        Assert.Contains("PaperBehindHeader", binder, StringComparison.Ordinal);
        Assert.Contains("GraphPaperBackground", binder, StringComparison.Ordinal);

        foreach (var path in new[]
                 {
                     MainPagePath, "Ledger.Android/Pages/SetupPage.xaml.cs", "Ledger.Android/Pages/LedgerWaitingPage.cs",
                     "Ledger.Android/Pages/AddManualAccountPage.xaml.cs",
                 })
        {
            var page = Source(path);
            Assert.Contains("PaperBehindHeader => true", page, StringComparison.Ordinal);
            Assert.DoesNotContain("new GraphPaperBackground", page, StringComparison.Ordinal);
        }
    }

    // ---- Materials ----

    [Fact]
    public void IndexCardSecondaryTextIsSoftInk_NotMuted()
    {
        var card = Source("Ledger.Android/Views/IndexCardView.cs");
        var row = MethodBody(card, "private static View BuildRow(");

        Assert.Contains("PaperResources.Colour(\"InkSoft\")", row, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"Text = row\.Left,[\s\S]*?TextColor = PaperResources\.Colour\(""InkSoft""\)"), row);
    }

    [Fact]
    public void ThePostItsSavingsFooterIsSoftInk_NotMuted()
    {
        var footer = MethodBody(Source(MainPagePath), "private View BuildSavingsSwitchFooter(");

        Assert.Matches(new Regex(@"Style = PaperResources\.Token<Style>\(""SecondaryLabel""\),\s*TextColor = PaperResources\.Colour\(""InkSoft""\)"), footer);
    }

    [Fact]
    public void ThePostItFieldsAreBoxedWithTheirOwnDollarAndPercent()
    {
        var box = Source("Ledger.Android/Views/FieldBox.cs");
        Assert.Contains("string? prefix", box, StringComparison.Ordinal);
        Assert.Contains("string? suffix", box, StringComparison.Ordinal);
        Assert.Contains("PaperDeep", box, StringComparison.Ordinal);
        Assert.Contains("AccountsLayout.FieldHeightDp", box, StringComparison.Ordinal);

        var main = Source(MainPagePath);
        var manual = MethodBody(main, "private View BuildManualPostIt(");
        Assert.Contains("FigureAffix.PrefixOf(", manual, StringComparison.Ordinal);
        Assert.Contains("FigureAffix.SuffixOf(", manual, StringComparison.Ordinal);
        Assert.Contains("FigureParser.FormatBare(", manual, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRequiredTintLaysOverTheMonoFieldsPaperDeep_NotAReplacementForIt()
    {
        var box = Source("Ledger.Android/Views/FieldBox.cs");

        Assert.Contains("RequiredTintAlpha", box, StringComparison.Ordinal);
        Assert.Contains("tintLayer", box, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChooserAndTheActionCardAreSetOverThePaperDeepHaze_NotABlackScrim()
    {
        foreach (var path in new[] { "Ledger.Android/Views/AddAccountChooserCard.cs", "Ledger.Android/Views/InstitutionActionCard.cs" })
        {
            var card = Source(path);
            Assert.DoesNotContain("Colors.Black", card, StringComparison.Ordinal);
            Assert.Contains("ActionCardHaze.Build(", card, StringComparison.Ordinal);
        }

        var haze = Source("Ledger.Android/Views/ActionCardHaze.cs");
        Assert.Contains("PaperDeep", haze, StringComparison.Ordinal);
        Assert.Contains("MaterialTokens.HazeAlpha", haze, StringComparison.Ordinal);
        Assert.Contains("AccountsLayout.ActionCardLeftDp", haze, StringComparison.Ordinal);
        Assert.Contains("AccountsLayout.ActionCardRightDp", haze, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHazeCoversTheWholePage_SoItIsHostedByTheFrameNotTheWell()
    {
        var binder = Source("Ledger.Android/Pages/LedgerBinderPage.cs");
        Assert.Contains("ShowOverlay(", binder, StringComparison.Ordinal);
        Assert.Contains("HideOverlay(", binder, StringComparison.Ordinal);

        var main = Source(MainPagePath);
        Assert.DoesNotContain("overlayHost", main, StringComparison.Ordinal);
        Assert.Contains("ShowOverlay(", main, StringComparison.Ordinal);
        Assert.Contains("HideOverlay(", main, StringComparison.Ordinal);
    }

    [Fact]
    public void ActionCardRowsCarryIconsAndThePrimaryRowIsSemiboldGreen()
    {
        var chooser = Source("Ledger.Android/Views/AddAccountChooserCard.cs");
        Assert.Contains("ActionIcon", chooser, StringComparison.Ordinal);
        Assert.Contains("\"AccentDeep\"", chooser, StringComparison.Ordinal);
        Assert.Contains("SansSemibold", chooser, StringComparison.Ordinal);

        var reorder = Source("Ledger.Android/Views/InstitutionActionCard.cs");
        Assert.Contains("ActionIcon", reorder, StringComparison.Ordinal);

        var icons = Source("Ledger.Android/Views/ActionIcon.cs");
        foreach (var name in new[] { "MoveUp", "MoveDown", "MoveToBottom", "Disconnect", "Bank", "Pencil" })
        {
            Assert.Contains(name, icons, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NextDueIsABoxedFieldWithAChevron_NotABareLabel()
    {
        var chooser = Source("Ledger.Android/Views/DayChooser.cs");

        Assert.Contains("ChevronDrawable(CardChevron.Down", chooser, StringComparison.Ordinal);
        Assert.Contains("AccountsLayout.FieldHeightDp", chooser, StringComparison.Ordinal);
        Assert.Contains("PaperBright", chooser, StringComparison.Ordinal);
    }

    // ---- Copy ----

    [Fact]
    public void TheEmptyStateStartsOneHundredEightyDownAndItsSentenceIsSoftInkTwoSeventyWide()
    {
        var empty = MethodBody(Source(MainPagePath), "private static View EmptyState(");

        Assert.Contains("AccountsLayout.EmptyStateTopDp", empty, StringComparison.Ordinal);
        Assert.Contains("AccountsLayout.EmptyStateSentenceWidthDp", empty, StringComparison.Ordinal);
        Assert.Contains("InkSoft", empty, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoveIsAFortyEightDpTargetWithARuleAboveIt()
    {
        var manual = MethodBody(Source(MainPagePath), "private View BuildManualPostIt(");

        Assert.Contains("AccountsLayout.RemoveHitDp", manual, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"removeRule|Remove rule", RegexOptions.IgnoreCase), manual);
    }

    [Fact]
    public void SettingsFieldsAreBoxed()
    {
        var setup = Source("Ledger.Android/Pages/SetupPage.xaml");

        foreach (var name in new[] { "ClientIdEntry", "SecretEntry", "VaultPassphraseEntry" })
        {
            Assert.Matches(
                new Regex(@"<Border Style=""\{StaticResource HairlineCard\}"" BackgroundColor=""\{StaticResource PaperDeep\}"" Padding=""12,0"" HeightRequest=""44"">\s*<Entry x:Name=""" + name),
                setup);
        }
    }

    // ---- helpers ----

    private static string StripComments(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*[\s\S]*?\*/|<!--[\s\S]*?-->", string.Empty);
        return Regex.Replace(withoutBlocks, @"(?<!:)//.*$", string.Empty, RegexOptions.Multiline);
    }

    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No method with signature '{signature}'.");

        var arrow = source.IndexOf("=>", start, StringComparison.Ordinal);
        var open = source.IndexOf('{', start);
        if (arrow >= 0 && (open < 0 || arrow < open))
        {
            return source[start..(source.IndexOf(';', arrow) + 1)];
        }

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

        throw new InvalidOperationException($"Unbalanced braces in '{signature}'.");
    }

    private static string Source(string ledgerRelativePath)
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), ledgerRelativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {ledgerRelativePath}");
        return File.ReadAllText(path);
    }
}
