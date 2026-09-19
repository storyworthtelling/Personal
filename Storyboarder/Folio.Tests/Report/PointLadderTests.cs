using Folio.Domain.Markup;
using System.Reflection;
using System.Text.RegularExpressions;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Design;
using Storyworthtelling.Paper;

namespace Folio.Tests.Report;

/// <summary>
/// G13 — every printed size comes from <see cref="PaperTypeScale"/>.
///
/// <para>The bug this reintroduces is hardcoding a point size. It is the cheapest possible change to
/// make and the hardest to see: one page set a shade larger than its neighbours does not fail
/// anything, it just quietly means the report has two type scales and only one of them is
/// registered. That is how the previous eight got there.</para>
/// </summary>
public class PointLadderTests
{
    private static readonly float[] Ladder = typeof(PaperTypeScale)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(float) && field.Name != nameof(PaperTypeScale.Ratio))
        .Select(field => (float)field.GetRawConstantValue()!)
        .ToArray();

    public static TheoryData<string> Roles()
    {
        var data = new TheoryData<string>();
        foreach (var method in typeof(ReportType).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            data.Add(method.Name);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void EveryReportTypeRoleSitsOnARungOfTheRegisteredLadder(string role)
    {
        var method = typeof(ReportType).GetMethod(role, BindingFlags.Public | BindingFlags.Static)!;
        using var font = (SkiaSharp.SKFont)method.Invoke(null, null)!;

        Assert.Contains(font.Size, Ladder);
    }

    /// <summary>The declared roles; one more that appears is a decision, not a diff.</summary>
    [Fact]
    public void TheReportDeclaresExactlyTheRolesItPrints()
    {
        var roles = typeof(ReportType).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            new[]
            {
                "Body", "BodyBold", "BodyItalic", "Folio", "Heading", "Label", "Manuscript",
                "Running", "Section", "Small", "Subtitle", "Title",
            },
            roles);
    }

    /// <summary>
    /// The mechanism: no file in the project may build a font at a number. Sizes reach a canvas
    /// through <see cref="ReportType"/>, which reaches them through the ladder.
    /// </summary>
    [Fact]
    public void NoSourceFileBuildsAFontAtALiteralSize()
    {
        var offenders = new List<string>();
        foreach (var file in DesignTestHelpers.SourceFiles())
        {
            if (file.EndsWith(Path.Combine("Report", "PointLadderTests.cs"), StringComparison.Ordinal))
            {
                continue;   // this guard writes the pattern out on purpose
            }
            var source = StripComments(File.ReadAllText(file));
            foreach (Match match in Regex.Matches(source, @"new\s+SKFont\s*\(([^)]*)\)"))
            {
                var arguments = match.Groups[1].Value;
                if (Regex.IsMatch(arguments, @",\s*\d+(\.\d+)?f?\s*$"))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {match.Value.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "A font built at a literal size is a second, unregistered type scale:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// And the pixel ladder it replaced is gone rather than merely unused — an unread constant is an
    /// invitation, and these read as points while being 150-DPI pixels.
    /// </summary>
    [Fact]
    public void ThePixelLadderIsGone()
    {
        var offenders = DesignTestHelpers.SourceFiles()
            .Where(file => !file.EndsWith(Path.Combine("Report", "PointLadderTests.cs"), StringComparison.Ordinal))
            .Where(file => Regex.IsMatch(StripComments(File.ReadAllText(file)), @"DesignTokens\.Pdf\b"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "DesignTokens.Pdf was the 150-DPI pixel geometry; the page is in points now:\n"
            + string.Join("\n", offenders!));
    }

    private static string StripComments(string source) =>
        Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline),
            @"//.*?$", string.Empty, RegexOptions.Multiline);

    /// <summary>
    /// The manuscript's line height decides how long the marked-up document is — about 140 pages
    /// (F45) — and it is set against a size Domain cannot see, because Domain holds no reference to
    /// the library the ladder lives in. So the two are held together here instead: if the ladder's
    /// Body rung moves and MarkupGeometry does not, the page count silently becomes a different
    /// number from the one the plan was measured on.
    /// </summary>
    [Fact]
    public void TheManuscriptIsSetOnTheLaddersBodyRung()
    {
        using var manuscript = ReportType.Manuscript();

        Assert.Equal(PaperTypeScale.Body, manuscript.Size);
        Assert.Equal(PaperTypeScale.Body, (float)MarkupGeometry.TypeSize);
    }
}
