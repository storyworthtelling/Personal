using Android.Content;
using Watch.Domain;
using Watch.Domain.Dash;

namespace Dash.Wear;

/// <summary>
/// Gathers the phone's Rolodex for the wrist (P14 B6, W-5.1.2): a page at a time, each asked of Post through the link
/// and waited for within its bound (<see cref="PostLink"/>), by the pure rule that keeps pages of one edition together
/// (<see cref="RolodexGathering"/>). On the pool, never the main thread; it logs the contract and a count only.
/// </summary>
internal static class RolodexCarrier
{
    public static async Task<RolodexFetch> FetchAsync(Context context)
    {
        var fetched = await RolodexGathering.GatherAsync(async ask =>
        {
            var reply = await PostLink.Shared.AskAsync(context, ask);
            return reply.Answer is DashRolodex page ? new RolodexAnswer(page, null) : new RolodexAnswer(null, reply.Trouble ?? DashTrouble.Unreadable);
        });
        if (fetched.Whole is { } whole)
        {
            DashLog.Info(DashLogLines.Outcome(WatchIdentity.DashRolodexPath, DashOutcome.Answered, whole.Contacts.Count));
        }
        return fetched;
    }
}

/// <summary>The Rolodex the wrist last gathered whole, for the process only (<see cref="HeldRolodex"/>).</summary>
internal static class RolodexBook
{
    public static HeldRolodex Held { get; } = new();
}
