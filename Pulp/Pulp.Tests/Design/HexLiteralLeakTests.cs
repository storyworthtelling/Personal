using System.Text.RegularExpressions;

namespace Pulp.Tests.Design;

/// <summary>
/// G5 — the DESIGN.md token-file registry (§16/§19) for Pulp: no colour hex literal and no
/// type-size literal in Pulp source. Colours come from <c>Codex.Domain.Design.DesignTokens</c>
/// through <c>PaperTheme</c>, sizes from <c>DesignTokens.TypeSp</c> through <c>PaperUi</c>.
/// Pulp registers no token file of its own, so the allowlist is empty; adding one is a decision.
/// </summary>
public class HexLiteralLeakTests
{
    private static readonly string[] AllowedFiles = [];

    private static readonly Regex HexColour = new("\"#[0-9A-Fa-f]{6,8}\"");

    private static readonly Regex TypeSize = new(
        @"\b(TextSize|FontSize)\s*=\s*[0-9]|SetTextSize\(\s*([\w.]+\s*,\s*)?[0-9]");

    [Fact]
    public void NoColourHexLiteralOutsideTokenFiles()
    {
        var offenders = Offenders(HexColour);
        Assert.True(offenders.Count == 0,
            "Colour hex literals in Pulp (use DesignTokens via PaperTheme):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void NoTypeSizeLiteralOutsideTokenFiles()
    {
        var offenders = Offenders(TypeSize);
        Assert.True(offenders.Count == 0,
            "Type-size literals in Pulp (use DesignTokens.TypeSp via PaperUi):\n" + string.Join("\n", offenders));
    }

    [Theory]
    [InlineData("view.TextSize = 14f;")]
    [InlineData("view.SetTextSize(ComplexUnitType.Sp, 16);")]
    [InlineData("label.FontSize = 12;")]
    [InlineData("view.SetTextSize(Android.Util.ComplexUnitType.Sp, 16);")]
    [InlineData("view.SetTextSize(18f);")]
    public void TheTypeSizeDetectorRecognisesALiteral(string line)
    {
        Assert.Matches(TypeSize, line);
    }

    [Fact]
    public void TheTypeSizeDetectorAcceptsAToken()
    {
        Assert.DoesNotMatch(TypeSize, "view.SetTextSize(ComplexUnitType.Sp, DesignTokens.TypeSp.Body);");
    }

    private static List<string> Offenders(Regex pattern)
    {
        var files = PulpSource.Files(".cs")
            .Where(file => !PulpSource.IsGuard(file, "HexLiteralLeakTests.cs"))
            .Where(file => !AllowedFiles.Contains(PulpSource.Relative(file), StringComparer.Ordinal))
            .ToList();
        Assert.True(files.Count > 0, "HexLiteralLeakTests scanned no files; the guard would pass on nothing.");

        return files
            .Where(file => pattern.IsMatch(File.ReadAllText(file)))
            .Select(PulpSource.Relative)
            .ToList();
    }
}
