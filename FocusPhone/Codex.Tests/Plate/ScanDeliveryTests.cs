using Codex.Domain.Plate;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// Which way a finished scan leaves the phone. The delivery is deliberately hybrid: share
/// intents for Drive and Post, the shared reMarkable library for the tablet.
/// </summary>
public class ScanDeliveryTests
{
    [Theory]
    [InlineData(ScanDestination.Drive)]
    [InlineData(ScanDestination.Post)]
    public void DriveAndPostGoOutAsAShareIntent(ScanDestination destination)
    {
        // Both are apps that already know how to receive a PDF, so Plate hands the document
        // over rather than acquiring a Drive API client and a drive write scope of its own.
        Assert.Equal(DeliveryRoute.ShareIntent, ScanDelivery.RouteFor(destination));
    }

    [Fact]
    public void TheRemarkableGoesThroughTheSharedLibraryToTheRoot()
    {
        // There is no reMarkable app to share to. The one route is the shared library's
        // plain root upload — the overload Foreword has actually exercised — rather than
        // EnsureFolder plus the folder overload, whose wire format is still reconstructed.
        Assert.Equal(DeliveryRoute.RemarkableRoot, ScanDelivery.RouteFor(ScanDestination.Remarkable));
    }

    [Fact]
    public void TheShareIsAimedAtOneAppRatherThanAChooser()
    {
        // A bare chooser on a locked phone lists whatever survived the lockdown, which is
        // not the door the owner picked. Naming the package makes the pick mean something.
        Assert.Equal("com.google.android.apps.docs", ScanDelivery.TargetPackageFor(ScanDestination.Drive));
        Assert.Equal(PolicyContext.CommunicationPackage, ScanDelivery.TargetPackageFor(ScanDestination.Post));
    }

    [Fact]
    public void TheRemarkableHasNoAppToShareTo()
    {
        // Null is the answer, not the reMarkable's own package: its Android app cannot
        // receive a PDF this way, which is exactly why that route goes through the library.
        Assert.Null(ScanDelivery.TargetPackageFor(ScanDestination.Remarkable));
    }

    [Fact]
    public void EveryDoorHasARoute()
    {
        // A fourth destination added without a route would fall through to a default and
        // silently deliver nowhere.
        foreach (var destination in Enum.GetValues<ScanDestination>())
        {
            Assert.True(Enum.IsDefined(ScanDelivery.RouteFor(destination)));
        }
    }
}
