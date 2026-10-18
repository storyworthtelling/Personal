using System.Text.RegularExpressions;
using Constitution.Tests.Body;

namespace Constitution.Tests.BodyScreens;

/// <summary>
/// The head's Body screens are compile-checked, not run here, so what can be said about them is said by reading
/// their source: that the honest-chart guard reaches every file of the tab, that nothing a log line holds is a
/// reading, that the permission request asks only to read, and that the shell and Diagnostics are wired to it.
/// </summary>
public class BodyScreensSourceTests
{
    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(ConstitutionSource.Root(), relative.Replace('/', Path.DirectorySeparatorChar)));

    private static IReadOnlyList<string> TabFiles() =>
        ConstitutionSource.Files(".cs")
            .Select(ConstitutionSource.Relative)
            .Where(path => path == "Constitution.Android/BodyArea.cs" || path.StartsWith("Constitution.Android/BodyTab/", StringComparison.Ordinal))
            .ToList();

    // ---- the honest-chart guard reaches the whole tab ----

    [Fact]
    public void EveryFileOfTheTabIsUnderTheHonestChartGuard()
    {
        var files = TabFiles();

        Assert.True(files.Count >= 8, $"The guard found only {files.Count} tab files; it is not reading the real folder.");
        var strays = files.Where(path => !HonestChartTests.IsBodyFile(path)).ToList();
        Assert.True(strays.Count == 0,
            "Tab files the honest-chart guard does not read (name them Body* or Signal*): " + string.Join(", ", strays));
    }

    // ---- no reading in a log line ----

    private static readonly Regex TrailCall = new(
        @"\.Outcome\(\s*\$""(?<text>[^""]*)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly Regex Interpolation = new(@"\{(?<expr>[^}]*)\}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>What a trail line may interpolate: the type name of a failure, or a seat's frozen id. Never a value.</summary>
    internal static IEnumerable<string> LoosePieces(string source)
    {
        foreach (Match call in TrailCall.Matches(source))
        {
            foreach (Match piece in Interpolation.Matches(call.Groups["text"].Value))
            {
                var expression = piece.Groups["expr"].Value;
                if (!expression.Contains("GetType().Name", StringComparison.Ordinal)
                    && !expression.Contains("VoicedOccasions.For(request.Seat).Id", StringComparison.Ordinal))
                {
                    yield return expression;
                }
            }
        }
    }

    [Fact]
    public void NoTrailLineOfTheTabHoldsAReading()
    {
        var files = TabFiles();
        var calls = files.Sum(path => TrailCall.Matches(Read(path)).Count);
        Assert.True(calls >= 3, $"The guard found only {calls} trail lines on the tab; it is not reading them.");

        var loose = files.SelectMany(path => LoosePieces(Read(path)).Select(piece => path + ": {" + piece + "}")).ToList();
        Assert.True(loose.Count == 0, "A trail line on the tab holds something other than a failure's type or a seat id:\n" + string.Join("\n", loose));
    }

    [Fact]
    public void TheTrailDetectorCatchesAPlantedReading()
    {
        var planted = "log.Outcome($\"read weight {reading.Value}\");";

        Assert.Equal(["reading.Value"], LoosePieces(planted).ToList());
        Assert.Empty(LoosePieces("log.Outcome($\"read failed: {e.GetType().Name}\");"));
    }

    // ---- the permission request only reads ----

    [Fact]
    public void TheRequesterAsksForTheSignalsReadPermissionsAndNothingElse()
    {
        var source = Read("Constitution.Android/Health/ActivityHealthPermissionRequester.cs");

        Assert.Contains("HealthSignalPermissions.For(signal)", source, StringComparison.Ordinal);
        Assert.Contains("activity.RequestPermissions(permissions, RequestCode)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WRITE", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRequestCodeIsNotTheAccountPickers()
    {
        var code = Regex.Match(Read("Constitution.Android/Health/ActivityHealthPermissionRequester.cs"), @"RequestCode\s*=\s*(\d+);");

        Assert.True(code.Success);
        Assert.NotEqual(9201, int.Parse(code.Groups[1].Value));
    }

    // ---- the shell and Diagnostics ----

    [Fact]
    public void TheShellForwardsThePermissionAnswerAndHostsTheSignalPage()
    {
        var shell = Read("Constitution.Android/ConstitutionActivity.cs");

        Assert.Contains("override void OnRequestPermissionsResult", shell, StringComparison.Ordinal);
        Assert.Contains("bodyArea?.OnRequestPermissionsResult", shell, StringComparison.Ordinal);
        Assert.Contains("RenderSignal(", shell, StringComparison.Ordinal);
        Assert.Contains("BackToPage(this, CloseSignal)", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePermissionBarIsPinnedAndCarriesTheOneAction()
    {
        var page = Read("Constitution.Android/BodyTab/SignalPageView.cs");

        Assert.Contains("PaperUi.BottomBar(", page, StringComparison.Ordinal);
        Assert.Contains("model.Ask.BarLabel", page, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsReadsTheBodySyncOutcome()
    {
        var source = Read("Constitution.Android/Settings/DiagnosticsExport.cs");

        Assert.Contains("Status.LastOutcome()", source, StringComparison.Ordinal);
        Assert.Contains("DiagnosticsComponent.BodySync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BodyDoesNotRaiseAnyNoticeBeyondTheTwoItOwns()
    {
        // The two Problem notices Body raises are written once, in the domain; the head never raises one by hand.
        foreach (var path in TabFiles())
        {
            Assert.DoesNotContain(".Raise(", Read(path), StringComparison.Ordinal);
        }
    }
}
