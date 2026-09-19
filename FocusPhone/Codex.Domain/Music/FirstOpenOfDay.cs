namespace Codex.Domain.Music;

/// <summary>
/// Whether today's app open is the first one — the one the nightly rescan's findings ride
/// in on, per the owner's request that they be "the first authorial voice greeting" of the
/// day. Pure date comparison, mirroring <c>FridayPicksSchedule</c>'s own "at most once"
/// gating; <paramref name="lastShown"/> is the local date the greeting last actually
/// displayed something (not merely attempted — a composer that returned nothing showed no
/// greeting to be first).
/// </summary>
public static class FirstOpenOfDay
{
    // Not shown, or last shown strictly before today. A lastShown date at or after today
    // (never shown yet, or a clock rolled back after already recording today's — or a
    // later day's — greeting) both count as "already handled" so a clock glitch can never
    // repeat a greeting that already went out.
    public static bool IsFirst(DateOnly today, DateOnly? lastShown) => lastShown is null || lastShown < today;
}
