using System.Text.RegularExpressions;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// The picker and the typed page are compile-checked, not run, so what can be said about them is said by reading the source: no
/// toast or dialog, no literal colour or size, no empty catch, no picture type, no camera permission, the actions only in the pinned
/// bar and every word from the models. Nothing here says the picker looks like book page 14; the owner has not compared them.
/// </summary>
public class PickerSourceTests
{
    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static IReadOnlyList<string> Files() =>
        ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Android/MealsTab/Picker", StringComparison.Ordinal))
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
        @"\bUri\b|Bitmap|Stream\b|byte\[\]|ClipData|SetData|PutExtra|MediaStore|BitmapFactory|\.Data\b|GetParcelableExtra",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    [Fact]
    public void TheGuardReadsThePickerFilesAndIsNotVacuous()
    {
        var names = Files().Select(ConstitutionSource.Relative).ToList();

        Assert.Contains("Constitution.Android/MealsTab/PickerSheet.cs", names);
        Assert.Contains("Constitution.Android/MealsTab/PickerTypedPage.cs", names);
    }

    [Fact]
    public void NoColourOrTypeSizeIsWrittenOut()
    {
        var offenders = Files().Where(file => Literal.IsMatch(File.ReadAllText(file))).Select(ConstitutionSource.Relative).ToList();

        Assert.True(offenders.Count == 0, "A hex or a type size outside the token files:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void NoToastNoDialogNoEmptyCatch()
    {
        Assert.DoesNotMatch(Toast, All());
        Assert.DoesNotMatch(Dialog, All());
        Assert.DoesNotMatch(EmptyCatch, All());
        Assert.Matches(EmptyCatch, "try { x(); } catch (Exception) { }");
    }

    [Fact]
    public void NoPictureTypeIsTouched()
    {
        Assert.DoesNotMatch(Pictures, All());
        Assert.Matches(Pictures, "var picture = Bitmap.CreateBitmap(1, 1);");
    }

    [Fact]
    public void NoCameraPermissionOrCaptureIntentExistsYet()
    {
        var heads = ConstitutionSource.Files(".cs")
            .Where(file => ConstitutionSource.Relative(file).StartsWith("Constitution.Android/", StringComparison.Ordinal))
            .Select(File.ReadAllText);
        var all = string.Join("\n", heads);

        Assert.DoesNotContain("permission.CAMERA", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Manifest.Permission.Camera", all, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionImageCapture", all, StringComparison.Ordinal);
        Assert.DoesNotContain("IMAGE_CAPTURE", all, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWordsComeFromTheModelsNotTheHead()
    {
        var area = Read("Constitution.Android/MealsArea.cs");
        var written = All() + area;

        Assert.DoesNotContain("\"Type it in\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Check the card\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Let go\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Add a recipe\"", written, StringComparison.Ordinal);
        Assert.Contains("RecipePickerModel.For(", area, StringComparison.Ordinal);
        Assert.Contains("TypedEntry.CheckAction", area, StringComparison.Ordinal);
        Assert.Contains("TypedEntry.LetGoAction", area, StringComparison.Ordinal);
        Assert.Contains("RecipePickerText.TypeInTitle", area, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePickerGrowsFromThePinnedBarWithNoDialog()
    {
        var area = Read("Constitution.Android/MealsArea.cs");
        var sheet = Read("Constitution.Android/MealsTab/PickerSheet.cs");

        Assert.Contains("new PickerSheet(", area, StringComparison.Ordinal);
        Assert.Contains("PaperUi.BottomBar", area, StringComparison.Ordinal);
        Assert.Contains("AddView(bar)", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void LettingGoOfTypingIsUndoFirstThroughTheSharedFold()
    {
        var area = Read("Constitution.Android/MealsArea.cs");

        Assert.Contains("typedUndo.Begin(board", area, StringComparison.Ordinal);
        Assert.Contains("typedUndo.Reverse(actKey)", area, StringComparison.Ordinal);
        Assert.Contains("typedUndo.Expire(now)", area, StringComparison.Ordinal);
    }

    [Fact]
    public void TypedWordsReachTheKeeperOnlyThroughTheConfirmModel()
    {
        var area = Read("Constitution.Android/MealsArea.cs");

        Assert.Contains("ScanConfirmModel.From(payload, null, ScanOrigin.Typed)", area, StringComparison.Ordinal);
        Assert.DoesNotContain("keeper.Keep(", area, StringComparison.Ordinal);
    }
}
