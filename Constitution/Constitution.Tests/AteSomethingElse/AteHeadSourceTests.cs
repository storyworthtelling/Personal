using System.Text.RegularExpressions;

namespace Constitution.Tests.AteSomethingElse;

/// <summary>
/// The head's side of "I ate something else", read by content (the guards run with no Android workload): one camera route reused
/// through a seam, no toast and no second screen, no new permission, and nothing at class scope that could hold the picture.
/// </summary>
public partial class AteHeadSourceTests
{
    private const string FlowPath = "Constitution.Android/MealsTab/AteSomethingElseFlow.cs";

    private static string Flow() => File.ReadAllText(Path.Combine(ConstitutionSource.Root(), FlowPath));

    private static string Source(string relative) => File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative));

    [GeneratedRegex(@"\bToast\b|AlertDialog|\bSnackbar\b|Progress" + @"Bar|ProgressDialog|\bDialog\b|StartActivity\(|new Intent\(|new Android\.Content\.Intent\(")]
    private static partial Regex ASecondSurface();

    [GeneratedRegex(@"^\s*(?:private|public|internal|protected)?\s*(?:readonly\s+)?(?:byte\[\]|Stream|MemoryStream|Bitmap|Android\.Net\.Uri|Uri|Java\.IO\.File)\??\s+\w+\s*[;=]", RegexOptions.Multiline)]
    private static partial Regex AFieldThatCouldHoldAPicture();

    [Fact]
    public void TheFlowExistsAndRoutesTheCameraThroughTheSharedLauncher()
    {
        var flow = Flow();

        Assert.Contains("CameraCaptureLauncher", flow, StringComparison.Ordinal);
        Assert.Contains("AteSomethingElseModel", flow, StringComparison.Ordinal);
        Assert.Contains("NoBackupFilesDir", flow, StringComparison.Ordinal);
        Assert.DoesNotContain("MediaStore.ActionImageCapture", flow, StringComparison.Ordinal);
    }

    [Fact]
    public void NoToastNoDialogNoSecondScreenInTheFlow()
    {
        Assert.Empty(ASecondSurface().Matches(Flow()));
    }

    [Theory]
    [InlineData("Toast.MakeText(host, x, ToastLength.Short);")]
    [InlineData("new AlertDialog.Builder(host)")]
    [InlineData("host.StartActivity(intent);")]
    [InlineData("var bar = new Progress\u0042ar(host);")]
    public void TheSecondSurfacePatternFiresOnPlantedSamples(string planted)
    {
        Assert.NotEmpty(ASecondSurface().Matches(planted));
    }

    [Fact]
    public void TheFlowHoldsNoPictureAtClassScope()
    {
        Assert.Empty(AFieldThatCouldHoldAPicture().Matches(Flow()));
    }

    [Theory]
    [InlineData("    private byte[] photo;")]
    [InlineData("    private readonly Bitmap picture = null;")]
    [InlineData("    internal Android.Net.Uri address;")]
    [InlineData("    private Stream open = null;")]
    public void ThePictureFieldPatternFiresOnPlantedSamples(string planted)
    {
        Assert.NotEmpty(AFieldThatCouldHoldAPicture().Matches(planted));
    }

    [Fact]
    public void TheFlowNamesNoPermissionAndTheOneAskingPlaceIsMeals()
    {
        // D105 keeps the camera permission to one asking place. The flow is handed the check and the ask by the shell and names neither.
        var flow = Flow();
        Assert.DoesNotContain("RequestPermissions", flow, StringComparison.Ordinal);
        Assert.DoesNotContain("Manifest.Permission", flow, StringComparison.Ordinal);
        Assert.DoesNotContain("CheckSelfPermission", flow, StringComparison.Ordinal);

        var meals = Source("Constitution.Android/MealsArea.cs");
        var asks = Regex.Matches(meals, @"RequestPermissions\(\[([^\]]*)\]");
        Assert.Equal(["Android.Manifest.Permission.Camera"], asks.Cast<Match>().Select(ask => ask.Groups[1].Value.Trim()).ToArray());
    }

    [Fact]
    public void NoNewPermissionIsDeclaredForIt()
    {
        var declared = ConstitutionSource.Files(".cs")
            .Where(file => !ConstitutionSource.Relative(file).StartsWith("Constitution.Tests/", StringComparison.Ordinal))
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"\[assembly:\s*UsesPermission\(([^\)]*)\)\]").Select(match => match.Groups[1].Value.Trim()))
            .Order()
            .ToList();

        // Internet, the camera (D105), Plate's recipe read, the eight read-only Health Connect permissions, and the
        // notification permission for the brief's short list (C-9.1.3, D155): nothing more.
        Assert.Equal(12, declared.Count);
        Assert.Contains(declared, name => name.Contains("POST_NOTIFICATIONS", StringComparison.Ordinal));
        Assert.DoesNotContain(declared, name => name.Contains("WRITE", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(declared, name => name.Contains("STORAGE", StringComparison.OrdinalIgnoreCase) || name.Contains("MEDIA", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheBarsOfTodayAndMealsStartTheFlow()
    {
        Assert.Contains("AteSomethingElseFlow", Source("Constitution.Android/Today/TodayActionBar.cs"), StringComparison.Ordinal);
        Assert.Contains("AteSomethingElseFlow", Source("Constitution.Android/MealsArea.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("ActionIsLive", Source("Constitution.Android/Today/TodayActionBar.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheEstimateIsKeptUnderNoBackupAndNeverInTheHouseholdsFiles()
    {
        var flow = Flow();

        Assert.Contains("FileAteEstimates", flow, StringComparison.Ordinal);
        Assert.Contains("NoBackupFilesDir", flow, StringComparison.Ordinal);
        Assert.DoesNotContain("FilesDir.", flow.Replace("NoBackupFilesDir", string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCameraIsAimedAtPlateOnlyBeforeAnythingElse()
    {
        var launcher = Source("Constitution.Android/MealsTab/CameraCaptureLauncher.cs");

        Assert.Contains("SetPackage", launcher, StringComparison.Ordinal);
        Assert.Contains("PlatePackage", launcher + Flow(), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePhotoIsReadScaledAndReEncodedBeforeItGoesAnywhere()
    {
        var scaler = Source("Constitution.Android/MealsTab/AtePhotoScaler.cs");

        Assert.Contains("BitmapFactory", scaler, StringComparison.Ordinal);
        Assert.Contains("Compress", scaler, StringComparison.Ordinal);
    }

    // D107: the head draws the dinner card's one line and nothing under it, and no card for breakfast or lunch.
    [Fact]
    public void TheCardLineHasNoDetailLineAndNoOtherMealCards()
    {
        var line = Source("Constitution.Android/MealsTab/AteLineView.cs");
        var cards = Source("Constitution.Android/MealsTab/AteCards.cs");

        Assert.DoesNotContain("Detail", line, StringComparison.Ordinal);
        Assert.DoesNotContain("InkMuted", line, StringComparison.Ordinal);
        Assert.DoesNotContain("AteMeals.Title", cards, StringComparison.Ordinal);
        Assert.DoesNotContain("IndexLabel", cards, StringComparison.Ordinal);
    }

    [Fact]
    public void BothPagesSayTheStatusSentenceInline()
    {
        Assert.Contains("AteCards.Status", Source("Constitution.Android/Today/TodayPage.cs") + Source("Constitution.Android/TodayArea.cs"), StringComparison.Ordinal);
        Assert.Contains("AteCards.Status", Source("Constitution.Android/MealsTab/ThisWeekPage.cs") + Source("Constitution.Android/MealsArea.cs"), StringComparison.Ordinal);
    }
}
