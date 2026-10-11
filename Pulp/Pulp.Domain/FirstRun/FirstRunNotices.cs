using Pulp.Domain.Actions;

namespace Pulp.Domain.FirstRun;

/// <summary>
/// First run's own failures, on the dog-ear (D260): the household or the phone's owner not written.
/// No button — the step is still on the page to try again. Never voiced (§14).
/// </summary>
public static class FirstRunNotices
{
    public const string HouseholdKey = "first-run:household";

    public const string PhoneKey = "first-run:phone";

    public static WaitingNotice HouseholdNotSaved(DateTimeOffset at) => new(
        HouseholdKey, "The household was not saved",
        "Pulp could not write the people to this phone's household file. Press Save to try again.",
        NoticeRole.Problem, null, NoticeAction.None, at);

    public static WaitingNotice PhoneNotSaved(DateTimeOffset at) => new(
        PhoneKey, "This phone was not matched",
        "Pulp could not record whose phone this is. Choose again to retry.",
        NoticeRole.Problem, null, NoticeAction.None, at);
}
