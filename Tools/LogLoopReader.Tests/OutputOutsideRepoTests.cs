namespace LogLoopReader.Tests;

// §6.1 layer 1: plaintext never enters the working tree. The reader refuses any output path inside the
// repository, however it is spelled, and its default lives under the temp folder.
public class OutputOutsideRepoTests
{
    [Fact]
    public void APathInsideTheRepoIsRefused()
    {
        var repo = Support.RepoRoot();

        Assert.Throws<OutputInsideRepoException>(() => OutputGuard.EnsureOutside(Path.Combine(repo, "log-loop"), repo));
        Assert.Throws<OutputInsideRepoException>(() => OutputGuard.EnsureOutside(repo, repo));
        Assert.Throws<OutputInsideRepoException>(() => OutputGuard.EnsureOutside(Path.Combine(repo, "Tools", "..", "x"), repo));
    }

    [Fact]
    public void ARelativePathResolvedIntoTheRepoIsRefused()
    {
        var repo = Support.RepoRoot();
        var previous = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = repo;
            Assert.Throws<OutputInsideRepoException>(() => OutputGuard.EnsureOutside("plain", repo));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    [Fact]
    public void ASiblingWhoseNameStartsLikeTheRepoIsNotInsideIt()
    {
        var repo = Support.RepoRoot();

        OutputGuard.EnsureOutside(repo + "-plain", repo);
    }

    [Fact]
    public void TheDefaultIsUnderTheTempFolderAndOutsideTheRepo()
    {
        var repo = Support.RepoRoot();

        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), OutputGuard.DefaultOutput());
        OutputGuard.EnsureOutside(OutputGuard.DefaultOutput(), repo);
    }

    [Fact]
    public async Task FetchRefusesBeforeWritingAnything()
    {
        var repo = Support.RepoRoot();
        var inside = Path.Combine(repo, "log-loop-should-not-exist");
        var source = new Support.FakeSource(new() { ["swt-log.post.a.000001-000001.json"] = Support.Sealed(Support.Batch("post", "owner", "x"), Support.Key) });

        await Assert.ThrowsAsync<OutputInsideRepoException>(() => new FetchCommand(source, Support.Key).RunAsync(inside, repo, CancellationToken.None));

        Assert.False(Directory.Exists(inside));
    }

    [Fact]
    public void TheRepoIgnoresLogLoopFolders()
    {
        var ignore = File.ReadAllText(Path.Combine(Support.RepoRoot(), ".gitignore"));

        Assert.Contains("log-loop/", ignore);
    }
}
