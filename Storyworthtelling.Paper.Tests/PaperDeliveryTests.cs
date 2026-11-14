using Moq;
using Storyworthtelling.Paper;
using Storyworthtelling.Remarkable;

namespace Storyworthtelling.Paper.Tests;

public class PaperDeliveryTests
{
    [Fact]
    public void CapabilityFlagsReflectWiredTransports()
    {
        Assert.False(new PaperDelivery(null, null).CanDeliverToRemarkable);
        Assert.False(new PaperDelivery(null, null).CanArchiveToDrive);

        var both = new PaperDelivery(Mock.Of<IRemarkableClient>(), Mock.Of<IPaperDriveArchive>());
        Assert.True(both.CanDeliverToRemarkable);
        Assert.True(both.CanArchiveToDrive);
    }

    [Fact]
    public async Task DeliverToRemarkableRootUploadsTitleAndBytesAndPassesCancellation()
    {
        var remarkable = new Mock<IRemarkableClient>();
        var delivery = new PaperDelivery(remarkable.Object);
        var bytes = new byte[] { 1, 2, 3 };
        using var source = new CancellationTokenSource();

        await delivery.DeliverToRemarkableRoot("Report", bytes, source.Token);

        // The token used to stop here (audit 01 §11): a worker told to stop went on uploading.
        remarkable.Verify(r => r.Upload("Report", bytes, null, source.Token), Times.Once);
    }

    [Fact]
    public async Task DeliverToRemarkableFolderFindsTheFolderThenUploadsIntoIt()
    {
        var remarkable = new Mock<IRemarkableClient>();
        remarkable.Setup(r => r.EnsureFolder("Index", It.IsAny<CancellationToken>())).ReturnsAsync("folder-1");
        var delivery = new PaperDelivery(remarkable.Object);
        var bytes = new byte[] { 4 };
        using var source = new CancellationTokenSource();

        await delivery.DeliverToRemarkableFolder("Index", "Worksheet", bytes, source.Token);

        remarkable.Verify(r => r.EnsureFolder("Index", source.Token), Times.Once);
        remarkable.Verify(r => r.Upload("Worksheet", bytes, "folder-1", source.Token), Times.Once);
    }

    [Fact]
    public async Task ARefusalReachesTheCallerAsItIs()
    {
        var remarkable = new Mock<IRemarkableClient>();
        remarkable.Setup(r => r.Upload(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RemarkableRefusedException(RemarkableRefusal.OutOfMemory, 5));

        var refused = await Assert.ThrowsAsync<RemarkableRefusedException>(
            () => new PaperDelivery(remarkable.Object).DeliverToRemarkableRoot("Report", [1]));

        Assert.Equal(RemarkableRefusal.OutOfMemory, refused.Reason);
    }

    [Fact]
    public async Task DriveOperationsDelegateToTheArchive()
    {
        var drive = new Mock<IPaperDriveArchive>();
        drive.Setup(d => d.FindParentFolder("src", It.IsAny<CancellationToken>())).ReturnsAsync("parent");
        drive.Setup(d => d.EnsureFolder("parent", "Quest", It.IsAny<CancellationToken>())).ReturnsAsync("folder");
        drive.Setup(d => d.UploadFile("folder", "Quest.pdf", "application/pdf", It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("fileId");
        var delivery = new PaperDelivery(drive: drive.Object);

        Assert.Equal("parent", await delivery.FindDriveParentFolder("src"));
        Assert.Equal("folder", await delivery.EnsureDriveFolder("parent", "Quest"));
        Assert.Equal("fileId", await delivery.UploadDriveFile("folder", "Quest.pdf", "application/pdf", [9]));
    }

    [Fact]
    public async Task CallingAnUnwiredTransportThrows()
    {
        var noRemarkable = new PaperDelivery(drive: Mock.Of<IPaperDriveArchive>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => noRemarkable.DeliverToRemarkableRoot("t", [1]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => noRemarkable.DeliverToRemarkableFolder("f", "t", [1]));

        var noDrive = new PaperDelivery(Mock.Of<IRemarkableClient>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => noDrive.UploadDriveFile("f", "n", "m", [1]));
    }
}
