using Storyworthtelling.Household;

namespace Storyworthtelling.Household.Tests;

/// <summary>
/// What a phone keeps after a sync pass, and who may create the shared file (finding §2.7). Copied
/// from Pulp's HouseholdSyncTests; the screens' status and notices stay in Pulp.
/// </summary>
public class HouseholdAdoptionTests
{
    private static readonly DateTimeOffset Opened = new(2026, 3, 14, 19, 0, 0, TimeSpan.Zero);

    private static HouseholdRecord Viewer(string id, long wall, string writer) =>
        HouseholdRecord.Write(
            new RecordKey("viewer", id),
            1,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["child"] = "false" },
            new HlcTimestamp(wall, 0),
            writer);

    private static HouseholdDocument Of(params HouseholdRecord[] records) =>
        records.Aggregate(HouseholdDocument.Empty, (document, record) => document.With(record));

    // ---- what the phone keeps ----

    [Fact]
    public void AFirstRunOnAnEmptyPhoneAdoptsWhatDriveHolds()
    {
        var drive = Of(Viewer("robin-example", 10, "install-b-example"), Viewer("avery-example", 20, "install-b-example"));

        var kept = HouseholdSync.Adopt(HouseholdDocument.Empty, drive);

        Assert.True(kept.ContentEquals(drive));
    }

    [Fact]
    public void AnEditMadeWhileThePassWasOnTheNetworkIsKept()
    {
        var before = Of(Viewer("robin-example", 10, "install-a-example"));
        var published = Of(Viewer("robin-example", 10, "install-a-example"), Viewer("avery-example", 20, "install-b-example"));
        var editedDuringThePass = before.With(Viewer("jules-example", 30, "install-a-example"));

        var kept = HouseholdSync.Adopt(editedDuringThePass, published);

        Assert.NotNull(kept.Live(new RecordKey("viewer", "jules-example")));
        Assert.NotNull(kept.Live(new RecordKey("viewer", "avery-example")));
        Assert.NotNull(kept.Live(new RecordKey("viewer", "robin-example")));
    }

    [Fact]
    public void NothingIsWrittenWhenDriveAlreadyHoldsTheMerge()
    {
        var drive = Of(Viewer("robin-example", 10, "install-b-example"));

        Assert.False(HouseholdSync.NeedsWrite(drive, drive));
        Assert.False(HouseholdSync.NeedsWrite(HouseholdDocument.Empty, HouseholdDocument.Empty));
        Assert.True(HouseholdSync.NeedsWrite(drive.With(Viewer("avery-example", 20, "install-a-example")), drive));
    }


    [Fact]
    public void OnlyTheFilesOwnerMayCreateIt()
    {
        Assert.True(HouseholdSync.MayCreateFile(HouseholdFileRole.Owner));
        Assert.False(HouseholdSync.MayCreateFile(HouseholdFileRole.SharedWith));
        Assert.False(HouseholdSync.MayCreateFile(HouseholdFileRole.Unknown));
    }
}
