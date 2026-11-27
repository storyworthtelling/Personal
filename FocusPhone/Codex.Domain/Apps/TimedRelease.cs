using System.Globalization;

namespace Codex.Domain.Apps;

/// <summary>
/// The wire contract Pulp asks Flyleaf to honour before it opens a title (the Entertainment block). A
/// streaming service is hard-suspended by the lockdown; a timed release lifts that suspension on the one
/// service the title plays on, for the title's length plus a grace, so the title can play through to
/// its end. Pulp's copy is <c>Pulp.Domain/LockedOpen/TimedReleaseHandOff.cs</c>; the parity guards on both
/// sides read these files by content, so a drift on either side fails there.
///
/// It is an explicit broadcast under the same signature permission as the locked open (Pulp already holds
/// it), and an ordered one, so Flyleaf's answer reaches Pulp before the title is started.
/// </summary>
public static class TimedReleaseHandoff
{
    public const string Package = "com.storyworthtelling.focusphone.launcher";

    public const string Receiver = "com.storyworthtelling.focusphone.launcher.TimedReleaseReceiver";

    public const string Action = "com.storyworthtelling.focusphone.launcher.action.TIMED_RELEASE";

    /// <summary>The same signature permission as the locked open: a sender that may ask for one may ask for both.</summary>
    public const string Permission = LockedOpenHandoff.Permission;

    public const string PackageExtra = "package";

    public const string LinkExtra = "link";

    public const string MinutesExtra = "minutes";

    /// <summary>Flyleaf set the release: <c>Activity.RESULT_OK</c>.</summary>
    public const int ResultReleased = -1;

    /// <summary>Flyleaf refused the release, or could not make it: <c>Activity.RESULT_CANCELED</c>.</summary>
    public const int ResultRefused = 0;

    /// <summary>The preferences file the release windows persist in; a storage key, kept stable.</summary>
    public const string PreferencesName = "timedReleases";

    /// <summary>The longest window Flyleaf will honour: the locked session's own six-hour cap.</summary>
    public const int MaxMinutes = 360;

    public const int MinMinutes = 1;
}

/// <summary>
/// The decisions behind a timed release, in one pure place. <see cref="TimedReleaseGate"/> is what a
/// signed sender may ask for; <see cref="TimedReleases"/> is the window book Flyleaf keeps on the phone.
/// </summary>
public static class TimedReleaseGate
{
    /// <summary>
    /// The same checks as a locked open (owner namespace, streaming catalogue, the service's own link
    /// domain, ticked by the household, not blocked by the owner), without the policy-suspension check:
    /// the release is what lifts that suspension, so asking for it is exactly the case where it holds.
    /// </summary>
    public static LockedOpenRefusal Decide(LockedOpenRequest request, HiddenApps hidden, IReadOnlySet<string> ownerBlocked) =>
        LockedOpenGate.Decide(request, hidden, ownerBlocked, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>Reads the minutes extra; a value outside the honoured range is refused, not clamped.</summary>
    public static bool TryReadMinutes(string? text, out int minutes)
    {
        minutes = 0;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes)
            && minutes is >= TimedReleaseHandoff.MinMinutes and <= TimedReleaseHandoff.MaxMinutes;
    }
}

/// <summary>
/// The release windows Flyleaf keeps: each package it has released, and the Unix millisecond it is
/// suspended again. A window that has passed is simply not active, so a lost alarm cannot keep a service
/// open past its time; the next policy pass re-suspends it.
/// </summary>
public static class TimedReleases
{
    /// <summary>The windows still open at <paramref name="now"/>.</summary>
    public static IReadOnlyDictionary<string, long> Pruned(IReadOnlyDictionary<string, long> windows, DateTimeOffset now)
    {
        var at = now.ToUnixTimeMilliseconds();
        return windows
            .Where(window => window.Value > at)
            .ToDictionary(window => window.Key, window => window.Value, StringComparer.Ordinal);
    }

    /// <summary>The packages whose window is open at <paramref name="now"/>.</summary>
    public static IReadOnlySet<string> Active(IReadOnlyDictionary<string, long> windows, DateTimeOffset now) =>
        Pruned(windows, now).Keys.ToHashSet(StringComparer.Ordinal);

    /// <summary>The book with <paramref name="package"/> released until <paramref name="until"/>.</summary>
    public static IReadOnlyDictionary<string, long> With(IReadOnlyDictionary<string, long> windows, string package, DateTimeOffset until) =>
        new Dictionary<string, long>(windows, StringComparer.Ordinal) { [package] = until.ToUnixTimeMilliseconds() };

    /// <summary>When the next open window closes, or null when none is open.</summary>
    public static DateTimeOffset? NextExpiry(IReadOnlyDictionary<string, long> windows, DateTimeOffset now) =>
        Pruned(windows, now).Values
            .Select(ms => (DateTimeOffset?)DateTimeOffset.FromUnixTimeMilliseconds(ms))
            .Min();

    /// <summary>The window end for a release asked at <paramref name="now"/> for <paramref name="minutes"/>.</summary>
    public static DateTimeOffset Until(DateTimeOffset now, int minutes) => now.AddMinutes(minutes);
}
