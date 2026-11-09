using Storyworthtelling.Voice;
using Xunit;

namespace Storyworthtelling.Voice.Tests;

public class DriveQueryTests
{
    [Fact]
    public void Name_QuotesTheNameAndLeavesTrashOut()
    {
        Assert.Equal("name='notes.json' and trashed=false", DriveQuery.Name("notes.json"));
    }

    [Fact]
    public void Literal_EscapesAQuoteAndABackslash()
    {
        // Drive's search terms: a single quote and a backslash inside a quoted value are each escaped with '\'.
        // The backslash goes first, or the quote's own escape would be doubled.
        Assert.Equal(@"'Sam\'s \\ book'", DriveQuery.Literal(@"Sam's \ book"));
    }

    [Fact]
    public void InFolder_QuotesTheIdAndLeavesTrashOut()
    {
        Assert.Equal("'folder-1' in parents and trashed=false", DriveQuery.InFolder("folder-1"));
    }

    [Fact]
    public void And_JoinsClauses()
    {
        Assert.Equal(
            "'f' in parents and trashed=false and mimeType='application/vnd.google-apps.folder'",
            DriveQuery.And(DriveQuery.InFolder("f"), DriveQuery.FoldersOnly));
    }

    [Fact]
    public void NameInFolder_CarriesBothAndEscapesTheName()
    {
        Assert.Equal(
            @"'f' in parents and trashed=false and name='It\'s'",
            DriveQuery.And(DriveQuery.InFolder("f"), DriveQuery.NameIs("It's")));
    }

    [Fact]
    public void TheChooserUsesTheSameName()
    {
        Assert.Equal(DriveQuery.Name("a'b"), DriveFileChooser.NameQuery("a'b"));
    }

    [Fact]
    public async Task ListAll_FollowsEveryPageToken()
    {
        var pages = new Dictionary<string, DrivePage<int>>
        {
            [""] = new([1, 2], "p2"),
            ["p2"] = new([3], "p3"),
            ["p3"] = new([4, 5], null),
        };
        var asked = new List<string?>();

        var all = await DrivePaging.ListAllAsync<int>((token, _) =>
        {
            asked.Add(token);
            return Task.FromResult(pages[token ?? ""]);
        }, CancellationToken.None);

        Assert.Equal([1, 2, 3, 4, 5], all);
        Assert.Equal([null, "p2", "p3"], asked);
    }

    [Fact]
    public async Task ListAll_TreatsAnEmptyTokenAsTheEnd()
    {
        var calls = 0;
        var all = await DrivePaging.ListAllAsync<int>((_, _) =>
        {
            calls++;
            return Task.FromResult(new DrivePage<int>(null, ""));
        }, CancellationToken.None);

        Assert.Empty(all);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ListAll_RefusesAPageTokenThatRepeats()
    {
        // A token handed back twice would loop forever; it is a broken listing, said so, not a hang.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DrivePaging.ListAllAsync<int>((_, _) => Task.FromResult(new DrivePage<int>([1], "same")), CancellationToken.None));

        Assert.Contains("page token", ex.Message);
    }

    [Fact]
    public async Task ListAll_StopsAtTheCancellation()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            DrivePaging.ListAllAsync<int>((_, _) => Task.FromResult(new DrivePage<int>([1], "next")), cancel.Token));
    }
}
