using System.Text.Json;
using Codex.Domain.Apps;

namespace Codex.Domain.Policy;

/// <summary>
/// Android's <c>DevicePolicyManager.LOCK_TASK_FEATURE_*</c> values, named here so the plan is pure.
/// </summary>
public static class LockTaskFeatureFlags
{
    public const int None = 0;
    public const int SystemInfo = 1;
    public const int Notifications = 2;
    public const int Home = 4;
    public const int Overview = 8;
    public const int GlobalActions = 16;
    public const int Keyguard = 32;

    /// <summary>
    /// Exactly what a locked hand-off turns on: Home, because Home is the way out and Flyleaf is Home;
    /// and the keyguard, because without it the platform shows no lock screen while a task is
    /// locked and a screen that times out mid-film would wake onto the app. Everything else stays at
    /// the lockdown's own posture (notification alerts are already off with the status bar).
    /// </summary>
    public const int Session = Home | Keyguard;
}

/// <summary>Why a locked hand-off ended.</summary>
public enum LockedReleaseReason
{
    HomePressed,
    LongPause,
    TitleEnded,
    TitleChanged,
    MaxDuration,

    /// <summary>The service's media session never appeared within <see cref="LockedSessionRules.StartTimeout"/> of the start (D301).</summary>
    NeverStarted,

    /// <summary>Found at start-up with nobody able to still be using it (a reboot, the cap, a clock jump).</summary>
    Stale,
}

/// <summary>
/// The persisted fact that a locked hand-off is running, written before anything is changed so that
/// a Flyleaf that dies mid-way can still undo it. It carries what the start replaced (the lock-task
/// allowlist and features as they were) so a release puts back exactly that, and the little the
/// rules need to know about the playing title.
/// </summary>
public sealed record LockedSessionRecord(
    string Package,
    DateTimeOffset StartedAt,
    long StartedElapsedMs,
    IReadOnlyList<string> PriorLockTaskPackages,
    int PriorLockTaskFeatures,
    bool Unsuspended,
    string? Title,
    long LastPositionMs,
    long LastDurationMs,
    long? PausedSinceElapsedMs,
    bool Confirmed = false,
    bool Advanced = false,
    int BootCount = -1);

/// <summary>The text form of a <see cref="LockedSessionRecord"/>: one JSON object. Garbled is no record.</summary>
public static class LockedSessionCodec
{
    // Fields added after the first release are nullable here so a record an older Flyleaf wrote still
    // reads: absent Confirmed is true (it predates the start check and must not be timed out), absent
    // Advanced is false, absent BootCount is unknown. The old wall-clock PausedSince is dropped.
    private sealed record Wire(
        string? Package,
        DateTimeOffset StartedAt,
        long StartedElapsedMs,
        List<string>? PriorLockTaskPackages,
        int PriorLockTaskFeatures,
        bool Unsuspended,
        string? Title,
        long LastPositionMs,
        long LastDurationMs,
        long? PausedSinceElapsedMs,
        bool? Confirmed,
        bool? Advanced,
        int? BootCount);

    public static string Encode(LockedSessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(new Wire(
            record.Package, record.StartedAt, record.StartedElapsedMs, [.. record.PriorLockTaskPackages],
            record.PriorLockTaskFeatures, record.Unsuspended, record.Title, record.LastPositionMs,
            record.LastDurationMs, record.PausedSinceElapsedMs, record.Confirmed, record.Advanced, record.BootCount));
    }

    public static bool TryDecode(string? text, out LockedSessionRecord record)
    {
        record = Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            var wire = JsonSerializer.Deserialize<Wire>(text);
            if (wire is null || string.IsNullOrWhiteSpace(wire.Package))
            {
                return false;
            }

            record = new LockedSessionRecord(
                wire.Package, wire.StartedAt, wire.StartedElapsedMs, wire.PriorLockTaskPackages ?? [],
                wire.PriorLockTaskFeatures, wire.Unsuspended, wire.Title, wire.LastPositionMs,
                wire.LastDurationMs, wire.PausedSinceElapsedMs, wire.Confirmed ?? true, wire.Advanced ?? false,
                wire.BootCount ?? -1);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static readonly LockedSessionRecord Empty = new(
        string.Empty, DateTimeOffset.MinValue, 0, [], 0, false, null, 0, 0, null);
}

/// <summary>What the device reports when a start is decided.</summary>
public sealed record LockedStartFacts(
    string Package,
    IReadOnlyList<string> CurrentLockTaskPackages,
    int CurrentLockTaskFeatures,
    bool ServiceSuspendedNow,
    IReadOnlySet<string> PolicySuspended,
    IReadOnlyList<string> DialerPackages,
    string OwnPackage,
    DateTimeOffset Now,
    long ElapsedMs,
    LockedSessionRecord? Active,
    int BootCount = -1);

/// <summary>
/// The start of a locked hand-off, as data. <see cref="Allowed"/> false means do nothing at all.
/// Otherwise the head persists <see cref="Record"/> first, then applies the rest.
/// </summary>
public sealed record LockedStartPlan(
    bool Allowed,
    IReadOnlyList<string> LockTaskPackages,
    int LockTaskFeatures,
    string? UnsuspendPackage,
    LockedSessionRecord Record);

/// <summary>The end of a locked hand-off, as data. <see cref="IsNothing"/> is the no-record case.</summary>
public sealed record LockedReleasePlan(
    IReadOnlyList<string>? RestoreLockTaskPackages,
    int RestoreLockTaskFeatures,
    string? ResuspendPackage,
    string? BringForwardPackage)
{
    public static LockedReleasePlan Nothing { get; } = new(null, 0, null, null);

    public bool IsNothing => RestoreLockTaskPackages is null;
}

/// <summary>
/// The pure start and release of a locked hand-off (D288, D297, D300, D301), in the style of <see cref="PolicyPlan"/>.
///
/// <para><b>Start only adds.</b> The lock-task allowlist keeps every package it had and gains the
/// service, the launcher (the way out) and the phone app (so an incoming call can still surface over
/// a locked task); the one thing it may un-suspend is the single chosen service, and never one the
/// plan hard-suspends. <b>Release puts back exactly what start replaced</b>, from the record, so it
/// is idempotent: with no record it does nothing, and a second run finds none.</para>
/// </summary>
public static class LockedSessionPlan
{
    /// <summary>
    /// True when <c>SetPackagesSuspended</c>'s returned list (the packages it could not change) names the
    /// package. A null or empty list is success (D301).
    /// </summary>
    public static bool DidFail(IEnumerable<string>? failedList, string package) =>
        failedList is not null && failedList.Contains(package, StringComparer.Ordinal);

    public static LockedStartPlan Start(LockedStartFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var inert = new LockedSessionRecord(facts.Package, facts.Now, facts.ElapsedMs, [], 0, false, null, 0, 0, null);

        // D300: one session at a time. A second request over a live one is refused outright. Merging it
        // left the first service freed while the record named the second, and restarted the six-hour clock.
        if (facts.Active is not null
            || facts.Package.StartsWith(PolicyContext.OwnerPackagePrefix, StringComparison.Ordinal)
            || facts.PolicySuspended.Contains(facts.Package))
        {
            return new LockedStartPlan(false, [], 0, null, inert);
        }

        var packages = new List<string>(facts.CurrentLockTaskPackages);
        foreach (var added in new[] { facts.Package, facts.OwnPackage }.Concat(facts.DialerPackages))
        {
            if (!string.IsNullOrWhiteSpace(added) && !packages.Contains(added, StringComparer.Ordinal))
            {
                packages.Add(added);
            }
        }

        var unsuspend = facts.ServiceSuspendedNow ? facts.Package : null;
        var record = new LockedSessionRecord(
            facts.Package,
            facts.Now,
            facts.ElapsedMs,
            [.. facts.CurrentLockTaskPackages],
            facts.CurrentLockTaskFeatures,
            unsuspend is not null,
            Title: null,
            LastPositionMs: 0,
            LastDurationMs: 0,
            PausedSinceElapsedMs: null,
            Confirmed: false,
            Advanced: false,
            BootCount: facts.BootCount);
        return new LockedStartPlan(true, packages, LockTaskFeatureFlags.Session, unsuspend, record);
    }

    /// <summary>
    /// Release puts back the allowlist and features from the record. The service is suspended again only
    /// if it was freed for the session <i>and</i> the plan would suspend it right now
    /// (<paramref name="policySuspendedNow"/>): a policy pass that legitimately freed it mid-session, or
    /// never wanted it suspended, is not undone (D301).
    /// </summary>
    public static LockedReleasePlan Release(
        LockedSessionRecord? record, LockedReleaseReason reason, IReadOnlySet<string>? policySuspendedNow = null)
    {
        if (record is null)
        {
            return LockedReleasePlan.Nothing;
        }

        return new LockedReleasePlan(
            record.PriorLockTaskPackages,
            record.PriorLockTaskFeatures,
            record.Unsuspended && policySuspendedNow is not null && policySuspendedNow.Contains(record.Package)
                ? record.Package
                : null,
            reason == LockedReleaseReason.Stale ? null : PolicyContext.PulpPackage);
    }
}

/// <summary>The rules' answer to a playback reading: the record as it now stands, and whether it ends.</summary>
public sealed record LockedVerdict(
    LockedSessionRecord Record,
    LockedReleaseReason? Release,
    PlaybackSessionReading? Closing);

/// <summary>
/// When a locked hand-off ends (D288, D297). Pure, with the time passed in. Every number is
/// <b>un-evidenced</b> until a device has run one (finding section 4): the values to tune.
/// </summary>
public static class LockedSessionRules
{
    /// <summary>The hard cap: nothing stays locked longer, whatever else happens.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(6);

    /// <summary>Paused this long and the film is not being watched: let the phone go.</summary>
    public static readonly TimeSpan LongPause = TimeSpan.FromMinutes(30);

    /// <summary>A Home resume this soon after the start is the start settling, not a press.</summary>
    public static readonly TimeSpan HomeGrace = TimeSpan.FromSeconds(3);

    /// <summary>
    /// For this long after the start the app is still replacing the session it had before: an old
    /// title or a close that arrives then is that, and only sets the baseline.
    /// </summary>
    public static readonly TimeSpan SettleWindow = TimeSpan.FromSeconds(20);

    /// <summary>How often the head checks the clock-driven rules.</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(5);

    /// <summary>
    /// A session whose target app has shown no media session this long after the start never opened
    /// (D301): the platform refused the launch, or the app is not playing. Un-evidenced like the rest.
    /// </summary>
    public static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan StartCheckSlack = TimeSpan.FromSeconds(5);

    // How far the wall clock and the monotonic clock may disagree before a restart reads as a reboot
    // or a clock change. Alarms and sleep drift is far below this.
    private static readonly TimeSpan ClockSlack = TimeSpan.FromMinutes(1);

    // Every running rule takes the elapsed-realtime reading: a wall-clock change (a user, a time sync, a
    // timezone) can neither end a lock early nor extend it past its cap (D301).
    public static LockedReleaseReason? OnHome(LockedSessionRecord record, long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (Capped(record, elapsedMs))
        {
            return LockedReleaseReason.MaxDuration;
        }

        return Since(record, elapsedMs) < HomeGrace ? null : LockedReleaseReason.HomePressed;
    }

    public static LockedReleaseReason? OnTick(LockedSessionRecord record, long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (Capped(record, elapsedMs))
        {
            return LockedReleaseReason.MaxDuration;
        }

        if (!record.Confirmed && Since(record, elapsedMs) >= StartTimeout)
        {
            return LockedReleaseReason.NeverStarted;
        }

        return LongPaused(record, elapsedMs) ? LockedReleaseReason.LongPause : null;
    }

    /// <summary>How long until the head should next look: soon while the start is unconfirmed, else the tick interval.</summary>
    public static TimeSpan NextTick(LockedSessionRecord record, long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Confirmed)
        {
            return TickInterval;
        }

        var remaining = StartTimeout + StartCheckSlack - Since(record, elapsedMs);
        return remaining > TimeSpan.Zero ? remaining : StartCheckSlack;
    }

    /// <summary>
    /// The only rule that reads the wall clock: it asks whether the wall time since the start agrees with
    /// the monotonic time since the start. They agree across sleep and alarms; they part across a reboot
    /// (the monotonic clock restarted, which also catches a start within seconds of boot, where
    /// "elapsed is smaller than before" is not true) and across a clock change. The boot count, when both
    /// sides know it, is a second witness to a reboot.
    /// </summary>
    public static LockedReleaseReason? OnProcessStart(
        LockedSessionRecord record, DateTimeOffset now, long elapsedMs, int bootCount = -1)
    {
        ArgumentNullException.ThrowIfNull(record);
        var elapsedSince = TimeSpan.FromMilliseconds(elapsedMs - record.StartedElapsedMs);
        var wallSince = now - record.StartedAt;
        var rebooted = elapsedMs < record.StartedElapsedMs
            || (record.BootCount >= 0 && bootCount >= 0 && bootCount != record.BootCount);
        var clocksDisagree = (wallSince - elapsedSince).Duration() > ClockSlack;
        return rebooted || clocksDisagree || Capped(record, elapsedMs) ? LockedReleaseReason.Stale : null;
    }

    public static LockedVerdict OnReading(LockedSessionRecord record, PlaybackSessionReading reading, long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(reading);
        if (!string.Equals(reading.Package, record.Package, StringComparison.Ordinal))
        {
            return new LockedVerdict(record, null, null);
        }

        if (Capped(record, elapsedMs))
        {
            return new LockedVerdict(record, LockedReleaseReason.MaxDuration, null);
        }

        // Any reading from the target is the media session appearing: the start is confirmed.
        var confirmed = record with { Confirmed = true };
        var settling = Since(record, elapsedMs) < SettleWindow;
        var title = reading.Title?.Trim();
        var known = record.Title;
        var hasTitle = !string.IsNullOrEmpty(title);
        var changed = hasTitle && known is not null && !SameTitle(known, title!);

        if (changed && !settling)
        {
            // Close the old title only if it was seen to play on (D301): a baseline taken from a stale
            // session never moved, and a synthetic Stopped for it would be a false finish.
            var closing = record.Advanced
                ? new PlaybackSessionReading(
                    record.Package, known, record.LastPositionMs, record.LastDurationMs, PlaybackSessionState.Stopped)
                : null;
            return new LockedVerdict(confirmed, LockedReleaseReason.TitleChanged, closing);
        }

        if (reading.State == PlaybackSessionState.Stopped)
        {
            return settling
                ? new LockedVerdict(confirmed, null, null)
                : new LockedVerdict(confirmed, LockedReleaseReason.TitleEnded, null);
        }

        // A new baseline (the first title, or the real one replacing a stale one while settling) starts
        // from nothing; the same title moving forward is the proof it is playing.
        var rebaselined = changed;
        var advanced = !rebaselined
            && (record.Advanced || (known is not null && hasTitle && reading.PositionMs > record.LastPositionMs));
        var updated = confirmed with
        {
            Title = hasTitle ? title : known,
            LastPositionMs = reading.PositionMs,
            LastDurationMs = reading.DurationMs > 0 ? reading.DurationMs : record.LastDurationMs,
            PausedSinceElapsedMs = reading.State == PlaybackSessionState.Paused ? record.PausedSinceElapsedMs ?? elapsedMs : null,
            Advanced = advanced,
        };
        return new LockedVerdict(updated, LongPaused(updated, elapsedMs) ? LockedReleaseReason.LongPause : null, null);
    }

    private static TimeSpan Since(LockedSessionRecord record, long elapsedMs) =>
        TimeSpan.FromMilliseconds(elapsedMs - record.StartedElapsedMs);

    private static bool Capped(LockedSessionRecord record, long elapsedMs) => Since(record, elapsedMs) >= MaxDuration;

    private static bool LongPaused(LockedSessionRecord record, long elapsedMs) =>
        record.PausedSinceElapsedMs is { } since && TimeSpan.FromMilliseconds(elapsedMs - since) >= LongPause;

    private static bool SameTitle(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
