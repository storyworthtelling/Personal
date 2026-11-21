using System.Text;
using System.Text.Json;
using Watch.Domain;
using Watch.Domain.Updates;

namespace Watch.Tests.Updates;

/// <summary>
/// The <c>update/installed</c> contract (plan §2.5): Flyleaf asks over <c>MessageClient</c> what the watch has, and the
/// watch answers with its suite apps' versions and the last outcome of each install. Every message carries an integer
/// <c>v</c>; a newer one is refused, not guessed at.
/// </summary>
public class InstalledMessageTests
{
    private static InstalledReport Report() => new(
        [new WatchApp(WatchIdentity.InstallerPackage, 1_100_000_400), new WatchApp(WatchIdentity.FacePackage, 1_100_000_401)],
        [
            new WatchInstallResult(WatchIdentity.DashPackage, 1_100_000_402, WatchInstallOutcome.Waiting),
            new WatchInstallResult(WatchIdentity.WavePackage, 1_100_000_403, WatchInstallOutcome.Failed, InstallerStatus.Storage),
        ]);

    private static ContractRead<InstalledMessage> Decode(string json) => InstalledCodec.Decode(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void AnAskRoundTrips()
    {
        var read = InstalledCodec.Decode(InstalledCodec.Encode(new InstalledAsk()));

        Assert.True(read.Accepted);
        Assert.IsType<InstalledAsk>(read.Message);
        Assert.Equal(1, read.Version);
    }

    [Fact]
    public void AReportRoundTrips()
    {
        var sent = Report();

        var read = InstalledCodec.Decode(InstalledCodec.Encode(sent));

        var report = Assert.IsType<InstalledReport>(read.Message);
        Assert.Equal(sent.Apps, report.Apps);
        Assert.Equal(sent.Results, report.Results);
    }

    [Fact]
    public void AnEmptyReportRoundTrips()
    {
        var report = Assert.IsType<InstalledReport>(InstalledCodec.Decode(InstalledCodec.Encode(new InstalledReport([], []))).Message);

        Assert.Empty(report.Apps);
        Assert.Empty(report.Results);
    }

    [Fact]
    public void EveryMessageCarriesAnIntegerV()
    {
        foreach (InstalledMessage message in new InstalledMessage[] { new InstalledAsk(), Report() })
        {
            using var document = JsonDocument.Parse(InstalledCodec.Encode(message));
            var v = document.RootElement.GetProperty("v");
            Assert.Equal(JsonValueKind.Number, v.ValueKind);
            Assert.Equal(InstalledCodec.Version, v.GetInt32());
        }
        Assert.Equal(1, InstalledCodec.Version);
    }

    [Fact]
    public void ANewerVersionIsRefused()
    {
        var read = Decode("{\"v\":2,\"kind\":\"ask\"}");

        Assert.False(read.Accepted);
        Assert.Equal(ContractRefusal.Newer, read.Refusal);
        Assert.Equal(2, read.Version);
        Assert.Null(read.Message);
    }

    [Theory]
    [InlineData("{\"kind\":\"ask\"}")]
    [InlineData("{\"v\":\"1\",\"kind\":\"ask\"}")]
    [InlineData("{\"v\":1.5,\"kind\":\"ask\"}")]
    [InlineData("{\"v\":0,\"kind\":\"ask\"}")]
    [InlineData("{\"v\":1}")]
    [InlineData("{\"v\":1,\"kind\":\"tell\"}")]
    [InlineData("{\"v\":1,\"kind\":\"report\"}")]
    [InlineData("{\"v\":1,\"kind\":\"report\",\"apps\":[{\"package\":\"\",\"versionCode\":1}],\"results\":[]}")]
    [InlineData("{\"v\":1,\"kind\":\"report\",\"apps\":[{\"package\":\"com.storyworthtelling.watch.point\"}],\"results\":[]}")]
    [InlineData("{\"v\":1,\"kind\":\"report\",\"apps\":[],\"results\":[{\"package\":\"com.storyworthtelling.watch.point\",\"versionCode\":1,\"outcome\":\"sideways\"}]}")]
    [InlineData("{\"v\":1,\"kind\":\"report\",\"apps\":[],\"results\":[{\"package\":\"com.storyworthtelling.watch.point\",\"versionCode\":1,\"outcome\":\"failed\",\"status\":\"melted\"}]}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("")]
    public void AMalformedMessageIsUnreadable(string json)
    {
        var read = Decode(json);

        Assert.Equal(ContractRefusal.Unreadable, read.Refusal);
        Assert.Null(read.Message);
    }

    [Fact]
    public void NothingIsUnreadable()
    {
        Assert.Equal(ContractRefusal.Unreadable, InstalledCodec.Decode(null).Refusal);
    }

    [Fact]
    public void TheReportSaysNothingElse()
    {
        // Packages, version codes, outcome and status words: nothing from an APK, a message or the person.
        using var document = JsonDocument.Parse(InstalledCodec.Encode(Report()));
        var root = document.RootElement;

        Assert.Equal(["v", "kind", "apps", "results"], root.EnumerateObject().Select(property => property.Name));
        Assert.All(root.GetProperty("apps").EnumerateArray(), app =>
            Assert.Equal(["package", "versionCode"], app.EnumerateObject().Select(property => property.Name)));
        Assert.Equal(["package", "versionCode", "outcome"], root.GetProperty("results")[0].EnumerateObject().Select(property => property.Name));
        Assert.Equal(["package", "versionCode", "outcome", "status"], root.GetProperty("results")[1].EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void TheWordsAreFrozen()
    {
        Assert.Equal("v", InstalledCodec.VersionField);
        Assert.Equal("kind", InstalledCodec.KindField);
        Assert.Equal("ask", InstalledCodec.AskKind);
        Assert.Equal("report", InstalledCodec.ReportKind);
        Assert.Equal("apps", InstalledCodec.AppsField);
        Assert.Equal("results", InstalledCodec.ResultsField);
        Assert.Equal("package", InstalledCodec.PackageField);
        Assert.Equal("versionCode", InstalledCodec.VersionCodeField);
        Assert.Equal("outcome", InstalledCodec.OutcomeField);
        Assert.Equal("status", InstalledCodec.StatusField);
    }
}
