using System.Text.RegularExpressions;
using Folio.Infrastructure.Imaging;
using Folio.Tests.Design;
using SkiaSharp;
using Storyworthtelling.Paper;

namespace Folio.Tests.Report;

/// <summary>
/// G12 — printed colour resolves through the <b>Document</b> profile.
///
/// <para>The bug this reintroduces is routing a page through <c>PaperPalette.Resolve</c>, the older
/// device-adaptive resolver, whose <c>PaperRole.Active</c> still answers with the retired terracotta
/// <c>#C2542F</c>. That is not a hypothetical: it is what the shipped report did until this release,
/// and it survived because a retired colour is still a valid colour — every page rendered, every
/// test passed, and the only thing wrong was that the document was wearing a hue the bible had
/// replaced eighteen months earlier.</para>
/// </summary>
public class DocumentProfileTests
{
    private static readonly SKColor RetiredTerracotta =
        PaperPalette.Resolve(PaperRole.Active, DeviceColorProfile.Screen);

    [Fact]
    public void TheRuleIsTheDocumentActionHueAndNotTheRetiredTerracotta()
    {
        Assert.Equal(
            PaperPalette.ResolveRole(ColorRole.Action, ColorTier.Mark, UiColorProfile.DocumentColor),
            ReportPalette.Rule);
        Assert.NotEqual(RetiredTerracotta, ReportPalette.Rule);
    }

    [Theory]
    [InlineData(ColorRole.Problem)]
    [InlineData(ColorRole.Caution)]
    [InlineData(ColorRole.Reference)]
    [InlineData(ColorRole.Positive)]
    public void EverySeverityIsTheDocumentHue(ColorRole role)
    {
        var expected = PaperPalette.ResolveRole(role, ColorTier.Mark, UiColorProfile.DocumentColor);
        var severities = new[]
        {
            ReportPalette.MustFix, ReportPalette.Should, ReportPalette.Consider, ReportPalette.Great,
        };

        Assert.Contains(expected, severities);
    }

    [Fact]
    public void NoColourTheReportPrintsIsTheRetiredTerracotta()
    {
        var printed = new[]
        {
            ReportPalette.Ground, ReportPalette.Ink, ReportPalette.InkSoft, ReportPalette.InkMuted,
            ReportPalette.Hairline, ReportPalette.Rule, ReportPalette.MustFix, ReportPalette.Should,
            ReportPalette.Consider, ReportPalette.Great,
        };

        Assert.DoesNotContain(RetiredTerracotta, printed);
    }

    /// <summary>
    /// The mechanism, not the outcome: a page that reaches for the App profile or for the old
    /// role resolver is wrong even on the day its answer happens to look right.
    /// </summary>
    [Fact]
    public void NothingInTheProjectNamesTheOldRoleResolverOrTheAppProfile()
    {
        var offenders = new List<string>();
        foreach (var file in DesignTestHelpers.SourceFiles())
        {
            var source = StripComments(File.ReadAllText(file));
            if (file.EndsWith(Path.Combine("Report", "DocumentProfileTests.cs"), StringComparison.Ordinal))
            {
                continue;   // this guard names both on purpose
            }
            if (Regex.IsMatch(source, @"PaperRole\.Active\b")
                || Regex.IsMatch(source, @"UiColorProfile\.App\b")
                || Regex.IsMatch(source, @"DesignTokens\.Accent"))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        Assert.True(offenders.Count == 0,
            "These reach past the Document profile for a role colour:\n" + string.Join("\n", offenders));
    }

    private static string StripComments(string source) =>
        Regex.Replace(Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline),
            @"//.*?$", string.Empty, RegexOptions.Multiline);
}
