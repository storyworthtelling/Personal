using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// The settings standard (audit W8.4, report 02 §7–8): a credential appears only when it needs work —
/// missing, or just refused — so a working one has no line and no Replace (owner's ruling, W8.4b); there is
/// no Save — a field commits as it changes or on blur; and every Settings screen ends with the one build line.
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

    /// <summary>
    /// Owner's rulings (audit W8.4b): folding groups in every app, drawn as Pulp's ruled rows, in the suite's order.
    /// A native screen draws its groups through the kit's accordion, which refuses any other order.
    /// </summary>
    [Fact]
    public void EveryNativeSettingsScreenFoldsItsGroupsThroughTheKit()
    {
        var native = Screens.Where(screen => !screen.EndsWith(".xaml.cs", StringComparison.Ordinal)).ToList();
        Assert.Equal(6, native.Count);
        var missing = native
            .Where(screen => !File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), screen)).Contains(".Draw(", StringComparison.Ordinal)
                || !File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), screen)).Contains("new SettingsAccordion(", StringComparison.Ordinal))
            .ToList();
        Assert.True(missing.Count == 0, "Draw the groups through Codex.Ui.SettingsAccordion: " + string.Join(", ", missing));
    }

    /// <summary>The MAUI heads draw the same groups through the kit's <c>SettingsGroup</c>, in the same order.</summary>
    [Fact]
    public void EveryMauiSettingsScreenFoldsItsGroupsInTheSuitesOrder()
    {
        var pages = Screens.Where(screen => screen.EndsWith(".xaml.cs", StringComparison.Ordinal))
            .Select(screen => screen[..^".cs".Length])
            .ToList();
        Assert.Equal(3, pages.Count);
        foreach (var page in pages)
        {
            var xaml = File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), page));
            var groups = MauiGroup().Matches(xaml).Select(match => match.Groups["title"].Value).ToList();
            Assert.True(Storyworthtelling.Updates.SettingsAnatomy.IsInOrder(groups),
                $"{page} runs {string.Join(", ", groups)}; the suite runs Connections, Vault, the app’s own, Diagnostics.");
            Assert.DoesNotContain("StaticResource GroupCard", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("StaticResource TitleLabel", xaml, StringComparison.Ordinal);
        }
    }

    [GeneratedRegex(@"<kit:SettingsGroup\b[^>]*\bTitle=""(?<title>[^""]+)""")]
    private static partial Regex MauiGroup();

    [Fact]
    public void AWorkingKeyDoesNotAppear()
    {
        var found = Ui()
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(hit => !hit.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && WorkingOrReplace().IsMatch(hit.line))
            .Select(hit => $"{Path.GetRelativePath(GuardScan.RepoRoot(), hit.file)}:{hit.index + 1}")
            .ToList();
        Assert.True(found.Count == 0, "A key that works isn’t on the screen: no \"Working ✓\" and no Replace:\n" + string.Join("\n", found));
    }

    [GeneratedRegex(@"BuildLine\.Format\(|PaperUi\.BuildNumber\(")]
    private static partial Regex BuildLine();

    [GeneratedRegex(@"(?:PrimaryButton|SecondaryButton)\([^,]+,\s*""Save""|Text=""Save(?: settings)?""")]
    private static partial Regex SaveButton();

    // The literal words, wherever a screen or its Domain model would put them.
    [GeneratedRegex(@"""Working ✓""|""Replace""|Text=""Working ✓""|Text=""Replace""")]
    private static partial Regex WorkingOrReplace();

    private static IEnumerable<string> Ui() =>
        new[] { "FocusPhone", "Pulp", "Constitution", "Ledger", "Storyboarder", "Foreword" }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(GuardScan.RepoRoot(), folder), "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" || part.EndsWith(".Tests", StringComparison.Ordinal)));
}
