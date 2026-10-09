using System.Text.RegularExpressions;
using Xunit;

namespace Pulp.Tests.Guard;

/// <summary>
/// The icon is wired, not just generated (D200, D267). Folio and Plate each shipped a whole rebrand
/// wearing an old icon because the art existed and nothing named it; an unnamed mipmap is not an
/// error on Android, the platform default simply stands. CI's ICON CHECK reads the built APK; this
/// is the same question asked of the source, so it fails before a build is spent on it.
/// </summary>
public class LauncherIconTests
{
    private static readonly Regex ApplicationIcon = new(
        @"\[Application\([^\]]*Icon\s*=\s*""@mipmap/appicon""[^\]]*RoundIcon\s*=\s*""@mipmap/appicon_round""",
        RegexOptions.Compiled);

    [Fact]
    public void TheHeadNamesTheGeneratedIcon()
    {
        var sources = PulpSource.Files(".cs")
            .Where(file => file.Contains(Path.DirectorySeparatorChar + "Pulp.Android" + Path.DirectorySeparatorChar))
            .ToList();
        Assert.Contains(sources, file => ApplicationIcon.IsMatch(File.ReadAllText(file)));
    }

    [Theory]
    [InlineData("mipmap-anydpi-v26/appicon.xml")]
    [InlineData("mipmap-anydpi-v26/appicon_round.xml")]
    [InlineData("mipmap-xxxhdpi/appicon_foreground.png")]
    [InlineData("values/colors.xml")]
    public void TheGeneratedIconIsInTheHead(string resource) =>
        Assert.True(
            File.Exists(Path.Combine(PulpSource.Root(), "Pulp.Android", "Resources", resource)),
            "Missing " + resource + ": run `dotnet run --project Tools/IconStudio -- --repo-root .`");
}
