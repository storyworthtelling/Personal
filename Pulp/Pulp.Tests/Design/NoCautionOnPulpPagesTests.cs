using System.Text.RegularExpressions;
using Xunit;

namespace Pulp.Tests.Design;

/// <summary>
/// Pulp's amber identity (70 degrees) sits 14.3 degrees from the Caution role's ochre: a recorded
/// departure from DESIGN.md §3's 20-degree floor (D275), the second after Index's against Positive.
/// What makes it safe is that nothing on a Pulp page is ever drawn in Caution, so an amber tab or
/// accent can never be read as "consider this". This guard is that rule. The teen rating chip,
/// which the gate drew in Caution, takes the Reference slate instead (D276).
/// </summary>
public class NoCautionOnPulpPagesTests
{
    // Every way the suite names the Caution role: the native tokens, the generated palette, and the
    // role enum the dog-ear and the Paper kit resolve through.
    private static readonly Regex CautionReference = new(
        @"\b(DesignTokens\.Caution\w*|PaperColorTokens\.Caution\w*|ColorRole\.Caution|DogEarHue\.Caution)\b",
        RegexOptions.Compiled);

    [Fact]
    public void NothingInPulpDrawsTheCautionRole()
    {
        var files = PulpSource.Files(".cs")
            .Where(file => !file.EndsWith(nameof(NoCautionOnPulpPagesTests) + ".cs", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(files);

        var offenders = files
            .Where(file => CautionReference.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(PulpSource.Root(), file))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Pulp draws no Caution (D275): its amber identity is a recorded departure 14.3 degrees from "
            + "the caution ochre, safe only while no Pulp page uses that role. Found in: "
            + string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("var hue = DesignTokens.CautionMark;")]
    [InlineData("chip.Role = ColorRole.Caution;")]
    [InlineData("paint.Color = PaperColorTokens.CautionFill;")]
    public void TheDetectorRecognisesEveryWayOfNamingCaution(string line) =>
        Assert.Matches(CautionReference, line);

    [Theory]
    [InlineData("var hue = DesignTokens.ReferenceMark;")]
    [InlineData("// a cautionary note in prose")]
    public void TheDetectorLeavesOtherRolesAlone(string line) =>
        Assert.DoesNotMatch(CautionReference, line);
}
