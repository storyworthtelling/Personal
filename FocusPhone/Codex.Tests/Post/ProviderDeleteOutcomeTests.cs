using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// A filed call or text whose row the system had already removed (the call log is trimmed on its own) deleted
/// zero rows, which was read as a failure; the tombstone then stood and the delete was retried on every sync forever.
/// </summary>
public class ProviderDeleteOutcomeTests
{
    [Fact]
    public void ARowTheDeleteRemovedIsSettled()
    {
        Assert.True(ProviderDeleteOutcome.Settled(rowsDeleted: 1, rowStillExists: () => true));
    }

    [Fact]
    public void ARowThatWasAlreadyGoneIsSettledBecauseNothingIsOwed()
    {
        Assert.True(ProviderDeleteOutcome.Settled(rowsDeleted: 0, rowStillExists: () => false));
    }

    [Fact]
    public void ARowThatIsStillThereAfterNothingWasDeletedIsNotSettled()
    {
        Assert.False(ProviderDeleteOutcome.Settled(rowsDeleted: 0, rowStillExists: () => true));
    }

    [Fact]
    public void ThePresenceCheckIsNotRunWhenTheDeleteRemovedARow()
    {
        var asked = false;
        ProviderDeleteOutcome.Settled(1, () => { asked = true; return true; });

        Assert.False(asked);
    }
}

public class PostSyncUsesProviderDeleteOutcomeTests
{
    [Fact]
    public void BothProviderDeletesDecideThroughTheOutcome()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Post.Android", "PostSync.cs")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);
        var source = File.ReadAllText(Path.Combine(dir!, "Post.Android", "PostSync.cs"));

        Assert.DoesNotContain("return deleted > 0;", source, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(source, "ProviderDeleteOutcome\\.Settled").Count);
    }
}
