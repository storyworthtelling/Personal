using Xunit;
using Codex.Domain.Policy;

namespace Codex.Tests.Guard;

/// <summary>
/// Post and Plate open a link by naming Flyleaf's RestrictedBrowserActivity outright, so the class name they
/// name must be the one the manifest registers. Without a declared <c>Name</c> .NET for Android registers the
/// activity under a generated <c>crc64…</c> package, and every explicit hand-off failed to find it (audit W2, H2):
/// the link fell back to whatever the system's own default was, or to nothing.
/// </summary>
public class RestrictedBrowserHandoffTests
{
    [Fact]
    public void TheHandoffNamesTheFullJavaClassInFlyleafsPackage()
    {
        Assert.Equal(PolicyContext.LauncherPackage, RestrictedBrowserHandoff.Package);
        Assert.Equal(PolicyContext.LauncherPackage + ".RestrictedBrowserActivity", RestrictedBrowserHandoff.Activity);
    }

    [Fact]
    public void TheActivityIsRegisteredUnderThatName()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "FocusPhone", "Flyleaf.Android", "RestrictedBrowserActivity.cs"));

        Assert.Contains("Name = RestrictedBrowserHandoff.Activity,", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Post.Android", "LinkOpener.cs")]
    [InlineData("Plate.Android", "ScanArea.cs")]
    public void EverySenderNamesItByTheHandoffConstants(string project, string file)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "FocusPhone", project, file));

        Assert.Contains("new ComponentName(RestrictedBrowserHandoff.Package, RestrictedBrowserHandoff.Activity)", source, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}
