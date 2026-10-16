namespace Pulp.Tests.Wiring;

/// <summary>
/// The three cross-app contracts are built as couriers and a receiver; this holds that the composition
/// root actually calls them (G16, G17, G18). Source-level, like the calendar hooks' guard, because the
/// composition cannot be constructed without a device.
/// </summary>
public class CourierWiringGuardTests
{
    private static string Services() => File.ReadAllText(Path.Combine(PulpSource.Root(), "Pulp.Android/App/PulpServices.cs"));

    [Fact]
    public void RecordingAHandOffPublishesTheUsageToIndex()
    {
        var text = Services();

        Assert.Contains("IndexUsageCourier.Publish(", text, StringComparison.Ordinal);
        Assert.Contains("UsageHandOffs.From(", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameHandOffAlsoQueuesTheUsageFileForDrive()
    {
        var text = Services();

        Assert.Contains("usageFile.Queue(", text, StringComparison.Ordinal);
        var publisher = File.ReadAllText(Path.Combine(PulpSource.Root(), "Pulp.Android/Usage/IndexUsageFilePublisher.cs"));
        Assert.Contains("DriveIndexUsageStore", publisher, StringComparison.Ordinal);
        Assert.Contains("IndexUsageFileQueue", publisher, StringComparison.Ordinal);
        Assert.Contains("IndexUsageFileFlusher", publisher, StringComparison.Ordinal);
        var flusher = File.ReadAllText(Path.Combine(PulpSource.Root(), "Pulp.Domain/Usage/IndexUsageFileFlusher.cs"));
        Assert.Contains("catch (Exception", flusher, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHiddenSetGoesToFlyleafThroughTheGateFromTheHouseholdsServices()
    {
        var text = Services();

        Assert.Contains("HiddenAppsCourier.Send(", text, StringComparison.Ordinal);
        Assert.Contains("hiddenGate.ShouldSend(", text, StringComparison.Ordinal);
        Assert.Contains("ServiceCatalogue.Public", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSessionHookIsSetAndResolvesThroughTheDomainRule()
    {
        var text = Services();

        Assert.Contains("SessionHooks.Record = RecordSession;", text, StringComparison.Ordinal);
        Assert.Contains("SessionRecording.Resolve(", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSessionHookNeverMarksAnythingWatchedItself()
    {
        var text = Services();
        var start = text.IndexOf("private async Task RecordSession(", StringComparison.Ordinal);
        Assert.True(start > 0, "The session hook's method was not found; the guard has nothing to protect.");
        var next = text.IndexOf("\n    // ----", start, StringComparison.Ordinal);
        var hook = next < 0 ? text[start..] : text[start..next];

        Assert.DoesNotContain("MarkedWatched", hook, StringComparison.Ordinal);
        Assert.DoesNotContain("Watched.Decide", hook, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHandOffStillGoesOnlyThroughTheExecutor()
    {
        // The wiring adds no intent of its own: Services builds no VIEW intent and names no planner.
        var text = Services();

        Assert.DoesNotContain("ActionView", text, StringComparison.Ordinal);
        Assert.DoesNotContain("HandOffPlanner", text, StringComparison.Ordinal);
    }
}
