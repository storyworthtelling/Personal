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
    DateTimeOffset? PausedSince);

/// <summary>The text form of a <see cref="LockedSessionRecord"/>: one JSON object. Garbled is no record.</summary>
public static class LockedSessionCodec
{
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
        DateTimeOffset? PausedSince);

    public static string Encode(LockedSessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(new Wire(
            record.Package, record.StartedAt, record.StartedElapsedMs, [.. record.PriorLockTaskPackages],
            record.PriorLockTaskFeatures, record.Unsuspended, record.Title, record.LastPositionMs,
            record.LastDurationMs, record.PausedSince));
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
                wire.LastDurationMs, wire.PausedSince);
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
    LockedSessionRecord? Active);

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
/// The pure start and release of a locked hand-off (D288, D297), in the style of <see cref="PolicyPlan"/>.
///
/// <para><b>Start only adds.</b> The lock-task allowlist keeps every package it had and gains the
/// service, the launcher (the way out) and the phone app (so an incoming call can still surface over
/// a locked task); the one thing it may un-suspend is the single chosen service, and never one the
/// plan hard-suspends. <b>Release puts back exactly what start replaced</b>, from the record, so it
/// is idempotent: with no record it does nothing, and a second run finds none.</para>
/// </summary>
public static class LockedSessionPlan
{
    public static LockedStartPlan Start(LockedStartFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var inert = new LockedSessionRecord(facts.Package, facts.Now, facts.ElapsedMs, [], 0, false, null, 0, 0, null);
        if (facts.Package.StartsWith(PolicyContext.OwnerPackagePrefix, StringComparison.Ordinal)
            || facts.PolicySuspended.Contains(facts.Package))
        {
            return new LockedStartPlan(false, [], 0, null, inert);
        }

        // A second start over a running one must not mistake our own additions for the originals.
        var priorPackages = facts.Active?.PriorLockTaskPackages ?? facts.CurrentLockTaskPackages;
        var priorFeatures = facts.Active?.PriorLockTaskFeatures ?? facts.CurrentLockTaskFeatures;

        var packages = new List<string>(priorPackages);
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
            [.. priorPackages],
            priorFeatures,
            unsuspend is not null || (facts.Active?.Unsuspended ?? false),
            Title: null,
            LastPositionMs: 0,
            LastDurationMs: 0,
            PausedSince: null);
        return new LockedStartPlan(true, packages, LockTaskFeatureFlags.Session, unsuspend, record);
    }

    public static LockedReleasePlan Release(LockedSessionRecord? record, LockedReleaseReason reason)
    {
        if (record is null)
        {
            return LockedReleasePlan.Nothing;
        }

        return new LockedReleasePlan(
            record.PriorLockTaskPackages,
            record.PriorLockTaskFeatures,
            record.Unsuspended ? record.Package : null,
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

    public static LockedReleaseReason? OnHome(LockedSessionRecord record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (Capped(record, now))
        {
            return LockedReleaseReason.MaxDuration;
        }

        return now - record.StartedAt < HomeGrace ? null : LockedReleaseReason.HomePressed;
    }

    public static LockedReleaseReason? OnTick(LockedSessionRecord record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (Capped(record, now))
        {
            return LockedReleaseReason.MaxDuration;
        }

        return record.PausedSince is { } since && now - since >= LongPause ? LockedReleaseReason.LongPause : null;
    }

    public static LockedReleaseReason? OnProcessStart(LockedSessionRecord record, DateTimeOffset now, long elapsedMs)
    {
        ArgumentNullException.ThrowIfNull(record);
        var rebooted = elapsedMs < record.StartedElapsedMs;
        var clockWentBack = now < record.StartedAt - TimeSpan.FromMinutes(1);
        return rebooted || clockWentBack || Capped(record, now) ? LockedReleaseReason.Stale : null;
    }

    public static LockedVerdict OnReading(LockedSessionRecord record, PlaybackSessionReading reading, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(reading);
        if (!string.Equals(reading.Package, record.Package, StringComparison.Ordinal))
        {
            return new LockedVerdict(record, null, null);
        }

        if (Capped(record, now))
        {
            return new LockedVerdict(record, LockedReleaseReason.MaxDuration, null);
        }

        var settling = now - record.StartedAt < SettleWindow;
        var title = reading.Title?.Trim();
        var known = record.Title;
        var hasTitle = !string.IsNullOrEmpty(title);

        if (hasTitle && known is not null && !SameTitle(known, title!) && !settling)
        {
            var closing = new PlaybackSessionReading(
                record.Package, known, record.LastPositionMs, record.LastDurationMs, PlaybackSessionState.Stopped);
            return new LockedVerdict(record, LockedReleaseReason.TitleChanged, closing);
        }

        if (reading.State == PlaybackSessionState.Stopped)
        {
            return settling
                ? new LockedVerdict(record, null, null)
                : new LockedVerdict(record, LockedReleaseReason.TitleEnded, null);
        }

        var updated = record with
        {
            Title = hasTitle ? title : known,
            LastPositionMs = reading.PositionMs,
            LastDurationMs = reading.DurationMs > 0 ? reading.DurationMs : record.LastDurationMs,
            PausedSince = reading.State == PlaybackSessionState.Paused ? record.PausedSince ?? now : null,
        };
        var release = updated.PausedSince is { } since && now - since >= LongPause ? LockedReleaseReason.LongPause : (LockedReleaseReason?)null;
        return new LockedVerdict(updated, release, null);
    }

    private static bool Capped(LockedSessionRecord record, DateTimeOffset now) => now - record.StartedAt >= MaxDuration;

    private static bool SameTitle(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
