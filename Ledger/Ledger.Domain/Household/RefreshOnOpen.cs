using Index.Domain.Time;

namespace Index.Domain.Household;

/// <summary>
/// Refresh on open (D149, I5): "No manual refresh anywhere: balances refresh every time Index opens, plus
/// the daily worker, no cool-off." The decisions behind it, with the head's clocks and gates passed in.
///
/// <para>Not a second refresh implementation. The page asks the existing worker to run (its unique-work
/// policy and process-wide cycle gate already refuse a second cycle); this says whether to ask, whether
/// one is in progress, and what the status line says.</para>
/// </summary>
public static class RefreshOnOpen
{
    /// <summary>The teal status sentence under the header while a refresh runs (mockup 10, §6). A single
    /// ellipsis character, as the suite writes it.</summary>
    public const string RefreshingSentence = "Refreshing balances…";

    /// <summary>How long a requested refresh may take to start before the page stops claiming it. The
    /// worker is queued on WorkManager, which waits for a network and may never run; without a limit an
    /// offline phone would say "Refreshing balances…" for the whole poll window about nothing.</summary>
    public static readonly TimeSpan StartGrace = TimeSpan.FromSeconds(20);

    /// <summary>Whether Accounts asks for a refresh on appearing. Only an install that may touch Plaid and
    /// has its keys; never her install, never an unconfigured one, never while one is already going.</summary>
    public static bool ShouldStart(InstallRole role, bool isConfigured, bool refreshActive) =>
        InstallRoleGate.MayRunPlaidWork(role) && isConfigured && !refreshActive;

    /// <summary>
    /// Whether a refresh is in progress as far as the page can tell. A cycle that is running is always
    /// one (the daily worker's included). Otherwise a request made by this page counts until the cycle
    /// has recorded its end (<paramref name="lastRunAtUtc"/> moved past the request), or until
    /// <see cref="StartGrace"/> passes with nothing having started.
    /// </summary>
    public static bool IsActive(DateTime? requestedAtUtc, DateTime nowUtc, bool cycleRunning, DateTime lastRunAtUtc)
    {
        if (cycleRunning)
        {
            return true;
        }

        if (requestedAtUtc is not { } requested)
        {
            return false;
        }

        if (lastRunAtUtc >= requested)
        {
            return false;
        }

        return nowUtc - requested < StartGrace;
    }

    /// <summary>The status line when nothing is running: "Refreshed 2h ago" (mockups 2 and 10), or
    /// "Refreshed just now" once the refresh that was shown has finished. Any other outcome keeps its own
    /// words before the time.</summary>
    public static string Summary(string outcome, DateTime lastRunAtLocal, DateTime nowLocal)
    {
        if (string.IsNullOrEmpty(outcome) || lastRunAtLocal == DateTime.MinValue)
        {
            return "Not refreshed yet";
        }

        var when = RelativeTime.Format(lastRunAtLocal, nowLocal);
        return outcome == "Refreshed" ? $"Refreshed {when}" : $"{outcome} — {when}";
    }
}
