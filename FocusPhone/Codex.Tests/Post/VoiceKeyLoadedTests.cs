using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// The shared Claude key lives in the Drive vault, and <c>VoiceOptionsAccess.ForHead</c> returns
/// options that stay empty until <c>RefreshAsync</c> reads it. Post's mail sorter built its client
/// without that call, so it never had a key and its model half silently never ran. Every place
/// in Post that builds the options must also load them.
///
/// <para>Read from source because the Android head cannot be referenced from here.</para>
/// </summary>
public class VoiceKeyLoadedTests
{
    [Fact]
    public void EveryPostFileThatBuildsVoiceOptionsLoadsThem()
    {
        var head = Path.Combine(RepoRoot(), "FocusPhone", "Post.Android");
        var builders = Directory.GetFiles(head, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => File.ReadAllText(path).Contains("VoiceOptionsAccess.ForHead("))
            .ToList();

        Assert.Contains(builders, path => path.EndsWith("PostSync.cs"));
        Assert.All(builders, path => Assert.Contains(".RefreshAsync(", File.ReadAllText(path)));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
