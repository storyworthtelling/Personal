using IconStudio;

namespace IconStudio.Tests;

public class GlyphDrawablesTests
{
    [Fact]
    public void EveryCommittedGlyphDrawableIsWhatIconStudioWrites()
    {
        foreach (var (target, glyph) in GlyphDrawables.Targets)
        {
            var committed = Path.Combine(RepoRoot(), target.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(committed), $"{target} is missing; run IconStudio --repo-root .");
            Assert.Equal(GlyphDrawables.Render(glyph), File.ReadAllText(committed));
        }
    }

    [Fact]
    public void AGlyphDrawableIsAStrokeWithNoFill()
    {
        var xml = GlyphDrawables.Render("Mailbox");
        Assert.Contains("android:strokeWidth=\"2\"", xml, StringComparison.Ordinal);
        Assert.Contains("android:fillColor=\"#00000000\"", xml, StringComparison.Ordinal);
        Assert.Contains("android:strokeLineCap=\"round\"", xml, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
