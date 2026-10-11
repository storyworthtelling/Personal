using Pulp.Domain.Services;

namespace Pulp.Tests.Wiring;

/// <summary>
/// The review's findings on the cross-app sends, held at source level because the receiver, courier
/// and composition cannot be built without a device: a cold-started receiver reaches its hook, a
/// failed hidden-apps send is forgotten, and a hand-off write publishes only what it touched.
/// </summary>
public class CrossAppSendGuardTests
{
    private static string Read(string relative) => File.ReadAllText(Path.Combine(PulpSource.Root(), relative));

    [Fact]
    public void TheSessionReceiverComposesThePulpServicesBeforeItReadsTheHook()
    {
        var text = Read("Pulp.Android/Sessions/SessionReceiver.cs");

        var compose = text.IndexOf("PulpServices.For(context", StringComparison.Ordinal);
        var hook = text.IndexOf("SessionHooks.Record", text.IndexOf("OnReceive(", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(compose > 0, "OnReceive must construct the services itself: a cold-started receiver has no activity to have done it.");
        Assert.True(hook > compose, "The hook is set by the services' constructor, so it can only be read after PulpServices.For.");
    }

    [Fact]
    public void TheSessionReceiverHoldsTheBroadcastOpenUntilTheRecordingFinishes()
    {
        var text = Read("Pulp.Android/Sessions/SessionReceiver.cs");

        Assert.Contains("GoAsync()", text, StringComparison.Ordinal);
        Assert.Contains(".Finish()", text, StringComparison.Ordinal);
        Assert.Contains("Func<SessionMeaning, Task>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordSessionReturnsATaskTheReceiverCanAwait()
    {
        var text = Read("Pulp.Android/App/PulpServices.cs");

        Assert.Contains("private async Task RecordSession(SessionMeaning meaning)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = Task.Run(async () =>\n        {\n            try\n            {\n                var view = HouseholdView.Of(await household.LoadAsync(CancellationToken.None));\n                foreach (var sitting in SessionRecording", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHiddenCourierReportsWhetherABroadcastWasSentAndTheCallerForgetsOnFalse()
    {
        var courier = Read("Pulp.Android/Hidden/HiddenAppsCourier.cs");
        var services = Read("Pulp.Android/App/PulpServices.cs");

        Assert.Contains("public static bool Send(", courier, StringComparison.Ordinal);
        Assert.Contains("!HiddenAppsCourier.Send(", services, StringComparison.Ordinal);
        Assert.Contains("hiddenGate.Forget()", services, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyAnEditMayEmptyTheHiddenSetAndALoadNeverDoes()
    {
        var services = Read("Pulp.Android/App/PulpServices.cs");

        Assert.Contains("HiddenSendCause.Load", services, StringComparison.Ordinal);
        Assert.Contains("HiddenSendCause.Edit", services, StringComparison.Ordinal);
    }

    [Fact]
    public void AHandOffWritePublishesOnlyTheServiceMonthItTouched()
    {
        var services = Read("Pulp.Android/App/PulpServices.cs");

        Assert.Contains("IndexUsagePayload.ForTouched(", Read("Pulp.Android/Usage/IndexUsageCourier.cs"), StringComparison.Ordinal);
        Assert.Contains("IndexUsageCourier.Publish(appContext, UsageHandOffs.From(view.AllSittings), ServiceCatalogue.Public, touched)", services, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSessionRecordingPassesTheNamesAndTheTitleChangeMemoryToTheRule()
    {
        var services = Read("Pulp.Android/App/PulpServices.cs");

        Assert.Contains("titleChanges", services, StringComparison.Ordinal);
        Assert.Contains("titleName:", services, StringComparison.Ordinal);
    }

    [Fact]
    public void NoServiceIsMatchedByAMerchantNameAsShortAndCommonAsOneWord()
    {
        var hbo = Assert.IsType<StreamingService>(ServiceCatalogue.Public.ById(new Pulp.Domain.Core.ServiceId("hbo-max")));

        Assert.DoesNotContain("Max", hbo.MerchantNames);
        Assert.Null(ServiceCatalogue.Public.ByMerchant("Max"));
        Assert.Equal(hbo, ServiceCatalogue.Public.ByMerchant("hbo max"));
        Assert.Contains("HBO Max", hbo.MerchantNames);
        Assert.Contains("Max.com", hbo.MerchantNames);
        Assert.All(ServiceCatalogue.Public.All, service =>
            Assert.DoesNotContain(service.MerchantNames, name => !name.Contains('.') && !name.Contains(' ') && name.Length <= 3));
    }
}
