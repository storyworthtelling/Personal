using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Policy;

/// <summary>
/// The pure start and release of a locked hand-off (D288, D297). Start only ever adds to the lock
/// task allowlist and records what it replaced; release puts back exactly that, and is safe to run
/// twice or after a crash.
/// </summary>
public class LockedSessionPlanTests
{
    private const string Netflix = "com.netflix.mediaclient";
    private const string Flyleaf = "com.storyworthtelling.focusphone.launcher";
    private const string Dialer = "com.example.dialer";
    private static readonly DateTimeOffset At = new(2026, 3, 4, 20, 0, 0, TimeSpan.Zero);
    private static readonly Uri Link = new("https://www.example.com/title/harbor-lights");

    private static LockedStartFacts Facts(
        IReadOnlyList<string>? current = null,
        int features = 0,
        bool suspended = false,
        IEnumerable<string>? policySuspended = null,
        LockedSessionRecord? active = null,
        string package = Netflix) =>
        new(
            package,
            current ?? [],
            features,
            suspended,
            new HashSet<string>(policySuspended ?? []),
            [Dialer],
            Flyleaf,
            At,
            1_000,
            active);

    [Fact]
    public void StartKeepsTheExistingAllowlistAndAddsTheServiceTheLauncherAndTheDialer()
    {
        var plan = LockedSessionPlan.Start(Facts(current: ["com.example.kept"]));

        Assert.True(plan.Allowed);
        Assert.Equal(["com.example.kept", Netflix, Flyleaf, Dialer], plan.LockTaskPackages);
    }

    [Fact]
    public void StartDoesNotRepeatAPackageAlreadyAllowed()
    {
        var plan = LockedSessionPlan.Start(Facts(current: [Flyleaf, Netflix]));

        Assert.Equal([Flyleaf, Netflix, Dialer], plan.LockTaskPackages);
    }

    [Fact]
    public void TheFeaturesAreHomeAndKeyguardAndNothingElse()
    {
        var plan = LockedSessionPlan.Start(Facts());

        // Home is the way out; the keyguard stays on so a lock-task session never opens a lock screen
        // onto the app. No notifications, no overview, no power menu, no system info: the lockdown's
        // own posture, not a relaxation of it.
        Assert.Equal(LockTaskFeatureFlags.Home | LockTaskFeatureFlags.Keyguard, plan.LockTaskFeatures);
        Assert.Equal(0, plan.LockTaskFeatures & (LockTaskFeatureFlags.Notifications | LockTaskFeatureFlags.Overview
            | LockTaskFeatureFlags.GlobalActions | LockTaskFeatureFlags.SystemInfo));
    }

    [Fact]
    public void TheFlagValuesAreAndroids()
    {
        Assert.Equal(1, LockTaskFeatureFlags.SystemInfo);
        Assert.Equal(2, LockTaskFeatureFlags.Notifications);
        Assert.Equal(4, LockTaskFeatureFlags.Home);
        Assert.Equal(8, LockTaskFeatureFlags.Overview);
        Assert.Equal(16, LockTaskFeatureFlags.GlobalActions);
        Assert.Equal(32, LockTaskFeatureFlags.Keyguard);
    }

    [Fact]
    public void StartRecordsWhatItReplacedSoReleaseCanPutItBack()
    {
        var plan = LockedSessionPlan.Start(Facts(current: ["com.example.kept"], features: 2));

        Assert.Equal(Netflix, plan.Record.Package);
        Assert.Equal(["com.example.kept"], plan.Record.PriorLockTaskPackages);
        Assert.Equal(2, plan.Record.PriorLockTaskFeatures);
        Assert.Equal(At, plan.Record.StartedAt);
        Assert.Equal(1_000, plan.Record.StartedElapsedMs);
    }

    [Fact]
    public void ARunnableServiceIsLeftAloneAndASuspendedOneIsReleasedForTheSessionOnly()
    {
        Assert.Null(LockedSessionPlan.Start(Facts(suspended: false)).UnsuspendPackage);
        Assert.False(LockedSessionPlan.Start(Facts(suspended: false)).Record.Unsuspended);

        var released = LockedSessionPlan.Start(Facts(suspended: true));
        Assert.Equal(Netflix, released.UnsuspendPackage);
        Assert.True(released.Record.Unsuspended);
    }

    [Fact]
    public void APackageThePolicyHardSuspendsIsRefusedNotReleased()
    {
        var plan = LockedSessionPlan.Start(Facts(suspended: true, policySuspended: [Netflix]));

        Assert.False(plan.Allowed);
        Assert.Null(plan.UnsuspendPackage);
        Assert.Empty(plan.LockTaskPackages);
    }

    [Fact]
    public void AnOwnerPackageIsRefusedEvenHere()
    {
        var plan = LockedSessionPlan.Start(Facts(package: "com.storyworthtelling.focusphone.communication"));

        Assert.False(plan.Allowed);
    }

    [Fact]
    public void AStartOverAnActiveSessionKeepsTheOriginalPriorsNotOurOwnAdditions()
    {
        var first = LockedSessionPlan.Start(Facts(current: ["com.example.kept"], features: 0, suspended: true));
        // The device now reports what the first start set.
        var second = LockedSessionPlan.Start(Facts(
            current: first.LockTaskPackages,
            features: first.LockTaskFeatures,
            suspended: false,
            active: first.Record,
            package: "com.hulu.plus"));

        Assert.Equal(["com.example.kept"], second.Record.PriorLockTaskPackages);
        Assert.Equal(0, second.Record.PriorLockTaskFeatures);
        Assert.Equal("com.hulu.plus", second.Record.Package);
        Assert.True(first.Record.Unsuspended);
    }

    [Fact]
    public void ReleasePutsBackTheAllowlistAndTheFeaturesAndBringsPulpForward()
    {
        var start = LockedSessionPlan.Start(Facts(current: ["com.example.kept"], features: 2, suspended: true));

        var release = LockedSessionPlan.Release(start.Record, LockedReleaseReason.TitleEnded);

        Assert.Equal(["com.example.kept"], release.RestoreLockTaskPackages);
        Assert.Equal(2, release.RestoreLockTaskFeatures);
        Assert.Equal(Netflix, release.ResuspendPackage);
        Assert.Equal(PolicyContext.PulpPackage, release.BringForwardPackage);
        Assert.False(release.IsNothing);
    }

    [Fact]
    public void ReleaseDoesNotResuspendWhatItNeverReleased()
    {
        var start = LockedSessionPlan.Start(Facts(suspended: false));

        Assert.Null(LockedSessionPlan.Release(start.Record, LockedReleaseReason.HomePressed).ResuspendPackage);
    }

    [Fact]
    public void ReleaseWithNoRecordDoesNothingSoItIsSafeToRunTwice()
    {
        var release = LockedSessionPlan.Release(null, LockedReleaseReason.HomePressed);

        Assert.True(release.IsNothing);
        Assert.Null(release.RestoreLockTaskPackages);
        Assert.Null(release.ResuspendPackage);
        Assert.Null(release.BringForwardPackage);
    }

    [Theory]
    [InlineData(LockedReleaseReason.HomePressed, true)]
    [InlineData(LockedReleaseReason.LongPause, true)]
    [InlineData(LockedReleaseReason.TitleEnded, true)]
    [InlineData(LockedReleaseReason.TitleChanged, true)]
    [InlineData(LockedReleaseReason.MaxDuration, true)]
    [InlineData(LockedReleaseReason.Stale, false)]
    public void OnlyACleanupOfAStaleLockLeavesPulpWhereItWas(LockedReleaseReason reason, bool bringsForward)
    {
        var start = LockedSessionPlan.Start(Facts());

        Assert.Equal(bringsForward, LockedSessionPlan.Release(start.Record, reason).BringForwardPackage is not null);
    }

    [Fact]
    public void TheRecordSurvivesTheStoreAndAnUnreadableOneIsNoRecord()
    {
        var record = LockedSessionPlan.Start(Facts(current: ["com.example.kept"], features: 2, suspended: true)).Record
            with { Title = "Harbor Lights", LastPositionMs = 61_000, LastDurationMs = 5_400_000, PausedSince = At.AddMinutes(3) };

        Assert.True(LockedSessionCodec.TryDecode(LockedSessionCodec.Encode(record), out var back));
        Assert.Equal(record.Package, back.Package);
        Assert.Equal(record.StartedAt, back.StartedAt);
        Assert.Equal(record.StartedElapsedMs, back.StartedElapsedMs);
        Assert.Equal(record.PriorLockTaskPackages, back.PriorLockTaskPackages);
        Assert.Equal(record.PriorLockTaskFeatures, back.PriorLockTaskFeatures);
        Assert.Equal(record.Unsuspended, back.Unsuspended);
        Assert.Equal(record.Title, back.Title);
        Assert.Equal(record.LastPositionMs, back.LastPositionMs);
        Assert.Equal(record.LastDurationMs, back.LastDurationMs);
        Assert.Equal(record.PausedSince, back.PausedSince);

        Assert.False(LockedSessionCodec.TryDecode(null, out _));
        Assert.False(LockedSessionCodec.TryDecode("", out _));
        Assert.False(LockedSessionCodec.TryDecode("{", out _));
        Assert.False(LockedSessionCodec.TryDecode("[]", out _));
        Assert.False(LockedSessionCodec.TryDecode("{\"package\":\"\"}", out _));
    }
}
