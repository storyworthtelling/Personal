using System.Text.RegularExpressions;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// The two files this kit copies from <c>Codex.Domain</c> are still identical to their originals.
///
/// <para>They are copies rather than references because <c>Codex.Domain</c> is FocusPhone's and is
/// deliberately dependency-free — the same reason <c>RelativeTime</c> and <c>TimeFormats</c> exist
/// twice in this repository, and kept honest the same way. A copy that drifts is worse than no
/// copy: two apps lay their tabs out differently and neither is wrong on its own terms, so nobody
/// finds it until they are held side by side.</para>
///
/// <para>Compared as <b>source</b>, deliberately. A test asserting a handful of remembered values
/// would pass while the arithmetic between them changed, which is the failure this whole release
/// exists to stop repeating.</para>
/// </summary>
public class NativeParityTests
{
    [Theory]
    [InlineData("FocusPhone/Codex.Domain/Design/BinderTabLayout.cs", "Storyworthtelling.Maui/Layout/BinderTabLayout.cs")]
    public void TheCopiedFileIsTheOriginal(string native, string kit)
    {
        Assert.Equal(Body(RepoFile(native)), Body(RepoFile(kit)));
    }

    /// <summary>
    /// The dp scales, compared member by member rather than as whole files: the kit's copy carries
    /// only Space and Shape, and explains in its own words why type sizes are not with them.
    /// </summary>
    [Fact]
    public void TheDpScalesAreTheNativeOnes()
    {
        var native = Constants(RepoFile("FocusPhone/Codex.Domain/Design/DesignTokens.cs"));
        var kit = Constants(RepoFile("Storyworthtelling.Maui/DesignTokens.cs"));

        Assert.NotEmpty(kit);

        var wrong = new List<string>();
        foreach (var (name, value) in kit)
        {
            if (!native.TryGetValue(name, out var expected))
            {
                wrong.Add($"{name}: the kit invented it — there is no native constant of that name");
            }
            else if (expected != value)
            {
                wrong.Add($"{name} is {value} in the kit and {expected} natively");
            }
        }

        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>
    /// Everything but the namespace line and the using it needs, so the comparison is of the code
    /// rather than of where it lives.
    /// </summary>
    private static string Body(string path) =>
        string.Join("\n", File.ReadAllLines(path)
            .Where(line => !line.StartsWith("namespace ", StringComparison.Ordinal)
                && !line.StartsWith("using ", StringComparison.Ordinal))
            .Select(line => line.TrimEnd())
            .SkipWhile(string.IsNullOrEmpty));

    private static Dictionary<string, string> Constants(string path) =>
        Regex.Matches(File.ReadAllText(path), @"public const int (?<name>\w+) = (?<value>[^;]+);")
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value);

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
