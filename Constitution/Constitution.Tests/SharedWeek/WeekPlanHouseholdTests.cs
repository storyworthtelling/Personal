using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Household;
using Constitution.Tests.HouseholdHead;
using Storyworthtelling.Household;
using static Constitution.Tests.SharedWeek.SharedWeekData;

namespace Constitution.Tests.SharedWeek;

/// <summary>The plan going into the household file from the phone that holds the lease, and coming out on the other one. Two phones, one invented Drive file.</summary>
public class WeekPlanHouseholdTests
{
    private static async Task<HeadRig> OwnerAsync(HeadDrive? drive = null)
    {
        var rig = new HeadRig(drive);
        await rig.Service.SyncAsync(CancellationToken.None);
        return rig;
    }

    private static async Task<HeadRig> PartnerAsync(HeadDrive drive)
    {
        var rig = new HeadRig(drive, HouseholdFileRole.SharedWith);
        await rig.Service.SyncAsync(CancellationToken.None);
        return rig;
    }

    private static HouseholdRecord Mine(HouseholdDocument document) =>
        document.Live(WeekPlanMaps.WeekPlan.KeyOf(Plan())) ?? throw new InvalidOperationException("The document holds no week plan.");

    private static WeekPlanSet Plans(HeadRig rig) => Assert.IsType<WeekPlansRead.Held>(new HouseholdWeekPlans(rig.Service).Read()).Set;

    [Fact]
    public async Task APublishedWeekIsInThePhonesCopyAtOnce()
    {
        using var owner = await OwnerAsync();

        var outcome = await new HouseholdWeekPlanPublisher(owner.Service).PublishAsync(Plan(), CancellationToken.None);

        Assert.IsType<WeekPlanPublication.Published>(outcome);
        Assert.Equal(new DateOnly(2026, 10, 25), Assert.Single(Plans(owner).Plans).Plan.WeekStart);
    }

    [Fact]
    public async Task PublishingTheSameWeekAgainWritesNothing()
    {
        using var owner = await OwnerAsync();
        var publisher = new HouseholdWeekPlanPublisher(owner.Service);
        await publisher.PublishAsync(Plan(), CancellationToken.None);
        var stamp = owner.Service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult().MaxStamp;

        var again = await publisher.PublishAsync(Plan(), CancellationToken.None);

        Assert.IsType<WeekPlanPublication.Unchanged>(again);
        Assert.Equal(stamp, owner.Service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult().MaxStamp);
    }

    [Fact]
    public async Task AChangedWeekIsPublishedWithANewerStamp()
    {
        using var owner = await OwnerAsync();
        var publisher = new HouseholdWeekPlanPublisher(owner.Service);
        await publisher.PublishAsync(Plan(), CancellationToken.None);

        var outcome = await publisher.PublishAsync(Plan(["Tuesday dinner"]), CancellationToken.None);

        Assert.IsType<WeekPlanPublication.Published>(outcome);
        Assert.Equal(["Tuesday dinner"], Assert.Single(Plans(owner).Plans).Plan.ExtraLines);
    }

    [Fact]
    public async Task ThePartnerReadsTheWeekAfterTheTwoSync()
    {
        using var owner = await OwnerAsync();
        await new HouseholdWeekPlanPublisher(owner.Service).PublishAsync(Plan(["Tuesday dinner"], Daily()), CancellationToken.None);
        await owner.Service.SyncAsync(CancellationToken.None);
        using var partner = await PartnerAsync(owner.Drive);

        var held = Assert.Single(Plans(partner).Plans);

        Assert.Equal(owner.Service.InstallId, held.Writer);
        Assert.NotEqual(partner.Service.InstallId, held.Writer);
        Assert.Equal(["Tuesday dinner"], held.Plan.ExtraLines);
        Assert.Equal(Constitution.Domain.Household.Records.WeekPlanMaps.WeekPlan.FieldsOf(Plan(["Tuesday dinner"], Daily())), Constitution.Domain.Household.Records.WeekPlanMaps.WeekPlan.FieldsOf(held.Plan));
    }

    [Fact]
    public async Task ThePartnerReadingWritesNothingAndNeverCreatesARecord()
    {
        using var owner = await OwnerAsync();
        await new HouseholdWeekPlanPublisher(owner.Service).PublishAsync(Plan(), CancellationToken.None);
        await owner.Service.SyncAsync(CancellationToken.None);
        using var partner = await PartnerAsync(owner.Drive);
        var before = partner.Service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();

        _ = Plans(partner);

        var after = partner.Service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(before.ContentEquals(after));
        Assert.Equal(owner.Service.InstallId, Mine(after).Writer);
    }

    [Fact]
    public async Task ANewerRecordFromTheLeaseHolderReplacesTheOneThePartnerHolds()
    {
        using var owner = await OwnerAsync();
        var publisher = new HouseholdWeekPlanPublisher(owner.Service);
        await publisher.PublishAsync(Plan(["Old line"]), CancellationToken.None);
        await owner.Service.SyncAsync(CancellationToken.None);
        using var partner = await PartnerAsync(owner.Drive);
        await publisher.PublishAsync(Plan(["New line"]), CancellationToken.None);
        await owner.Service.SyncAsync(CancellationToken.None);

        await partner.Service.SyncAsync(CancellationToken.None);

        Assert.Equal(["New line"], Assert.Single(Plans(partner).Plans).Plan.ExtraLines);
    }

    [Fact]
    public async Task AFileThatWillNotOpenIsNeverWrittenOver()
    {
        using var owner = await OwnerAsync();
        owner.Drive.Content = "this is not a sealed file";
        await owner.Service.SyncAsync(CancellationToken.None);
        var drive = owner.Drive.Content;
        var writes = owner.Drive.Writes;

        var outcome = await new HouseholdWeekPlanPublisher(owner.Service).PublishAsync(Plan(), CancellationToken.None);

        var refused = Assert.IsType<WeekPlanPublication.NotPublished>(outcome);
        Assert.Equal(WeekPlanNotPublishedBecause.FileUnreadable, refused.Because);
        Assert.Equal(drive, owner.Drive.Content);
        Assert.Equal(writes, owner.Drive.Writes);
        Assert.NotEmpty(Assert.IsType<WeekPlansRead.Blocked>(new HouseholdWeekPlans(owner.Service).Read()).Sentence);
    }

    [Fact]
    public async Task AWrongKeyRefusesToPublishAndToRead()
    {
        using var owner = await OwnerAsync();
        await new HouseholdWeekPlanPublisher(owner.Service).PublishAsync(Plan(), CancellationToken.None);
        await owner.Service.SyncAsync(CancellationToken.None);
        owner.HeldPassphrase = HeadRig.OtherKey;
        await owner.Service.SyncAsync(CancellationToken.None);

        var outcome = await new HouseholdWeekPlanPublisher(owner.Service).PublishAsync(Plan(), CancellationToken.None);

        Assert.IsType<WeekPlanPublication.NotPublished>(outcome);
        Assert.IsType<WeekPlansRead.Blocked>(new HouseholdWeekPlans(owner.Service).Read());
    }

    [Fact]
    public async Task ADriveOutageDoesNotStopThePublishToThePhonesCopy()
    {
        using var owner = await OwnerAsync();
        owner.Drive.Failure = new IOException("invented outage");

        var outcome = await new HouseholdWeekPlanPublisher(owner.Service).PublishAsync(Plan(), CancellationToken.None);

        Assert.IsType<WeekPlanPublication.Published>(outcome);
    }

    [Fact]
    public async Task ADamagedLocalCopyIsLeftAloneAndSaysSo()
    {
        using var owner = await OwnerAsync();
        var path = Directory.GetFiles(Path.GetDirectoryName(owner.Install.FilePath)!, "household.json").Single();
        await File.WriteAllTextAsync(path, "{ damaged");

        var outcome = await new HouseholdWeekPlanPublisher(owner.Service).PublishAsync(Plan(), CancellationToken.None);

        var refused = Assert.IsType<WeekPlanPublication.NotPublished>(outcome);
        Assert.Equal(WeekPlanNotPublishedBecause.CopyDamaged, refused.Because);
        Assert.Equal("{ damaged", await File.ReadAllTextAsync(path));
        Assert.IsType<WeekPlansRead.Blocked>(new HouseholdWeekPlans(owner.Service).Read());
    }

    [Fact]
    public async Task ASkippedRecordIsCountedAndNeverPartOfThePlans()
    {
        using var owner = await OwnerAsync();
        await new HouseholdWeekPlanPublisher(owner.Service).PublishAsync(Plan(), CancellationToken.None);
        await owner.Service.EditAsync(
            (document, stamp, writer) => document.With(Mine(document).Edit(new Dictionary<string, string> { ["format"] = "9" }, 1, stamp, writer)),
            CancellationToken.None);

        var set = Plans(owner);

        Assert.Empty(set.Plans);
        Assert.Equal(WeekPlanSkipReason.UnknownVersion, Assert.Single(set.Skipped).Reason);
    }
}
