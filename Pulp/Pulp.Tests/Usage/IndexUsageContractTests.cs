using System.Text.RegularExpressions;
using Pulp.Domain.Core;
using Pulp.Domain.Services;
using Pulp.Domain.Usage;

namespace Pulp.Tests.Usage;

/// <summary>
/// Guard G16, Pulp's half: the hand-off contract to Index (D90, D269–D271) is pinned here and read
/// back out of Index's own receiver and manifest by content, so a drift on either side fails a test
/// instead of Index quietly going on answering to an action nobody sends. Index's half
/// (<c>Ledger.Tests/Guard/PulpUsageContractTests</c>) reads this contract's constants the same way.
/// </summary>
public class IndexUsageContractTests
{
    private static readonly ServiceCatalogue Catalogue = ServiceCatalogue.Public;

    private static ServiceMonthUsage Row(string service, int year, int month, int handOffs, int? minutes, DateOnly? last) =>
        new(new ServiceId(service), new UsageMonth(year, month), handOffs, minutes, last);

    [Fact]
    public void TheRowCrossesAsServiceMonthCountAndLastDayOpenedWithTheServicesMerchantNames()
    {
        var payload = IndexUsagePayload.From(Row("netflix", 2026, 9, 14, 600, new DateOnly(2026, 9, 28)));

        var extras = IndexUsageContract.Extras(payload, Catalogue);

        Assert.Equal("netflix", extras[IndexUsageContract.ServiceExtra]);
        Assert.Equal("2026-09", extras[IndexUsageContract.MonthExtra]);
        Assert.Equal("14", extras[IndexUsageContract.CountExtra]);
        Assert.Equal("2026-09-28", extras[IndexUsageContract.LastOpenedExtra]);
        Assert.Equal("Netflix|Netflix.com", extras[IndexUsageContract.MerchantsExtra]);
    }

    [Fact]
    public void MinutesWatchedNeverCrossBecauseNoDecisionAllowsThem()
    {
        var payload = IndexUsagePayload.From(Row("hulu", 2026, 9, 3, 245, new DateOnly(2026, 9, 2)));

        var extras = IndexUsageContract.Extras(payload, Catalogue);

        Assert.Equal(
            ["count", "lastOpened", "merchants", "month", "service"],
            extras.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList());
        Assert.DoesNotContain("245", extras.Values);
    }

    [Fact]
    public void ADatelessRowCarriesNoDateKeyRatherThanAnEmptyOne()
    {
        var payload = IndexUsagePayload.From(Row("hulu", 2026, 9, 3, null, null));

        Assert.False(IndexUsageContract.Extras(payload, Catalogue).ContainsKey(IndexUsageContract.LastOpenedExtra));
    }

    [Fact]
    public void OnlyMonthsWithAHandOffFromAKnownServiceAreSent()
    {
        var rows = new[]
        {
            Row("netflix", 2026, 8, 2, null, new DateOnly(2026, 8, 4)),
            Row("netflix", 2026, 9, 0, 30, null),
            Row("not-in-the-table", 2026, 9, 5, null, new DateOnly(2026, 9, 1)),
        };

        var payloads = IndexUsagePayload.ForRows(rows, Catalogue);

        var only = Assert.Single(payloads);
        Assert.Equal(new UsageMonth(2026, 8), only.Month);
    }

    [Fact]
    public void ANegativeCountIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new IndexUsagePayload(new ServiceId("netflix"), new UsageMonth(2026, 9), -1, null));
    }

    [Fact]
    public void AMerchantNameCarryingTheSeparatorIsRefusedSoItCannotSplitInTwo()
    {
        var service = new StreamingService(new ServiceId("x"), "X", ["com.example.x"], "x", ["A|B"]);
        var payload = new IndexUsagePayload(service.Id, new UsageMonth(2026, 9), 1, null);

        Assert.Throws<ArgumentException>(() => IndexUsageContract.Extras(payload, new ServiceCatalogue([service])));
    }

    [Fact]
    public void AServiceTheCatalogueLacksCannotBeSent()
    {
        var payload = new IndexUsagePayload(new ServiceId("not-in-the-table"), new UsageMonth(2026, 9), 1, null);

        Assert.Throws<ArgumentException>(() => IndexUsageContract.Extras(payload, Catalogue));
    }

    // ---- G16: the two sides agree, by content ----

    [Fact]
    public void ThePackageIsTheOneIndexKeptFromLedgerAndPostsHandoffNamesToo()
    {
        Assert.Equal(PostHandoffConstant("Package"), IndexUsageContract.Package);
    }

    [Fact]
    public void TheReceiverIsIndexsUsageReceiverInThatPackage()
    {
        Assert.Equal(IndexUsageContract.Package + ".UsageReceiver", IndexUsageContract.Receiver);
        Assert.Contains($"Name = IndexIdentity.ApplicationId + \".UsageReceiver\"", IndexReceiverSource(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheActionAndPermissionAreTheOnesIndexsReceiverAndManifestDeclare()
    {
        var receiver = IndexReceiverSource();
        var actionSuffix = IndexUsageContract.Action[IndexUsageContract.Package.Length..];
        var permissionSuffix = IndexUsageContract.Permission[IndexUsageContract.Package.Length..];

        Assert.Equal("com.storyworthtelling.ledger.action.USAGE", IndexUsageContract.Action);
        Assert.Equal("com.storyworthtelling.ledger.RECEIVE_USAGE", IndexUsageContract.Permission);
        Assert.Contains($"HandoffAction = IndexIdentity.ApplicationId + \"{actionSuffix}\";", receiver, StringComparison.Ordinal);
        Assert.Contains($"Permission = IndexIdentity.ApplicationId + \"{permissionSuffix}\"", receiver, StringComparison.Ordinal);
        Assert.Contains(
            $"<permission android:name=\"{IndexUsageContract.Permission}\" android:protectionLevel=\"signature\" />",
            File.ReadAllText(IndexPath("Platforms", "Android", "AndroidManifest.xml")), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPayloadFieldNameIsTheOneIndexsReceiverReads()
    {
        var receiver = IndexReceiverSource();

        foreach (var (name, value) in new[]
                 {
                     ("ServiceExtra", IndexUsageContract.ServiceExtra),
                     ("MonthExtra", IndexUsageContract.MonthExtra),
                     ("CountExtra", IndexUsageContract.CountExtra),
                     ("LastOpenedExtra", IndexUsageContract.LastOpenedExtra),
                     ("MerchantsExtra", IndexUsageContract.MerchantsExtra),
                 })
        {
            Assert.Contains($"public const string {name} = \"{value}\";", receiver, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheSenderNamesTheComponentAndTheSignaturePermissionAndTheHeadDeclaresItUsesIt()
    {
        var courier = File.ReadAllText(Path.Combine(PulpSource.Root(), "Pulp.Android", "Usage", "IndexUsageCourier.cs"));

        Assert.Contains("new ComponentName(IndexUsageContract.Package, IndexUsageContract.Receiver)", courier, StringComparison.Ordinal);
        Assert.Contains("SendBroadcast(intent, IndexUsageContract.Permission)", courier, StringComparison.Ordinal);
        Assert.Contains("new Intent(IndexUsageContract.Action)", courier, StringComparison.Ordinal);

        var declaration = File.ReadAllText(Path.Combine(PulpSource.Root(), "Pulp.Android", "Usage", "IndexUsagePermission.cs"));
        Assert.Contains($"UsesPermission(Name = \"{IndexUsageContract.Permission}\")", declaration, StringComparison.Ordinal);
    }

    [Fact]
    public void NoOutboxExistsBecauseIndexAbsentIsTheOrdinaryCase()
    {
        var courier = File.ReadAllText(Path.Combine(PulpSource.Root(), "Pulp.Android", "Usage", "IndexUsageCourier.cs"));

        Assert.Contains("catch (Exception", courier, StringComparison.Ordinal);
        Assert.DoesNotContain("Outbox", courier.Replace("No outbox", string.Empty), StringComparison.OrdinalIgnoreCase);
    }

    // ---- helpers ----

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(PulpSource.Root());
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static string IndexPath(params string[] parts) =>
        Path.Combine([RepoRoot(), "Ledger", "Ledger.Android", .. parts]);

    private static string IndexReceiverSource()
    {
        var path = IndexPath("Platforms", "Android", "UsageReceiver.cs");
        Assert.True(File.Exists(path), $"UsageReceiver.cs not found at {path}.");
        return File.ReadAllText(path);
    }

    private static string PostHandoffConstant(string name)
    {
        var path = Path.Combine(RepoRoot(), "FocusPhone", "Codex.Domain", "Post", "MoneyMail.cs");
        var match = Regex.Match(File.ReadAllText(path), $@"public const string {Regex.Escape(name)}\s*=\s*""([^""]*)""\s*;");
        Assert.True(match.Success, $"IndexHandoff.{name} not found in MoneyMail.cs.");
        return match.Groups[1].Value;
    }

    [Fact]
    public void OnlyTheTouchedServiceMonthIsSentNotAllHistory()
    {
        var rows = new[]
        {
            Row("netflix", 2026, 8, 2, null, new DateOnly(2026, 8, 4)),
            Row("netflix", 2026, 9, 6, null, new DateOnly(2026, 9, 4)),
            Row("hulu", 2026, 9, 3, null, new DateOnly(2026, 9, 2)),
        };

        var payloads = IndexUsagePayload.ForTouched(rows, [(new ServiceId("netflix"), new UsageMonth(2026, 9))], Catalogue);

        var only = Assert.Single(payloads);
        Assert.Equal(6, only.HandOffs);
        Assert.Equal(new UsageMonth(2026, 9), only.Month);
    }

    [Fact]
    public void ATouchedMonthThatDroppedToZeroIsSentAsAnExplicitZeroSoIndexClearsIt()
    {
        var payloads = IndexUsagePayload.ForTouched([], [(new ServiceId("netflix"), new UsageMonth(2026, 9))], Catalogue);

        var zero = Assert.Single(payloads);
        Assert.Equal(0, zero.HandOffs);
        Assert.Null(zero.LastOpened);
        var extras = IndexUsageContract.Extras(zero, Catalogue);
        Assert.Equal("0", extras[IndexUsageContract.CountExtra]);
        Assert.False(extras.ContainsKey(IndexUsageContract.LastOpenedExtra));
        Assert.Equal("Netflix|Netflix.com", extras[IndexUsageContract.MerchantsExtra]);
    }

    [Fact]
    public void ATouchedServiceNotInTheCatalogueIsNotSentAndDuplicatesCollapse()
    {
        var touched = new[]
        {
            (new ServiceId("not-in-the-table"), new UsageMonth(2026, 9)),
            (new ServiceId("hulu"), new UsageMonth(2026, 9)),
            (new ServiceId("hulu"), new UsageMonth(2026, 9)),
        };

        Assert.Single(IndexUsagePayload.ForTouched([], touched, Catalogue));
    }
}
