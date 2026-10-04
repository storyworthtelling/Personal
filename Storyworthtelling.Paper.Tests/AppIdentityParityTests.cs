using System.Text.RegularExpressions;

namespace Storyworthtelling.Paper.Tests;

/// <summary>
/// The identity hexes here and the ones in <c>Tools/IconStudio/Tokens.cs</c> are the same values.
///
/// <para>There are two copies because the icons are painted by a console application and a printed
/// page is composed by a library, and a library cannot reference a console. Two copies of a colour
/// is exactly the shape of the bug the MAUI kit had — a palette written twice and enforced once, so
/// both halves could be wrong together, and were — so this test is the enforcement. IconStudio is
/// the source: it is where the hue was chosen and where the cover art is measured against it.</para>
///
/// <para>Read out of the file's <b>source</b> rather than by loading the console's assembly, because
/// the tools solution is not built by this one.</para>
/// </summary>
public class AppIdentityParityTests
{
    [Theory]
    [InlineData("ForewordFillHex", AppIdentityTokens.ForewordFillHex)]
    [InlineData("ForewordGroundHex", AppIdentityTokens.ForewordGroundHex)]
    [InlineData("ForewordSunriseHex", AppIdentityTokens.ForewordSunriseHex)]
    [InlineData("ForewordDeepHex", AppIdentityTokens.ForewordDeepHex)]
    [InlineData("FolioFillHex", AppIdentityTokens.FolioFillHex)]
    [InlineData("FolioGroundHex", AppIdentityTokens.FolioGroundHex)]
    [InlineData("FolioMarkHex", AppIdentityTokens.FolioMarkHex)]
    [InlineData("FolioTextHex", AppIdentityTokens.FolioTextHex)]
    [InlineData("FolioDeepHex", AppIdentityTokens.FolioDeepHex)]
    [InlineData("FolioClothHex", AppIdentityTokens.FolioClothHex)]
    [InlineData("PulpFillHex", AppIdentityTokens.PulpFillHex)]
    [InlineData("PulpGroundHex", AppIdentityTokens.PulpGroundHex)]
    [InlineData("PulpMarkHex", AppIdentityTokens.PulpMarkHex)]
    [InlineData("PulpTextHex", AppIdentityTokens.PulpTextHex)]
    [InlineData("PulpDeepHex", AppIdentityTokens.PulpDeepHex)]
    [InlineData("PulpClothHex", AppIdentityTokens.PulpClothHex)]
    public void TheValueMatchesIconStudio(string constant, string here)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Tools", "IconStudio", "Tokens.cs"));

        var match = Regex.Match(source, $@"{constant}\s*=\s*""(?<hex>#[0-9A-Fa-f]{{6,8}})""");

        Assert.True(match.Success, $"IconStudio no longer declares {constant} — this copy is orphaned.");
        Assert.Equal(match.Groups["hex"].Value, here);
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
