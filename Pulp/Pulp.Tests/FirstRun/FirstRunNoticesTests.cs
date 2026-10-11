using Pulp.Domain.Actions;
using Pulp.Domain.FirstRun;

namespace Pulp.Tests.FirstRun;

/// <summary>
/// A first-run write that did not land folds into the dog-ear (never a toast, never voiced): one
/// key per step, so trying again replaces the notice and a success withdraws it.
/// </summary>
public class FirstRunNoticesTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheHouseholdAndThePhoneEachHaveTheirOwnSlot()
    {
        var household = FirstRunNotices.HouseholdNotSaved(At);
        var phone = FirstRunNotices.PhoneNotSaved(At);

        Assert.Equal(FirstRunNotices.HouseholdKey, household.Key);
        Assert.Equal(FirstRunNotices.PhoneKey, phone.Key);
        Assert.NotEqual(household.Key, phone.Key);
        Assert.NotEqual(VaultOutcomes.NoticeKey, household.Key);
    }

    [Fact]
    public void BothAreProblemsWithNoButtonToAWrongPlace()
    {
        var notices = new[] { FirstRunNotices.HouseholdNotSaved(At), FirstRunNotices.PhoneNotSaved(At) };

        Assert.All(notices, notice =>
        {
            Assert.Equal(NoticeRole.Problem, notice.Role);
            Assert.Equal(NoticeAction.None, notice.Action);
            Assert.Null(notice.ActionLabel);
        });
    }
}
