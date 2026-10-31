using Constitution.Infrastructure.ReadBack;
using Moq;
using Storyworthtelling.Remarkable;

namespace Constitution.Tests.ReadBack;

/// <summary>The tablet's cloud as the read-back uses it: the shared client, nothing else, and nothing uploaded.</summary>
public class RemarkableAnnotatedPageSourceTests
{
    private static readonly byte[] Pdf = [37, 80, 68, 70, 45];

    private static (RemarkableAnnotatedPageSource Source, Mock<IRemarkableClient> Client) Make()
    {
        var client = new Mock<IRemarkableClient>(MockBehavior.Strict);
        return (new RemarkableAnnotatedPageSource(client.Object), client);
    }

    [Fact]
    public async Task DownloadsByTheExactTitleAndKeepsTheIdForTheTrash()
    {
        var (source, client) = Make();
        client.Setup(c => c.DownloadByTitle("Title One")).ReturnsAsync(new RemarkableDoc { Id = "doc-1", Title = "Title One", PdfBytes = Pdf });

        var found = await source.Download("Title One");

        Assert.Equal(new TabletDocument("doc-1", "Title One", Pdf).Id, found!.Id);
        Assert.Equal("Title One", found.Title);
        Assert.Equal(Pdf, found.PdfBytes);
        client.VerifyAll();
    }

    [Fact]
    public async Task ADocumentTheTabletDoesNotHaveIsNullNotAnError()
    {
        var (source, client) = Make();
        client.Setup(c => c.DownloadByTitle("Gone")).ReturnsAsync((RemarkableDoc?)null);

        Assert.Null(await source.Download("Gone"));
    }

    [Fact]
    public async Task LocatingFindsTheIdWithoutFetchingThePages()
    {
        var (source, client) = Make();
        client.Setup(c => c.ListRecent()).ReturnsAsync(
        [
            new RemarkableDoc { Id = "a", Title = "Other", PdfBytes = [] },
            new RemarkableDoc { Id = "b", Title = "Title One", PdfBytes = [] },
        ]);

        Assert.Equal("b", await source.Locate("Title One"));
        client.Verify(c => c.DownloadByTitle(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LocatingMatchesTitlesTheWayTheSharedClientDoes()
    {
        var (source, client) = Make();
        client.Setup(c => c.ListRecent()).ReturnsAsync([new RemarkableDoc { Id = "b", Title = "title one", PdfBytes = [] }]);

        Assert.Equal("b", await source.Locate("Title One"));
    }

    [Fact]
    public async Task LocatingSomethingAbsentIsNull()
    {
        var (source, client) = Make();
        client.Setup(c => c.ListRecent()).ReturnsAsync([]);

        Assert.Null(await source.Locate("Anything"));
    }

    [Fact]
    public async Task TrashingMovesTheDocumentToTheTabletsTrash()
    {
        var (source, client) = Make();
        client.Setup(c => c.MoveToTrash("doc-1")).Returns(Task.CompletedTask);

        await source.Trash("doc-1");

        client.VerifyAll();
    }

    [Fact]
    public async Task ATransportFailurePropagatesWithItsOwnException()
    {
        var (source, client) = Make();
        var down = new HttpRequestException("down");
        client.Setup(c => c.MoveToTrash(It.IsAny<string>())).ThrowsAsync(down);

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => source.Trash("doc-1"));

        Assert.Same(down, thrown);
    }
}
