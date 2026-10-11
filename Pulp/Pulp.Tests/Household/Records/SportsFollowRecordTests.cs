using Pulp.Domain.Household;
using Pulp.Domain.Household.Records;
using Pulp.Domain.Sports;
using static Pulp.Tests.Household.Records.RecordsFixtures;

namespace Pulp.Tests.Household.Records;

/// <summary>A team an adult follows (D60: per adult) as a household record; unfollowing is its tombstone.</summary>
public class SportsFollowRecordTests
{
    private static readonly SportsFollow RobinsGulls = new(Robin, new TeamId("harbor-gulls"));

    [Fact]
    public void AFollowRoundTrips()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.SportsFollow, RobinsGulls, Next(), PhoneA);

        var read = HouseholdRecords.Read(document, HouseholdMaps.SportsFollow);

        Assert.Empty(read.Unreadable);
        Assert.Equal(RobinsGulls, Assert.Single(read.Items));
    }

    [Fact]
    public void TheKindIsTheFrozenWord()
    {
        Assert.Equal("sports-follow", HouseholdKinds.SportsFollow);
        Assert.Equal("sports-follow", HouseholdMaps.SportsFollow.Kind);
    }

    [Fact]
    public void TwoAdultsFollowingTheSameTeamAreTwoRecordsAndOneAdultTwiceIsOne()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.SportsFollow, RobinsGulls, Next(), PhoneA);
        document = HouseholdRecords.Put(document, HouseholdMaps.SportsFollow, new SportsFollow(Avery, new TeamId("harbor-gulls")), Next(), PhoneB);
        document = HouseholdRecords.Put(document, HouseholdMaps.SportsFollow, new SportsFollow(Robin, new TeamId("HARBOR-GULLS")), Next(), PhoneA);

        var view = HouseholdView.Of(document);

        Assert.Equal(2, view.SportsFollows.Count);
        Assert.Equal([new TeamId("harbor-gulls")], view.FollowsOf(Robin).Select(follow => follow.Team));
        Assert.Empty(view.FollowsOf(Pip));
    }

    [Fact]
    public void UnfollowingLeavesATombstoneAndTheFollowIsGone()
    {
        var document = HouseholdRecords.Put(HouseholdDocument.Empty, HouseholdMaps.SportsFollow, RobinsGulls, Next(), PhoneA);
        document = HouseholdRecords.Remove(document, HouseholdMaps.SportsFollow, RobinsGulls, Next(), PhoneA);

        Assert.Empty(HouseholdView.Of(document).SportsFollows);
    }
}
