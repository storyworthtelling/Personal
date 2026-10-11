using Pulp.Domain.Core;
using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Library;
using Pulp.Domain.Usage;
using static Pulp.Tests.Household.Records.RecordsFixtures;

namespace Pulp.Tests.Usage;

/// <summary>
/// Which recorded hand-offs the usage count is tallied from (G16 wiring). The household keeps a hand-off
/// as each watching viewer's own sitting, so the usage entries are read back off those sittings and a
/// hand-off for two children is still one opening of the service.
/// </summary>
public class UsageHandOffsTests
{
    private static readonly ServiceId Flix = new("example-flix");
    private static readonly ServiceId Sample = new("sample-stream");
    private static readonly TitleId Lantern = new("title-lantern-cove");
    private static readonly DateTimeOffset First = new(2026, 3, 6, 19, 30, 0, TimeSpan.FromHours(-5));
    private static readonly DateTimeOffset Second = First.AddDays(1);

    private static WatchedSitting Sitting(string viewer, DateTimeOffset at, ServiceId? service, WatchSource evidence = WatchSource.HandOff) =>
        new(new ViewerId(viewer), new WatchEvent(Lantern, WatchedKind.Show, at, service, null, evidence));

    [Fact]
    public void EachHandOffSittingBecomesOneUsageEntry()
    {
        var entries = UsageHandOffs.From([Sitting("adult-a", First, Flix), Sitting("adult-a", Second, Sample)]);

        Assert.Equal([new UsageHandOff(Flix, First), new UsageHandOff(Sample, Second)], entries);
    }

    [Fact]
    public void OneHandOffForTwoChildrenIsCountedOnce()
    {
        var entries = UsageHandOffs.From([Sitting("child-wren", First, Flix), Sitting("child-moss", First, Flix)]);

        Assert.Single(entries);
    }

    [Fact]
    public void SessionsMarksAndServicelessSittingsAreNotOpenings()
    {
        var entries = UsageHandOffs.From(
        [
            Sitting("adult-a", First, Flix, WatchSource.Session),
            Sitting("adult-a", First, Flix, WatchSource.MarkedWatched),
            Sitting("adult-a", First, null),
        ]);

        Assert.Empty(entries);
    }

    [Fact]
    public void TheHouseholdViewHandsOverEveryonesSittings()
    {
        var one = Sitting("adult-a", First, Flix);
        var two = Sitting("child-wren", Second, Sample);
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.Watched, one, Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.Watched, two, Next(), PhoneA);

        var all = HouseholdView.Of(document).AllSittings;

        Assert.Equal(2, all.Count);
        Assert.Contains(one, all);
        Assert.Contains(two, all);
    }

    [Fact]
    public void TwoViewersAMillisecondApartAreStillOneOpening()
    {
        var entries = UsageHandOffs.From([Sitting("child-wren", First, Flix), Sitting("child-moss", First.AddMilliseconds(1), Flix)]);

        Assert.Single(entries);
    }

    [Fact]
    public void OpeningsMinutesApartAreTwoOpenings()
    {
        var entries = UsageHandOffs.From([Sitting("adult-a", First, Flix), Sitting("adult-a", First.AddMinutes(3), Flix)]);

        Assert.Equal(2, entries.Count);
    }
}
