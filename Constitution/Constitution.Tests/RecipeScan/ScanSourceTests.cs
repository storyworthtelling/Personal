using System.Text.RegularExpressions;

namespace Constitution.Tests.RecipeScan;

/// <summary>
/// The scan's head files are compile-checked, not run, so what can be said about them is said by reading the source: the three
/// boxes touch no picture type, no toast or dialog, no literal colour or size, the actions live in the
/// pinned bar and every decision comes from the models. The photographs themselves are the camera files' (CameraSourceTests).
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
                // The reader's wiring hands the key to its one request header, so it is held by CameraSourceTests instead.
                return relative is "Constitution.Android/MealsTab/PickerTypedPage.cs" or "Constitution.Android/ContractPermissions.cs";
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

        Assert.Equal(2, names.Count);
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

        Assert.True(offenders.Count == 0, "A picture, stream, URI or extra in the boxes:\n" + string.Join("\n", offenders));
        Assert.Matches(Pictures, "intent.PutExtra(\"image\", bytes);");
    }

    [Fact]
    public void NoPassphraseTokenOrKeyIsHandled()
    {
        Assert.DoesNotMatch(new Regex("(?<![A-Za-z])(passphrase|token|apikey)(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)), All());
    }

    [Fact]
    public void NothingStartsPlatesOldRecipeRead()
    {
        var area = Read("Constitution.Android/MealsArea.cs");

        Assert.DoesNotContain("READ_RECIPE", All() + area, StringComparison.Ordinal);
        Assert.DoesNotContain("SetClassName(", area, StringComparison.Ordinal);
        Assert.DoesNotContain("GetStringExtra", area, StringComparison.Ordinal);
    }

    [Fact]
    public void TheActionsLiveInTheModelAndThePinnedBar()
    {
        var area = Read("Constitution.Android/MealsArea.cs");

        Assert.Contains("PaperUi.BottomBar", area, StringComparison.Ordinal);
        Assert.Contains("TypedEntry.KeepAction", area, StringComparison.Ordinal);
        Assert.Contains("TypedEntry.LetGoAction", area, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Keep the card\"", All() + area, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Let go\"", All() + area, StringComparison.Ordinal);
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
    public void LettingGoOfTheBoxesIsUndoFirst()
    {
        var area = Read("Constitution.Android/MealsArea.cs");

        Assert.Contains("TypedEntryUndo", area, StringComparison.Ordinal);
        Assert.Contains("ConnectionNoticeBoard.With", area, StringComparison.Ordinal);
        Assert.Contains("typedUndo.Reverse(", area, StringComparison.Ordinal);
        Assert.Contains("typed.Restore(words)", area, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBoxesAreDrawnFromTheEntry()
    {
        var page = Read("Constitution.Android/MealsTab/PickerTypedPage.cs");

        foreach (var member in new[] { "entry.PageHeading", "TypedEntry.ReadNote", "entry.FromPhotos", "TypedEntry.TitleLabel", "TypedEntry.IngredientsLabel", "TypedEntry.StepsLabel" })
        {
            Assert.Contains(member, page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheActivityHandsTheAnswerToTheMealsArea()
    {
        var activity = Read("Constitution.Android/ConstitutionActivity.cs");

        Assert.Contains("mealsArea?.OnActivityResult(requestCode, resultCode, data)", activity, StringComparison.Ordinal);
    }
}
