using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// The settings standard (audit W8.4, report 02 §7–8): a working credential is "Working ✓" with a quiet
/// Replace in every app; there is no Save — a field commits as it changes or on blur; and every Settings
/// screen ends with the one build line.
/// </summary>
public partial class SettingsStandardTests
{
    // Each app's Settings screen, where the build line and the no-Save rule live.
    private static readonly string[] Screens =
    [
        "FocusPhone/Flyleaf.Android/LauncherSettingsActivity.cs",
        "FocusPhone/Score.Android/SettingsArea.cs",
        "FocusPhone/Post.Android/SettingsArea.cs",
        "FocusPhone/Plate.Android/SettingsArea.cs",
        "Pulp/Pulp.Android/Shell/SettingsArea.cs",
        "Constitution/Constitution.Android/SettingsArea.cs",
        "Ledger/Ledger.Android/Pages/SetupPage.xaml.cs",
        "Storyboarder/Folio.Android/Pages/SetupPage.xaml.cs",
        "Foreword/Foreword.Android/Pages/SetupPage.xaml.cs",
    ];

    [Fact]
    public void EverySettingsScreenEndsWithTheOneBuildLine()
    {
        var missing = Screens
            .Where(screen => !BuildLine().IsMatch(File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), screen))))
            .ToList();
        Assert.True(missing.Count == 0, "No BuildLine.Format / PaperUi.BuildNumber: " + string.Join(", ", missing));
    }

    [Fact]
    public void NoSettingsScreenHasASaveButton()
    {
        var found = new List<string>();
        foreach (var screen in Screens)
        {
            foreach (var file in new[] { screen, screen.Replace(".xaml.cs", ".xaml", StringComparison.Ordinal) }.Distinct())
            {
                var path = Path.Combine(GuardScan.RepoRoot(), file);
                if (File.Exists(path) && SaveButton().IsMatch(File.ReadAllText(path)))
                {
                    found.Add(file);
                }
            }
        }

        Assert.True(found.Count == 0, "Commit on change or blur (§5), not a Save button: " + string.Join(", ", found));
    }

    [Fact]
    public void EveryReplaceIsQuiet()
    {
        var found = Ui()
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(hit => LoudReplace().IsMatch(hit.line))
            .Select(hit => $"{Path.GetRelativePath(GuardScan.RepoRoot(), hit.file)}:{hit.index + 1}")
            .ToList();
        Assert.True(found.Count == 0, "Replace is a quiet button beside \"Working ✓\":\n" + string.Join("\n", found));
    }

    [Fact]
    public void AWorkingKeySaysWorkingWithItsTick()
    {
        var groups = File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), "Pulp/Pulp.Domain/Settings/SettingsGroups.cs"));
        Assert.DoesNotContain("=> \"Working\",", groups, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"BuildLine\.Format\(|PaperUi\.BuildNumber\(")]
    private static partial Regex BuildLine();

    [GeneratedRegex(@"(?:PrimaryButton|SecondaryButton)\([^,]+,\s*""Save""|Text=""Save(?: settings)?""")]
    private static partial Regex SaveButton();

    [GeneratedRegex(@"(?:PrimaryButton|SecondaryButton)\([^,]+,\s*""Replace""")]
    private static partial Regex LoudReplace();

    private static IEnumerable<string> Ui() =>
        new[] { "FocusPhone", "Pulp", "Constitution", "Ledger", "Storyboarder", "Foreword" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(GuardScan.RepoRoot(), folder), "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" || part.EndsWith(".Tests", StringComparison.Ordinal)));
}
