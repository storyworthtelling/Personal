using Moq;
using Storyworthtelling.Paper;
using Storyworthtelling.Remarkable;

namespace Storyworthtelling.Paper.Tests;

public class DeliveryProfilePolicyTests
{
    [Theory]
    [InlineData(true, DeviceColorProfile.PaperPro)]
    [InlineData(false, DeviceColorProfile.Rm2)]
    public void ReMarkableProfileFollowsTheTablet(bool colorDevice, DeviceColorProfile expected) =>
        Assert.Equal(expected, DeliveryProfilePolicy.ForRemarkable(colorDevice));

    [Fact]
    public void DriveGetsScreenRgb() =>
        Assert.Equal(DeviceColorProfile.Screen, DeliveryProfilePolicy.ForDrive());
}

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
    public async Task DeliverToRemarkableRootUploadsTitleAndBytes()
    {
        var remarkable = new Mock<IRemarkableClient>();
        var delivery = new PaperDelivery(remarkable.Object);
        var bytes = new byte[] { 1, 2, 3 };

        await delivery.DeliverToRemarkableRoot("Report", bytes);

        remarkable.Verify(r => r.Upload("Report", bytes), Times.Once);
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

        var noDrive = new PaperDelivery(Mock.Of<IRemarkableClient>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => noDrive.UploadDriveFile("f", "n", "m", [1]));
    }
}
