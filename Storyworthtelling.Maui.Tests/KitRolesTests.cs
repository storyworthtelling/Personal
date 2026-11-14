using System.Text.RegularExpressions;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// The kit's styles wear the role DESIGN.md gives them (audit W8.1, design-fidelity §1–5, §10).
///
/// <para>Every one of these was a style pointing at the right-looking key for the wrong role: the
/// error label, the "Needs you" group, the error chip and the warning card all took <c>Accent</c>,
/// which is Action pine, so Folio's and Index's setup errors and Foreword's "Needs you" showed in
/// the colour of success. The <c>Accent</c> keys are retired so a style has to name its role.</para>
/// </summary>
public class KitRolesTests
{
    [Fact]
    public void TheAccentKeysAreRetired()
    {
        var dictionary = Dictionary();

        Assert.DoesNotMatch(@"x:Key=""Accent(Deep)?""", dictionary);
        Assert.DoesNotMatch(@"StaticResource Accent(Deep)?\}", dictionary);
    }

    [Theory]
    [InlineData("ErrorLabel", "TextColor", "ProblemText")]
    [InlineData("ChipLabelError", "TextColor", "ProblemDeep")]
    [InlineData("GroupLabelProblem", "TextColor", "ProblemText")]
    [InlineData("ErrorChip", "Stroke", "ProblemMark")]
    [InlineData("StatusLine", "TextColor", "ProgressText")]
    public void AStyleWearsItsRole(string style, string property, string token)
    {
        Assert.Equal(token, Setter(Style(style), property));
    }

    /// <summary>
    /// §3: a filled primary is the deep tier; §8: pressing changes no colour, it shows an inset
    /// hairline instead.
    /// </summary>
    [Fact]
    public void ThePrimaryButtonIsTheDeepTierAndPressingChangesNoColour()
    {
        var primary = ImplicitButton();

        Assert.Equal("ActionDeep", Setter(primary, "BackgroundColor"));

        var pressed = Regex.Match(primary, @"<VisualState x:Name=""Pressed"">(?<body>.*?)</VisualState>",
            RegexOptions.Singleline);
        Assert.True(pressed.Success, "The primary button has no Pressed state, so it inherits the platform's.");
        Assert.DoesNotContain("BackgroundColor", pressed.Groups["body"].Value);
        Assert.Equal("Hairline", Setter(pressed.Groups["body"].Value, "BorderColor"));
    }

    /// <summary>§7: "radius 12, padding 18×11" — the owner's ruling over the kits' old 16×12.</summary>
    [Fact]
    public void EveryButtonIsPaddedEighteenByEleven()
    {
        var buttons = Regex.Matches(Dictionary(), @"<Style (?:x:Key=""\w+"" )?TargetType=""Button"".*?</Style>",
            RegexOptions.Singleline);

        Assert.NotEmpty(buttons);
        Assert.All(buttons, button => Assert.Equal("18,11", Setter(button.Value, "Padding")));
    }

    /// <summary>§6: "The spinner is retired" — the kit must not dress one up for a head to use.</summary>
    [Fact]
    public void TheKitShipsNoSpinner()
    {
        Assert.DoesNotContain("ActivityIndicator", Dictionary());
    }

    /// <summary>No MAUI head draws a spinner either: unknown duration is a status sentence.</summary>
    [Fact]
    public void NoMauiHeadDrawsASpinner()
    {
        var offenders = HeadFiles()
            .Where(file => File.ReadAllText(file).Contains("ActivityIndicator", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0, "Spinners: " + string.Join(", ", offenders));
    }

    /// <summary>§7's warning card is a 3dp Action rule down the left, not a 1dp ring all round.</summary>
    [Fact]
    public void TheWarningCardIsALeftRule()
    {
        var card = File.ReadAllText(KitFile("Views/PaperWarningCard.cs"));

        Assert.Contains("DesignTokens.Shape.WarningRuleDp", card);
        Assert.Contains("\"ActionMark\"", card);
        Assert.DoesNotContain("x:Key=\"WarningCard\"", Dictionary());

        var stillStyled = HeadFiles()
            .Where(file => File.ReadAllText(file).Contains("StaticResource WarningCard", StringComparison.Ordinal))
            .ToList();
        Assert.True(stillStyled.Count == 0, "Still on the old WarningCard style: " + string.Join(", ", stillStyled));
    }

    /// <summary>The header rule and the binder glyph default to the Ink token, never pure black.</summary>
    [Theory]
    [InlineData("Views/PaperPageHeader.cs")]
    [InlineData("Views/BinderTabs.cs")]
    public void DrawnInkDefaultsToTheInkToken(string file)
    {
        Assert.DoesNotContain("Colors.Black", File.ReadAllText(KitFile(file)));
    }

    // ---- reading ----

    private static string ImplicitButton()
    {
        var match = Regex.Match(Dictionary(), @"<Style TargetType=""Button"">.*?</Style>", RegexOptions.Singleline);
        Assert.True(match.Success, "The kit has no implicit Button style.");
        return match.Value;
    }

    private static string Style(string key)
    {
        var match = Regex.Match(Dictionary(), $@"<Style x:Key=""{key}"".*?</Style>", RegexOptions.Singleline);
        Assert.True(match.Success, $"The kit has no {key} style.");
        return match.Value;
    }

    private static string? Setter(string body, string property)
    {
        var match = Regex.Match(body, $@"<Setter Property=""{property}"" Value=""(?:\{{StaticResource (?<key>\w+)\}}|(?<literal>[^""]+))""");
        if (!match.Success)
        {
            return null;
        }

        return match.Groups["key"].Success ? match.Groups["key"].Value : match.Groups["literal"].Value;
    }

    private static IEnumerable<string> HeadFiles() =>
        new[] { "Ledger/Ledger.Android", "Storyboarder/Folio.Android", "Foreword/Foreword.Android", "Storyworthtelling.Maui" }
            .SelectMany(folder => Directory.EnumerateFiles(RepoFile(folder), "*.*", SearchOption.AllDirectories))
            .Where(file => file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string Dictionary() => File.ReadAllText(KitFile("Resources/Styles/Paper.xaml"));

    private static string KitFile(string relative) =>
        Path.Combine(RepoRoot(), "Storyworthtelling.Maui", relative.Replace('/', Path.DirectorySeparatorChar));

    private static string RepoFile(string relative) =>
        Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
