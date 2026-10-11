using Pulp.Domain.FirstRun;
using Pulp.Domain.People;

namespace Pulp.Tests.FirstRun;

/// <summary>
/// First run on the phone the household is shared with (D166): she does not add people — the owner's
/// household arrives through the share — so the household step never asks her, and first run is not
/// marked finished while that household has not come, or the "This phone is" step would never be asked
/// once it does. Invented state.
/// </summary>
public class SharedPhoneFirstRunTests
{
    private static FirstRunState State(bool hasRoster, bool mayAddPeople) => new(
        SignedIn: true, HasPassphrase: true, KeysPresent: FirstRunPlan.KeyCount, hasRoster,
        hasRoster ? ThisPhoneStatus.Unmatched : ThisPhoneStatus.NoRoster, new HashSet<FirstRunStep>(), Finished: false, mayAddPeople);

    [Fact]
    public void TheOwnersPhoneIsAskedForThePeople() =>
        Assert.Equal(FirstRunStep.Household, FirstRunPlan.Next(State(hasRoster: false, mayAddPeople: true)));

    [Fact]
    public void TheSharedPhoneIsNotAskedForThePeople() =>
        Assert.Equal(FirstRunStep.Done, FirstRunPlan.Next(State(hasRoster: false, mayAddPeople: false)));

    [Fact]
    public void TheSharedPhoneIsAskedWhoseItIsOnceTheHouseholdArrives() =>
        Assert.Equal(FirstRunStep.ThisPhone, FirstRunPlan.Next(State(hasRoster: true, mayAddPeople: false)));

    [Fact]
    public void TheSharedPhoneWithNoHouseholdYetIsNotMarkedFinished() =>
        Assert.False(FirstRunPlan.MayFinish(State(hasRoster: false, mayAddPeople: false)));

    [Fact]
    public void EveryOtherStateMayFinish()
    {
        Assert.True(FirstRunPlan.MayFinish(State(hasRoster: true, mayAddPeople: false)));
        Assert.True(FirstRunPlan.MayFinish(State(hasRoster: false, mayAddPeople: true)));
    }

    [Fact]
    public void TheGateDoesNotReopenForASharedPhoneWaitingForItsHousehold() =>
        Assert.False(FirstRunGate.ShouldOpen(State(hasRoster: false, mayAddPeople: false)));
}
