using System.Text.RegularExpressions;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// The scan's head files are compile-checked, not run, so what can be said about them is said by reading the source: the errand
/// aims at Plate by name and carries nothing, no picture type is touched, no toast or dialog, no literal colour or size, the
/// actions live in the pinned bar and every decision comes from the models. Nothing here says the page looks like book page 14;
/// the owner has not compared them.
/// </summary>
public class ScanSourceTests
{
    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static IReadOnlyList<string> Files() =>
        ConstitutionSource.Files(".cs")
            .Where(file =>
            {
                var relative = ConstitutionSource.Relative(file);
                return (relative.StartsWith("Constitution.Android/MealsTab/Scan", StringComparison.Ordinal))
                    || relative == "Constitution.Android/RecipeScanPermission.cs";
            })
            .ToList();

    private static string All() => string.Join("\n", Files().Select(File.ReadAllText));

    private static readonly Regex Literal = new(
        @"""#[0-9A-Fa-f]{6,8}""|Color\.ParseColor\(\s*""|SetTextSize\([^;]*,\s*[0-9]|TextSize\s*=\s*[0-9]",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Toast = new(@"Toast|Snackbar", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Dialog = new(
        "AlertDia" + "log|Are you " + "sure|DialogFragment|new Dia" + "log\\(",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex EmptyCatch = new(@"catch\s*(\([^)]*\))?\s*\{\s*\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Pictures = new(
        @"\bUri\b|Bitmap|Stream\b|byte\[\]|ClipData|SetData|SetDataAndType|GrantReadUriPermission|PutExtra|MediaStore|Drawable\.CreateFrom|BitmapFactory|\.Data\b|GetParcelableExtra|GetByteArrayExtra",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    [Fact]
    public void TheGuardReadsTheScanFilesAndIsNotVacuous()
    {
        var names = Files().Select(ConstitutionSource.Relative).ToList();

        Assert.Contains("Constitution.Android/MealsTab/ScanRecipeLauncher.cs", names);
        Assert.Contains("Constitution.Android/MealsTab/ScanReadPage.cs", names);
        Assert.Contains("Constitution.Android/RecipeScanPermission.cs", names);
    }

    [Fact]
    public void NoColourOrTypeSizeIsWrittenOut()
    {
        var offenders = Files().Where(file => Literal.IsMatch(File.ReadAllText(file))).Select(ConstitutionSource.Relative).ToList();

        Assert.True(offenders.Count == 0, "A hex or a type size outside the token files:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void NoToastAndNoDialog()
    {
        Assert.DoesNotMatch(Toast, All());
        Assert.DoesNotMatch(Dialog, All());
        Assert.Matches(Toast, "Toast.MakeText(host, \"Kept\", ToastLength.Short);");
    }

    [Fact]
    public void NoEmptyCatch()
    {
        var offenders = Files().Where(file => EmptyCatch.IsMatch(File.ReadAllText(file))).Select(ConstitutionSource.Relative).ToList();

        Assert.True(offenders.Count == 0, "An empty catch:\n" + string.Join("\n", offenders));
        Assert.Matches(EmptyCatch, "try { x(); } catch (Exception) { }");
    }

    [Fact]
    public void NoPictureTypeIsTouched()
    {
        var offenders = Files().Where(file => Pictures.IsMatch(File.ReadAllText(file))).Select(ConstitutionSource.Relative).ToList();

        Assert.True(offenders.Count == 0, "A picture, stream, URI or extra put on the errand:\n" + string.Join("\n", offenders));
        Assert.Matches(Pictures, "intent.PutExtra(\"image\", bytes);");
    }

    [Fact]
    public void NoPassphraseTokenOrKeyIsHandled()
    {
        Assert.DoesNotMatch(new Regex("(?<![A-Za-z])(passphrase|token|apikey)(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), All());
    }

    [Fact]
    public void TheErrandNamesPlatesComponentAndWaitsForTheAnswer()
    {
        var launcher = Read("Constitution.Android/MealsTab/ScanRecipeLauncher.cs");

        Assert.Contains("ScanRequest.Action", launcher, StringComparison.Ordinal);
        Assert.Contains("SetClassName(ScanRequest.Package, ScanRequest.Component)", launcher, StringComparison.Ordinal);
        Assert.Contains("StartActivityForResult", launcher, StringComparison.Ordinal);
        Assert.Contains("ActivityNotFoundException", launcher, StringComparison.Ordinal);
        Assert.Contains("SecurityException", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateChooser", launcher, StringComparison.Ordinal);
        Assert.DoesNotContain("SetPackage", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAnswerIsReadThroughTheContractNotByHand()
    {
        var launcher = Read("Constitution.Android/MealsTab/ScanRecipeLauncher.cs");

        Assert.Contains("ScanReturns.Decide", launcher, StringComparison.Ordinal);
        Assert.Contains("RecipeReadContract.TitleExtra", launcher, StringComparison.Ordinal);
        Assert.Contains("RecipeReadContract.IngredientsExtra", launcher, StringComparison.Ordinal);
        Assert.Contains("RecipeReadContract.StepsExtra", launcher, StringComparison.Ordinal);
        Assert.Contains("GetStringExtra", launcher, StringComparison.Ordinal);
    }

    [Fact]
    public void TheActionsLiveInTheModelAndThePinnedBar()
    {
        var area = Read("Constitution.Android/MealsArea.cs");

        Assert.Contains("PaperUi.BottomBar", area, StringComparison.Ordinal);
        Assert.Contains("scan.KeepAction", area, StringComparison.Ordinal);
        Assert.Contains("scan.ScanAgainAction", area, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Looks right\"", All() + area, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Scan again\"", All() + area, StringComparison.Ordinal);
        Assert.Contains("PrimaryActionRequested", area, StringComparison.Ordinal);
        Assert.Contains("RecipeBoxText.ScanAction", area, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepingGoesThroughTheKeeperSeamOnly()
    {
        var area = Read("Constitution.Android/MealsArea.cs");

        Assert.Contains("IRecipeKeeper", area, StringComparison.Ordinal);
        Assert.Contains("NoRecipeKeeper", area, StringComparison.Ordinal);
        Assert.Contains(".Keep(", area, StringComparison.Ordinal);
        Assert.DoesNotContain("RecipeMaps", area + All(), StringComparison.Ordinal);
        Assert.DoesNotContain("HouseholdRecords", area + All(), StringComparison.Ordinal);
        Assert.DoesNotContain("DriveHouseholdStore", area + All(), StringComparison.Ordinal);
    }

    [Fact]
    public void AReplacedDraftIsLetGoUndoFirst()
    {
        var area = Read("Constitution.Android/MealsArea.cs");

        Assert.Contains("ScanDiscardUndo", area, StringComparison.Ordinal);
        Assert.Contains("ConnectionNoticeBoard.With", area, StringComparison.Ordinal);
        Assert.Contains("scanUndo.Reverse(", area, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageIsDrawnFromTheModelAndNeverTheWordsOfOne()
    {
        var page = Read("Constitution.Android/MealsTab/ScanReadPage.cs");

        foreach (var member in new[] { "model.StatusSentence", "model.Label", "model.Lines", "model.Questions", "model.QuestionHeading", "IsOther" })
        {
            Assert.Contains(member, page, StringComparison.Ordinal);
        }

        Assert.Contains("PaperTheme.Wash(PaperTheme.CautionMark)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheActivityHandsTheAnswerToTheMealsArea()
    {
        var activity = Read("Constitution.Android/ConstitutionActivity.cs");

        Assert.Contains("mealsArea?.OnActivityResult(requestCode, resultCode, data)", activity, StringComparison.Ordinal);
    }
}
