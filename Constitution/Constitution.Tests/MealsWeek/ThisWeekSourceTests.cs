using System.Text.RegularExpressions;

namespace Constitution.Tests.MealsWeek;

/// <summary>
/// The head's views are compile-checked, not run here, so what can be said about This week and Today's dinner card is said
/// by reading their source: no toast, no dialog, no literal colour or size, no empty catch, no tap on a row, the action in the
/// pinned bar, every word taken from the models. Nothing here says the view looks like the book; the owner has not compared them.
/// </summary>
public class ThisWeekSourceTests
{
    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static readonly string[] Views =
    [
        "Constitution.Android/MealsTab/ThisWeekPage.cs",
        "Constitution.Android/MealsArea.cs",
        "Constitution.Android/Today/TodayPage.cs",
        "Constitution.Android/TodayArea.cs",
    ];

    private static string All() => string.Join("\n", Views.Select(Read));

    private static readonly Regex Literal = new(
        @"""#[0-9A-Fa-f]{6,8}""|Color\.ParseColor\(\s*""|SetTextSize\([^;]*,\s*[0-9]|TextSize\s*=\s*[0-9]",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Toast = new(@"Toast|Snackbar", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Dialog = new(
        "AlertDia" + "log|Conf" + "irm|Are you " + "sure|DialogFragment|new Dia" + "log\\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    // The scan's model is named for what it does (PBI C-6.5.3), and the name holds the word the dialog guard looks for. Only that
    // identifier is set aside; any other use of the word still fails the guard.
    private static string WithoutTheScanModelsName(string source) => source.Replace("ScanConf" + "irmModel", string.Empty, StringComparison.Ordinal);

    private static readonly Regex EmptyCatch = new(@"catch\s*(\([^)]*\))?\s*\{\s*\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Tap = new(
        @"SetOnClickListener|SetOnLongClickListener|\bClick\s*\+=|\bLongClick\s*\+=|\bClickable\s*=|SetClickable|HintChevron|[Cc]hevron",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    [Fact]
    public void TheGuardReadsTheViewsAndIsNotVacuous()
    {
        Assert.All(Views, view => Assert.True(Read(view).Length > 500, view));
    }

    [Fact]
    public void NoColourOrTypeSizeIsWrittenOut()
    {
        Assert.DoesNotMatch(Literal, All());
        Assert.Matches(Literal, "view.SetTextSize(ComplexUnitType.Sp, 15f);");
    }

    [Fact]
    public void NoToastNoDialogNoEmptyCatch()
    {
        Assert.DoesNotMatch(Toast, All());
        Assert.DoesNotMatch(Dialog, WithoutTheScanModelsName(All()));
        Assert.Matches(Dialog, WithoutTheScanModelsName("var ok = Conf" + "irm(host); var m = ScanConfirmModel.From(p);"));
        Assert.DoesNotMatch(EmptyCatch, All());
        Assert.Matches(Toast, "Toast.MakeText(host, \"x\", ToastLength.Short);");
        Assert.Matches(EmptyCatch, "try { x(); } catch (Exception) { }");
    }

    [Fact]
    public void NoRowOnTheWeekOrTheDinnerCardTakesATap()
    {
        Assert.DoesNotMatch(Tap, Read("Constitution.Android/MealsTab/ThisWeekPage.cs"));
        Assert.DoesNotMatch(Tap, Read("Constitution.Android/Today/TodayPage.cs"));
        Assert.Matches(Tap, "row.Click += OnTap;");
    }

    [Fact]
    public void TheActionStaysInThePinnedBar()
    {
        var area = Read("Constitution.Android/MealsArea.cs");

        Assert.Contains("ThisWeekBar", area, StringComparison.Ordinal);
        Assert.Contains("MealsWeekModel.ActionLabel", area, StringComparison.Ordinal);
        Assert.Contains("PaperUi.BottomBar", area, StringComparison.Ordinal);
        Assert.DoesNotContain("BottomBar", Read("Constitution.Android/MealsTab/ThisWeekPage.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSwitcherHeadsTheTabOnBothHalves()
    {
        Assert.Contains("MealsView.ThisWeek", Read("Constitution.Android/MealsArea.cs"), StringComparison.Ordinal);
        Assert.Contains("PaperUi.Segmented", Read("Constitution.Android/MealsTab/MealsPage.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDecisionIsTakenFromTheModels()
    {
        var text = All();

        foreach (var model in new[] { "MealsWeekModel.Build", "DinnerClipping.For", "DinnerClipping.PlanOf", "TodayModel.Dinner", "IMealWeekSource" })
        {
            Assert.Contains(model, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TodaysLineIsBoldFromTheModel()
    {
        Assert.Contains("line.IsToday", Read("Constitution.Android/MealsTab/ThisWeekPage.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void EarlierDaysAreDimmedFromTheModel()
    {
        var page = Read("Constitution.Android/MealsTab/ThisWeekPage.cs");

        Assert.Contains("line.IsPast", page, StringComparison.Ordinal);
        Assert.Contains("PaperTheme.InkMuted", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWeekIsListedInTheOrderTheModelGivesIt()
    {
        var page = Read("Constitution.Android/MealsTab/ThisWeekPage.cs");

        Assert.Contains("foreach (var line in model.Days)", page, StringComparison.Ordinal);
        Assert.DoesNotContain("OrderBy", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AReceiptIsDrawnForAnEveningOut()
    {
        var page = Read("Constitution.Android/Today/TodayPage.cs");

        Assert.Contains("ScrapContent.ReceiptLine", page, StringComparison.Ordinal);
        Assert.Contains("dinner.Paper", page, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPassphraseTokenOrKeyIsHandled()
    {
        Assert.DoesNotMatch(new Regex("(?<![A-Za-z])(passphrase|token|apikey)(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), All());
    }
}
