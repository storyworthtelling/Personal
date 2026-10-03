using Xunit;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// Guard G15 (Suite S3, D146): every app's Settings screen shows the owner's maker's mark.
///
/// <para>Two different mechanisms deliver it, so this guard checks two different things. The MAUI
/// heads (Folio, Foreword, Index) each place an <c>&lt;Image Source="owner_medallion.png"&gt;</c>
/// directly on their own <c>SetupPage.xaml</c> — checked by reading that file's own text. The four
/// native heads (Post, Flyleaf, Score, Plate) instead all call the ONE shared
/// <c>Codex.Ui.Android/PaperUi.BuildNumber</c>, which draws the mark itself — so for those, this
/// checks both that the head's own Settings file still calls <c>PaperUi.BuildNumber</c>, AND that
/// <c>PaperUi.BuildNumber</c> itself still draws <c>owner_medallion</c>. Losing either half is the
/// bug this guard exists to catch: a head silently stops calling the shared helper, or the shared
/// helper stops drawing the mark and takes all four heads down with it at once.</para>
///
/// <para>Read by file text rather than by loading types, the same pattern
/// <c>IdentitySeparationTests</c> uses in this same file for a suite-wide check: none of these
/// projects can be referenced from one test assembly (different TFMs — <c>net10.0-android</c> MAUI
/// heads, native Android heads, and this desktop-TFM project all sit in different solutions).</para>
/// </summary>
public class MakersMarkEverywhereTests
{
    private static readonly (string App, string RelativePath)[] MauiSettingsScreens =
    [
        ("Folio", "Storyboarder/Folio.Android/Pages/SetupPage.xaml"),
        ("Foreword", "Foreword/Foreword.Android/Pages/SetupPage.xaml"),
        ("Index", "Ledger/Ledger.Android/Pages/SetupPage.xaml"),
    ];

    private static readonly (string App, string RelativePath)[] NativeSettingsScreens =
    [
        ("Post", "FocusPhone/Post.Android/SettingsArea.cs"),
        ("Flyleaf", "FocusPhone/Flyleaf.Android/LauncherSettingsActivity.cs"),
        ("Score", "FocusPhone/Score.Android/SettingsArea.cs"),
        ("Plate", "FocusPhone/Plate.Android/SettingsArea.cs"),
    ];

    private const string SharedPaperUiPath = "FocusPhone/Codex.Ui.Android/PaperUi.cs";

    [Theory]
    [MemberData(nameof(MauiApps))]
    public void EveryMauiHeadsSetupPagePlacesTheMedallionImage(string app, string relativePath)
    {
        var source = ReadRepoFile(relativePath);
        Assert.True(
            source.Contains("owner_medallion.png", StringComparison.Ordinal),
            $"{app}'s {relativePath} no longer shows the owner's medallion on its Settings screen.");
    }

    [Theory]
    [MemberData(nameof(NativeApps))]
    public void EveryNativeHeadsSettingsScreenCallsTheSharedBuildNumberHelper(string app, string relativePath)
    {
        var source = ReadRepoFile(relativePath);
        Assert.True(
            source.Contains("PaperUi.BuildNumber(", StringComparison.Ordinal),
            $"{app}'s {relativePath} no longer calls PaperUi.BuildNumber, so its Settings screen "
            + "has no way to show the owner's medallion.");
    }

    [Fact]
    public void TheSharedBuildNumberHelperStillDrawsTheMedallion()
    {
        var source = ReadRepoFile(SharedPaperUiPath);
        Assert.True(
            source.Contains("owner_medallion", StringComparison.Ordinal),
            "PaperUi.BuildNumber no longer resolves the owner's medallion — every native head "
            + "(Post, Flyleaf, Score, Plate) loses the mark from its Settings screen at once.");
    }

    public static IEnumerable<object[]> MauiApps() =>
        MauiSettingsScreens.Select(entry => new object[] { entry.App, entry.RelativePath });

    public static IEnumerable<object[]> NativeApps() =>
        NativeSettingsScreens.Select(entry => new object[] { entry.App, entry.RelativePath });

    // ---- helpers ----

    private static string ReadRepoFile(string relativePath)
    {
        var path = Path.Combine(RepoRoot(), relativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {relativePath}");
        return File.ReadAllText(path);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DESIGN.md")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
