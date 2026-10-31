using System.Text.RegularExpressions;

namespace Constitution.Tests.RecipeBoxScreens;

/// <summary>
/// The head's views are compile-checked, not run here, so what can be said about the recipe box's are said by reading their
/// source: no toast, no confirm dialog, no literal colour or size, the primary action in the pinned bar, every decision taken
/// from the models. Nothing here says the box looks like the book; the owner has not compared them.
/// </summary>
public class MealsTabSourceTests
{
    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static IReadOnlyList<string> Files() =>
        ConstitutionSource.Files(".cs")
            .Where(file =>
            {
                var relative = ConstitutionSource.Relative(file);
                return relative == "Constitution.Android/MealsArea.cs"
                    || relative.StartsWith("Constitution.Android/MealsTab/", StringComparison.Ordinal);
            })
            .ToList();

    private static string All() => string.Join("\n", Files().Select(File.ReadAllText));

    private static readonly Regex Literal = new(
        @"""#[0-9A-Fa-f]{6,8}""|Color\.ParseColor\(\s*""|SetTextSize\([^;]*,\s*[0-9]|TextSize\s*=\s*[0-9]",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Toast = new(@"Toast|Snackbar|ToastLength", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Dialog = new(
        "AlertDia" + "log|Conf" + "irm|Are you " + "sure|DialogFragment|new Dia" + "log\\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    // The scan's model is named for what it does (PBI C-6.5.3), and the name holds the word the dialog guard looks for. Only that
    // identifier is set aside; any other use of the word still fails the guard.
    private static string WithoutTheScanModelsName(string source) => source.Replace("ScanConf" + "irmModel", string.Empty, StringComparison.Ordinal);

    private static readonly Regex EmptyCatch = new(@"catch\s*(\([^)]*\))?\s*\{\s*\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    [Fact]
    public void TheGuardReadsTheTabAndIsNotVacuous()
    {
        Assert.True(Files().Count >= 5, "The Meals tab files were not found: " + Files().Count);
    }

    [Fact]
    public void NoColourOrTypeSizeIsWrittenOutInTheTab()
    {
        var offenders = Files().Where(file => Literal.IsMatch(File.ReadAllText(file))).Select(ConstitutionSource.Relative).ToList();

        Assert.True(offenders.Count == 0, "A hex or a type size outside the token files:\n" + string.Join("\n", offenders));
    }

    [Theory]
    [InlineData("var c = \"#FFFFFF\";")]
    [InlineData("view.SetTextSize(ComplexUnitType.Sp, 15f);")]
    public void TheLiteralDetectorFiresOnPlantedLiterals(string planted) => Assert.True(Literal.IsMatch(planted), planted);

    [Fact]
    public void NoToastAnywhereInTheTab()
    {
        var offenders = Files().Where(file => Toast.IsMatch(File.ReadAllText(file))).Select(ConstitutionSource.Relative).ToList();

        Assert.True(offenders.Count == 0, "A toast on the Meals tab:\n" + string.Join("\n", offenders));
        Assert.True(Toast.IsMatch("Toast.MakeText(host, \"Planned\", ToastLength.Short).Show();"));
    }

    [Fact]
    public void NoConfirmDialogAnywhereInTheTab()
    {
        var offenders = Files().Where(file => Dialog.IsMatch(WithoutTheScanModelsName(File.ReadAllText(file)))).Select(ConstitutionSource.Relative).ToList();

        Assert.True(offenders.Count == 0, "A dialog on the Meals tab:\n" + string.Join("\n", offenders));
        Assert.True(Dialog.IsMatch("new AlertDia" + "log.Builder(host)"));
        Assert.True(Dialog.IsMatch(WithoutTheScanModelsName("var ok = Conf" + "irm(host); var m = ScanConfirmModel.From(p);")));
    }

    [Fact]
    public void NoEmptyCatch()
    {
        var offenders = Files().Where(file => EmptyCatch.IsMatch(File.ReadAllText(file))).Select(ConstitutionSource.Relative).ToList();

        Assert.True(offenders.Count == 0, "An empty catch:\n" + string.Join("\n", offenders));
        Assert.True(EmptyCatch.IsMatch("try { x(); } catch (Exception) { }"));
    }

    [Fact]
    public void ThePrimaryActionLivesInThePinnedBar()
    {
        var area = Read("Constitution.Android/MealsArea.cs");

        Assert.Contains("BuildBottomBar", area, StringComparison.Ordinal);
        Assert.Contains("PaperUi.BottomBar", All(), StringComparison.Ordinal);
        Assert.Contains("PrimaryAction", All(), StringComparison.Ordinal);
    }

    [Fact]
    public void LoadingAndNoHouseholdFileAreStatusSentences()
    {
        var area = Read("Constitution.Android/MealsArea.cs");

        Assert.Contains("LoadingSentence", area, StringComparison.Ordinal);
        Assert.Contains("Loading = true", area, StringComparison.Ordinal);
        Assert.Contains("PageFurniture.Sentence", area, StringComparison.Ordinal);
        Assert.Contains("IRecipeBoxSource", area, StringComparison.Ordinal);
        Assert.Contains("NoRecipeBoxSource", area, StringComparison.Ordinal);
    }

    [Fact]
    public void AMoveFoldsTheSharedDogEarAndNothingElse()
    {
        var area = Read("Constitution.Android/MealsArea.cs");

        Assert.Contains("CuisineMoveUndo", area, StringComparison.Ordinal);
        Assert.Contains("ConnectionNoticeBoard.With", area, StringComparison.Ordinal);
        Assert.Contains("IUndoableArea", area, StringComparison.Ordinal);
        Assert.Contains("Reverse(", area, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSwitcherIsTheSegmentedControl()
    {
        Assert.Contains("PaperUi.Segmented", All(), StringComparison.Ordinal);
    }

    [Fact]
    public void SearchSitsUnderTheSwitcherAndNarrowsInPlace()
    {
        var meals = Read("Constitution.Android/MealsTab/MealsPage.cs");
        var page = Read("Constitution.Android/MealsTab/RecipeBoxPage.cs");

        Assert.True(meals.IndexOf("AddView(switcher", StringComparison.Ordinal) < meals.IndexOf("AddView(body", StringComparison.Ordinal));
        Assert.Contains("PaperUi.Field(", page, StringComparison.Ordinal);
        Assert.Contains("TextChanged", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Segmented", page, StringComparison.Ordinal);
        Assert.Contains("RecipeBoxScreenModel.Build", Read("Constitution.Android/MealsArea.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDecisionIsTakenFromTheModels()
    {
        var text = All();

        foreach (var model in new[] { "RecipeBoxScreenModel.Build", "PileLayout.Place", "CardPickModel.Face", "CuisineChoices.For", "CookSoon.Press", "CardSelection" })
        {
            Assert.Contains(model, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ACardIsLongPressedForItsCuisineAndTappedToComeOut()
    {
        var text = All();

        Assert.Contains("LongClick", text, StringComparison.Ordinal);
        Assert.Contains("Click", text, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPassphraseTokenOrKeyIsHandled()
    {
        Assert.DoesNotMatch(new Regex("(?<![A-Za-z])(passphrase|token|apikey)(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), All());
    }
}
