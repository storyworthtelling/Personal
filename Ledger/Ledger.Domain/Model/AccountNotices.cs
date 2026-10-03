using System.Globalization;
using Index.Domain.Time;

namespace Index.Domain.Model;

/// <summary>
/// The severity a notice carries — named to mirror <c>Storyworthtelling.Maui.State.DogEarHue</c>
/// one-for-one, so the Android layer's mapping into a real <c>DogEarNotice</c> is a plain lookup
/// rather than a second decision. Kept as its own Domain enum rather than referencing that MAUI
/// type directly: <c>Storyworthtelling.Maui</c> targets <c>net10.0-android</c> only, so
/// <c>Index.Domain</c> (a plain <c>net10.0</c> library, tested from the desktop TFM) cannot
/// reference it at all — this is what keeps Accounts' notice logic unit-testable in
/// <c>Ledger.Tests</c> without the Android workload.
/// </summary>
public enum NoticeSeverity
{
    Problem,
    Caution,
    Action,
    Resolved,

    /// <summary>Slate: something only worth knowing (mockup 17's Trial notice).</summary>
    Reference,

    /// <summary>Teal: waiting, not wrong (mockup 8's unreadable budget box).</summary>
    Progress
}

/// <summary>What a notice's one action does.</summary>
public enum NoticeAction
{
    /// <summary>Open Plaid in update mode against the institution <see cref="NoticeTarget.Id"/> names.</summary>
    Reconnect,

    /// <summary>Turn back to Accounts with the account <see cref="NoticeTarget.Id"/> names open.</summary>
    OpenAccount
}

/// <summary>
/// What a notice's action acts on, by id. The actions used to find their target by matching the
/// notice's headline text, which only worked while the headline began with the bank's name or contained
/// the account's; mockup 17's headlines name the institution, so the id travels with the notice instead.
/// </summary>
public sealed record NoticeTarget(NoticeAction Action, Guid Id);

/// <summary>
/// One thing waiting behind Accounts' dog-ear (mockup 17), before it becomes a real <c>DogEarNotice</c>
/// on the Android side. <paramref name="At"/> is when the cause began — what the Waiting page prints
/// beside the headline and orders by — and <paramref name="Target"/> is what the action acts on; both
/// are optional because Budget's notices carry neither.
/// </summary>
public sealed record AccountNotice(
    string Headline, string Detail, NoticeSeverity Severity, string? ActionLabel,
    DateTimeOffset? At = null, NoticeTarget? Target = null);

/// <summary>
/// D161: Accounts' own notices for the shared Waiting page (S2) — a lost connection, a hand-entered
/// balance over a month old, a refresh that just failed, and how close the household is to Plaid's
/// ten-Item Trial cap. Pure and stateless: <see cref="Build"/> recomputes the whole list from live
/// institution state every call, so there is nothing to mark "seen" and nothing that outlives its own
/// cause — a bank that reconnects, or a manual balance that gets refreshed, simply stops producing a
/// notice the next time this runs (Guard G25, <c>NoticeLifetimeTests</c>). The Android layer calls
/// this on every render rather than caching its result.
/// </summary>
public static class AccountNotices
{
    // Plaid's Trial plan caps a team at ten Items EVER created (D149) — an install nearing that cap
    // needs to know before the eleventh connection is simply refused. Nine is "one away", the mockup's
    // own D161 wording ("how many of Plaid's ten Trial Items are used, from nine connections").
    private const int TrialItemCap = 10;
    private const int TrialItemWarningThreshold = 9;

    // The second half of the Trial notice, verbatim from mockup 17 (its first half carries the
    // typographic apostrophe in "Plaid\u2019s").
    private const string TrialPlanSentence = "An eleventh would need a paid plan, which bills each refresh.";

    public static IReadOnlyList<AccountNotice> Build(
        IReadOnlyList<Institution> institutions, DateTimeOffset now, bool lastRefreshFailed)
    {
        var notices = new List<AccountNotice>();

        foreach (var institution in institutions)
        {
            // A hand-entered institution (empty AccessToken) was never connected at Plaid, so it can
            // never need "reconnecting" — BalanceRefreshProcessor already excludes it from the refresh
            // loop for the same reason, but this guards the notice independently of that.
            if (institution.RefreshStatus == WorkStatus.Failed && !string.IsNullOrEmpty(institution.AccessToken))
            {
                notices.Add(LostConnectionNotice(institution, now));
            }
        }

        foreach (var institution in institutions)
        {
            // A hand-entered account is one Index never has a Plaid access token for — there is
            // nothing at the other end of a refresh to keep it current.
            if (!string.IsNullOrEmpty(institution.AccessToken))
            {
                continue;
            }

            // One notice per bank, about its oldest stale balance: the headline names the bank, so
            // two stale accounts at one bank would otherwise be two identical headlines.
            var oldestStale = institution.Accounts
                .Where(account => account.BalanceAsOfUtc is { } asOf && now > asOf.AddMonths(1))
                .OrderBy(account => account.BalanceAsOfUtc)
                .FirstOrDefault();
            if (oldestStale is not null)
            {
                notices.Add(StaleManualBalanceNotice(institution, oldestStale, now));
            }
        }

        var connected = institutions.Where(i => !string.IsNullOrEmpty(i.AccessToken)).ToList();

        if (lastRefreshFailed)
        {
            notices.Add(FailedRefreshNotice(connected, now));
        }

        if (connected.Count >= TrialItemWarningThreshold)
        {
            notices.Add(TrialLimitNotice(connected));
        }

        return notices;
    }

    private static AccountNotice LostConnectionNotice(Institution institution, DateTimeOffset now)
    {
        // "Its figures are from then": the last good refresh is both the date the connection was lost
        // (as near as Index can know it) and the date the figures are from.
        var detail = institution.LastRefreshedUtc is { } refreshed
            ? $"Plaid lost the connection on {ShortDate(refreshed, now)}, so its figures are from then. Reconnecting needs Admin mode."
            : "Plaid lost the connection before it ever refreshed, so there are no figures yet. Reconnecting needs Admin mode.";

        return new AccountNotice(
            $"{institution.Name} needs signing in",
            detail,
            NoticeSeverity.Problem,
            $"Reconnect {institution.Name}",
            At: institution.LastRefreshedUtc,
            Target: new NoticeTarget(NoticeAction.Reconnect, institution.Id));
    }

    private static AccountNotice StaleManualBalanceNotice(Institution institution, Account account, DateTimeOffset now)
    {
        var asOf = account.BalanceAsOfUtc!.Value;
        // The source decides how the stored date is read: a statement's day is a date (CalendarDate), a typed
        // balance's is an instant (the phone's day). A balance Post set does not claim it was set by hand.
        var source = BalanceDisplay.SourceOf(account);
        var day = BalanceDisplay.ShortDay(BalanceDisplay.Day(asOf, source), now);
        var detail = source == BalanceSource.Post
            ? $"Its balance came from Post on {day}. Put in the current one, or let Post update it from a statement."
            : $"Its balance was set by hand on {day}. Put in the current one, or let Post update it from a statement.";
        return new AccountNotice(
            $"{institution.Name} is a month old",
            detail,
            NoticeSeverity.Caution,
            $"Open {institution.Name}",
            // It went stale the moment it turned a month old, not the day it was typed in.
            At: asOf.AddMonths(1),
            Target: new NoticeTarget(NoticeAction.OpenAccount, account.Id));
    }

    // The mockup's notes quote this as one sentence, "Couldn't reach Plaid. The figures are from 2h
    // ago; it tries again next time you open Index." — split at the full stop into headline and detail.
    private static AccountNotice FailedRefreshNotice(IReadOnlyList<Institution> connected, DateTimeOffset now)
    {
        var lastGood = connected.Max(i => i.LastRefreshedUtc);
        var figures = lastGood is { } at
            ? $"The figures are from {RelativeTime.Format(at.LocalDateTime, now.LocalDateTime)}"
            : "There are no figures yet";

        return new AccountNotice(
            "Couldn't reach Plaid",
            $"{figures}; it tries again next time you open Index.",
            NoticeSeverity.Caution,
            ActionLabel: null,
            At: lastGood);
    }

    private static AccountNotice TrialLimitNotice(IReadOnlyList<Institution> connected)
    {
        // The ninth Item is the one that took the household into the warning; when it was linked is when
        // this notice began. An Item restored from the vault has no creation date, and a notice with no
        // date prints none rather than a made-up one.
        var ninth = connected.OrderBy(i => i.CreatedAtUtc).ElementAt(TrialItemWarningThreshold - 1).CreatedAtUtc;
        var detail = connected.Count < TrialItemCap
            ? "One more bank fits on Plaid\u2019s Trial plan. " + TrialPlanSentence
            : TrialPlanSentence;

        return new AccountNotice(
            $"{connected.Count} of {TrialItemCap} connections used",
            detail,
            NoticeSeverity.Reference,
            ActionLabel: null,
            At: ninth == default ? null : ninth);
    }

    // "Aug 26", or "Aug 26, 2025" from another year — the form the rest of the suite prints a past
    // instant in. A genuine instant, so it goes through local time (CalendarDate is for date-only values).
    private static string ShortDate(DateTimeOffset when, DateTimeOffset now)
    {
        var local = when.LocalDateTime;
        return local.Year == now.LocalDateTime.Year
            ? local.ToString("MMM d", CultureInfo.InvariantCulture)
            : local.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
