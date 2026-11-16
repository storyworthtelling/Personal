using System.Text.RegularExpressions;

namespace RepoGuards.Tests;

/// <summary>
/// DESIGN.md §4–§5 and §14 across every head and both kits (audit W8.5, report 04 §6–19). Before this, only
/// Pulp checked type sizes, only the MAUI kit's own dictionary checked letterspacing, and nothing checked
/// radii, capitals or black and white paint, so Folio carried about 25 invented sizes, Flyleaf's cover a
/// 44-pixel title, and Index a black scrim.
///
/// <list type="bullet">
/// <item>A type size comes from the ladder (<c>DesignTokens.TypeSp</c>, the kit's <c>*Size</c> resources),
/// never a number. A canvas painter may size text from its own geometry; its cap is a ladder rung.</item>
/// <item>Nothing is letterspaced.</item>
/// <item>Sentence case everywhere (owner's ruling, W8.5): no forced capitals, Index's mockup included.</item>
/// <item>Paper does not go dark: no <c>Colors.Black</c> or <c>Colors.White</c> paint; a haze is PaperDeep, a rule is Hairline.</item>
/// <item>A corner radius is a token, not a number. A drawn object keeps its own shape (<see cref="DrawnObjects"/>).</item>
/// </list>
/// </summary>
public partial class DesignLiteralsTests
{
    private static readonly string[] Heads =
    [
        "FocusPhone/Codex.Ui.Android", "FocusPhone/Flyleaf.Android", "FocusPhone/Score.Android", "FocusPhone/Post.Android",
        "FocusPhone/Plate.Android", "Pulp/Pulp.Android", "Constitution/Constitution.Android", "Ledger/Ledger.Android",
        "Storyboarder/Folio.Android", "Foreword/Foreword.Android", "Storyworthtelling.Maui",
    ];

    // The registered token file holds the ladder's numbers; that is where they belong.
    private static readonly string[] TokenFiles = ["Storyworthtelling.Maui/Resources/Styles/Paper.xaml"];

    /// <summary>Pictures of objects keep their own corners, as Plate's viewfinder and Pulp's posters do.</summary>
    private static readonly string[] DrawnObjects =
    [
        // A cloth book standing on Folio's shelf: its spine is cut square at the head and foot (owner's ruling, W8.5).
        "Storyboarder/Folio.Android/Pages/FlatBook.cs",
    ];

    /// <summary>The page shown when the design system itself failed to load: it cannot ask the kit for a size.</summary>
    private static readonly string[] DesignSystemUnavailable = ["Storyboarder/Folio.Android/Pages/StartupFailurePage.cs"];

    [Fact]
    public void NoTypeSizeIsANumber() => AssertNone(TypeSize(), "a type size from the ladder, not a number", DesignSystemUnavailable);

    [Fact]
    public void NothingIsLetterspaced() => AssertNone(Letterspacing(), "no letterspacing");

    [Fact]
    public void NothingIsForcedIntoCapitals() => AssertNone(Capitals(), "sentence case, not forced capitals");

    [Fact]
    public void PaperDoesNotGoDark() => AssertNone(BlackOrWhite(), "a paper role (PaperDeep haze, Hairline rule), not black or white");

    [Fact]
    public void ARadiusIsAToken() => AssertNone(Radius(), "a radius token, not a number", DrawnObjects);

    [Theory]
    [InlineData("label.FontSize = 12;")]
    [InlineData("<Label FontSize=\"11.5\" />")]
    [InlineData("view.SetTextSize(ComplexUnitType.Sp, 16);")]
    [InlineData("text.TextSize = Math.Min(h * 0.4f, 13 * density);")]
    public void TheTypeSizeDetectorCatchesANumber(string line) => Assert.Matches(TypeSize(), line);

    [Theory]
    [InlineData("view.SetTextSize(ComplexUnitType.Sp, DesignTokens.TypeSp.Body);")]
    [InlineData("<Label FontSize=\"{StaticResource LabelSize}\" />")]
    [InlineData("text.TextSize = Math.Min(h * 0.4f, DesignTokens.TypeSp.Label * density);")]
    [InlineData("letters.TextSize = Width * 0.34f;")]
    public void TheTypeSizeDetectorLetsATokenThrough(string line) => Assert.DoesNotMatch(TypeSize(), line);

    [Theory]
    [InlineData("StrokeShape = new RoundRectangle { CornerRadius = 3 },")]
    [InlineData("StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },")]
    [InlineData("<RoundRectangle CornerRadius=\"8\" />")]
    public void TheRadiusDetectorCatchesANumber(string line) => Assert.Matches(Radius(), line);

    // A size literal, or a Math.Min cap whose second argument starts with a number.
    [GeneratedRegex(@"\b(TextSize|FontSize)\s*=\s*""?[0-9]|SetTextSize\(\s*([\w.]+\s*,\s*)?[0-9]|TextSize\s*=\s*Math\.Min\([^;]*,\s*[0-9]")]
    private static partial Regex TypeSize();

    [GeneratedRegex(@"\b(CharacterSpacing|LetterSpacing)\s*=\s*""?-?[0-9.]*[1-9]")]
    private static partial Regex Letterspacing();

    [GeneratedRegex(@"\bToUpper(Invariant)?\(\)|SetAllCaps\(\s*true|TextTransform\s*=\s*""?(TextTransform\.)?Uppercase")]
    private static partial Regex Capitals();

    [GeneratedRegex(@"\bColors?\.(Black|White)\b")]
    private static partial Regex BlackOrWhite();

    [GeneratedRegex(@"\bCornerRadius\s*=\s*""?(new CornerRadius\()?[0-9]")]
    private static partial Regex Radius();

    private static void AssertNone(Regex pattern, string rule, params string[] allowed)
    {
        var found = Sources()
            .Where(file => !allowed.Contains(Relative(file), StringComparer.Ordinal))
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (file, line, index)))
            .Where(hit => !hit.line.TrimStart().StartsWith("//", StringComparison.Ordinal) && pattern.IsMatch(hit.line))
            .Select(hit => $"{Relative(hit.file)}:{hit.index + 1}")
            .ToList();
        Assert.True(found.Count == 0, $"Use {rule}:\n" + string.Join("\n", found));
    }

    private static IEnumerable<string> Sources() =>
        Heads.SelectMany(folder => Directory.EnumerateFiles(Path.Combine(GuardScan.RepoRoot(), folder), "*.*", SearchOption.AllDirectories))
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !TokenFiles.Contains(Relative(path), StringComparer.Ordinal));

    private static string Relative(string path) => Path.GetRelativePath(GuardScan.RepoRoot(), path).Replace('\\', '/');
}
