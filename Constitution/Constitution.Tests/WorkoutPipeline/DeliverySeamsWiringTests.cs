using System.Text.RegularExpressions;

namespace Constitution.Tests.WorkoutPipeline;

/// <summary>P13: the delivery-side seams, held by reading their source, as the other wiring guards do.</summary>
public class DeliverySeamsWiringTests
{
    private static string Source(string fileName)
    {
        var file = ConstitutionSource.Files(".cs").SingleOrDefault(path => Path.GetFileName(path) == fileName);
        Assert.True(file is not null, fileName + " was not found under Constitution/.");
        return File.ReadAllText(file!);
    }

    private static string Seams() => Source("WorkoutWeekDeliverySeams.cs");

    [Fact]
    public void CreateReturnsTheRecordAndNeverThrowsForPairing()
    {
        var source = Seams();

        Assert.Contains("public static DeliverySideSeams Create(Context context)", source, StringComparison.Ordinal);
        Assert.Contains("public sealed record DeliverySideSeams(", source, StringComparison.Ordinal);
        foreach (var member in new[] { "DeliveryService Delivery", "WorkoutDocumentDelivery Documents", "IRemarkableClient Tablet", "IPageInkProbe InkProbe",
            "IExpectedDocuments Expected", "IDeliveredWorkouts Delivered", "IWorkoutEncouragement Encouragement", "IWorkoutWeekLog Log", "IPlanLog PlanLog" })
        {
            Assert.Contains(member, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheTabletClientIsTheReadBacksOwn()
    {
        var source = Seams();

        Assert.Contains("ReadBackServices.TabletOf(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RemarkableAuthService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RemarkableDeviceToken", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeviceToken", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheClaudeKeyIsOnlyEverTheReadBacksVision()
    {
        var source = Seams();

        Assert.Contains("ReadBackServices.VisionOf(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClaudeApiKey", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Passphrase", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpectedDocumentsAreTheReadBacksOwnStore()
    {
        var source = Seams();

        Assert.Contains("ReadBackServices.ExpectedOf(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new FileExpectedDocuments", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"expected.json\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"read-back\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadBackAndDeliverySpellNoPathTwice()
    {
        var readBack = Source("ReadBackServices.cs");

        Assert.Contains("ReadBackPaths.", readBack, StringComparison.Ordinal);
        Assert.DoesNotContain("\"expected.json\"", readBack, StringComparison.Ordinal);
        Assert.DoesNotContain("\"read-back\"", readBack, StringComparison.Ordinal);
        Assert.DoesNotContain("Path.Combine", readBack, StringComparison.Ordinal);
    }

    [Fact]
    public void TheInkProbeUsesTheProofsCurrentValue()
    {
        var source = Seams();

        Assert.Contains("new VisionPageInkProbe(", source, StringComparison.Ordinal);
        Assert.Contains("ReadBackServices.InkProofOf(", source, StringComparison.Ordinal);
        Assert.Contains("IsEstablished", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DeliveredAndTheTrailUseTheirRealStores()
    {
        var source = Seams();

        Assert.Contains("new FileDeliveredWorkouts(WorkoutWeekPaths.Delivered(", source, StringComparison.Ordinal);
        Assert.Contains("NoBackupFilesDir", source, StringComparison.Ordinal);
        Assert.DoesNotContain("FilesDir.", source.Replace("NoBackupFilesDir", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("new TrailPlanLog(", source, StringComparison.Ordinal);
        Assert.Contains("new BodyLog(", source, StringComparison.Ordinal);
        Assert.Contains("new DeliveryService(", source, StringComparison.Ordinal);
        Assert.Contains("new ConnectionNoticeBoardAccess()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EncouragementUsesTheHeadsExistingVoice()
    {
        var source = Seams();

        Assert.Contains("new SeatLineEncouragement(", source, StringComparison.Ordinal);
        Assert.Contains("BodySeats.ComposeAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingIsLoggedButTypeNamesAndNoSecretIsHandled()
    {
        foreach (var name in new[] { "WorkoutWeekDeliverySeams.cs", "WorkoutWeekPaths.cs", "DeliverySeamAdapters.cs", "ReadBackPaths.cs" })
        {
            var source = Source(name);
            Assert.DoesNotContain("Log.", source.Replace("BodyLog", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
            Assert.DoesNotContain(".Message", source, StringComparison.Ordinal);
            Assert.DoesNotContain("catch (Exception", source, StringComparison.Ordinal);
            Assert.False(Regex.IsMatch(source, @"catch\s*(\([^)]*\))?\s*\{\s*\}"), name + " has an empty catch.");
            Assert.False(Regex.IsMatch(source, "#[0-9A-Fa-f]{6}\\b"), name + " holds a hex literal.");
            Assert.DoesNotContain("SetTextSize", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AddPermission", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheManifestAndSkeletonAreUntouched()
    {
        var services = Source("WorkoutWeekServices.cs");
        Assert.Contains("public static Task<WorkoutWeekOutcome> RunAsync(Context context, CancellationToken cancellation)", services, StringComparison.Ordinal);
    }
}
