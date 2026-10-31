using Pulp.Domain;
using Pulp.Domain.Household.Records;
using Pulp.Infrastructure.Household;
using Storyworthtelling.Household;
using Storyworthtelling.Voice;
using Storyworthtelling.Voice.Secrets;

namespace Pulp.Tests.Household;

/// <summary>
/// The household engine moved out of Pulp into <c>Storyworthtelling.Household</c>, and this is the proof
/// that Pulp, wired to it the way the app wires it, behaves as it did. Two installs write Pulp's own
/// kinds (and one kind nobody registered) with the library's clock and records, merge in both orders and
/// hold one document (G11); the Pulp-wired Drive store never writes over a file it cannot open (G14);
/// and the file Pulp wrote before the move still reads. Every name and figure is invented.
/// </summary>
public sealed class PulpHouseholdStillConvergesTests
{
    private const string Passphrase = "invented-household-passphrase";
    private const string InstallA = "install-a-example";
    private const string InstallB = "install-b-example";
    private const string UnregisteredKind = "kind-nobody-registered";

    private static HouseholdRecord Write(HybridLogicalClock clock, long wall, string kind, string id, string writer, string field)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = field };
        return HouseholdRecord.Write(new RecordKey(kind, id), 1, fields, clock.Now(wall), writer);
    }

    private static string Sealed(HouseholdDocument document, string passphrase = Passphrase) =>
        VaultEnvelope.Write(HouseholdDocumentCodec.Serialize(document), passphrase);

    private static DriveHouseholdStore PulpDrive(FakeDrive drive, string passphrase = Passphrase, bool mayCreate = true) =>
        new(drive, PulpIdentity.HouseholdFileName, () => passphrase, () => mayCreate);

    private static (HouseholdDocument A, HouseholdDocument B) TwoInstalls()
    {
        var clockA = new HybridLogicalClock();
        var clockB = new HybridLogicalClock();
        var shared = Write(clockA, 1000, HouseholdKinds.Playlist, "rainy-day", InstallA, "Rainy day");
        var a = HouseholdDocument.Empty.With(shared);
        var b = HouseholdDocument.Empty.With(shared);

        a = a.With(Write(clockA, 2000, HouseholdKinds.Viewer, "robin-example", InstallA, "Robin"));
        a = a.With(Write(clockA, 3000, UnregisteredKind, "mystery-1", InstallA, "kept untouched"));
        b = b.With(Write(clockB, 2000, HouseholdKinds.Viewer, "robin-example", InstallB, "Robin B"));
        b = b.With(shared.Delete(clockB.Now(4000), InstallB));
        b = b.With(Write(clockB, 5000, HouseholdKinds.Veto, "film-9", InstallB, "Not for me"));
        return (a, b);
    }

    [Fact]
    public void ThePulpTypesAreTheLibrarysTypes()
    {
        Assert.Equal("Storyworthtelling.Household", typeof(HouseholdDocument).Assembly.GetName().Name);
        Assert.Equal("Storyworthtelling.Household", typeof(DriveHouseholdStore).Assembly.GetName().Name);
        Assert.Equal("Storyworthtelling.Household", typeof(LocalHouseholdStore).Assembly.GetName().Name);
    }

    [Fact]
    public void TwoInstallsHoldOneDocumentInEitherOrder()
    {
        var (a, b) = TwoInstalls();

        var aThenB = HouseholdMerge.Merge(a, b);
        var bThenA = HouseholdMerge.Merge(b, a);

        Assert.True(aThenB.ContentEquals(bThenA));
        Assert.Equal(HouseholdSerialised(aThenB), HouseholdSerialised(bThenA));
    }

    [Fact]
    public void TheDeleteStaysAndTheUnknownKindSurvives()
    {
        var (a, b) = TwoInstalls();

        var merged = HouseholdMerge.Merge(a, b);

        Assert.True(merged.Find(new RecordKey(HouseholdKinds.Playlist, "rainy-day"))!.IsTombstone);
        Assert.NotNull(merged.Live(new RecordKey(UnregisteredKind, "mystery-1")));
        Assert.Contains(UnregisteredKind, HouseholdSerialised(merged), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoInstallsPublishingThroughDriveConverge()
    {
        var drive = new FakeDrive();
        var (a, b) = TwoInstalls();

        var first = await PulpDrive(drive).PublishAsync(a, CancellationToken.None);
        var second = await PulpDrive(drive).PublishAsync(b, CancellationToken.None);
        var afterBoth = await PulpDrive(drive).LoadAsync(CancellationToken.None);

        Assert.True(second.Held.ContentEquals(afterBoth));
        Assert.True(first.Held.ContentEquals(a));
        Assert.True(afterBoth.ContentEquals(HouseholdMerge.Merge(a, b)));
    }

    [Fact]
    public async Task AWrongPassphraseNeverOverwritesTheFile()
    {
        var drive = new FakeDrive { Content = Sealed(TwoInstalls().A) };
        var before = drive.Content;

        var failure = await Assert.ThrowsAsync<HouseholdUnreadableException>(() =>
            PulpDrive(drive, passphrase: "a-different-invented-passphrase").PublishAsync(TwoInstalls().B, CancellationToken.None));

        Assert.Equal(HouseholdUnreadableReason.WrongPassphrase, failure.Reason);
        Assert.Equal(before, drive.Content);
        Assert.Equal(0, drive.Writes);
    }

    [Fact]
    public async Task TheOtherAdultNeverCreatesTheFile()
    {
        var drive = new FakeDrive();

        var result = await PulpDrive(drive, mayCreate: false).PublishAsync(TwoInstalls().A, CancellationToken.None);

        Assert.Equal(HouseholdPublishOutcome.NotYetShared, result.Outcome);
        Assert.Null(drive.Content);
    }

    [Fact]
    public async Task ThePhonesOwnFileKeepsItsFrozenName()
    {
        var directory = Path.Combine(Path.GetTempPath(), "pulp-household-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LocalHouseholdStore(directory, "pulp-household.json");
            await store.SaveAsync(TwoInstalls().A, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(directory, "pulp-household.json")));
            Assert.True((await store.LoadAsync(CancellationToken.None)).ContentEquals(TwoInstalls().A));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string HouseholdSerialised(HouseholdDocument document) => HouseholdDocumentCodec.Serialize(document);

    private sealed class FakeDrive : IDriveTextFile
    {
        public string? Content { get; set; }

        public int Writes { get; private set; }

        public Task<string?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Content);

        public Task WriteAsync(string content, CancellationToken cancellationToken)
        {
            Writes++;
            Content = content;
            return Task.CompletedTask;
        }
    }
}
