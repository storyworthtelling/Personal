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
        string package = Netflix,
        int bootCount = -1) =>
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
            active,
            bootCount);

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
    public void ASecondStartWhileOneIsActiveIsRefusedAndChangesNothing()
    {
        // D300: one session at a time. A second request used to take the new package while keeping the
        // first one freed and restarting the six-hour clock; refusing it leaves the first record whole.
        var first = LockedSessionPlan.Start(Facts(current: ["com.example.kept"], features: 0, suspended: true));
        var second = LockedSessionPlan.Start(Facts(
            current: first.LockTaskPackages,
            features: first.LockTaskFeatures,
            suspended: true,
            active: first.Record,
            package: "com.hulu.plus"));

        Assert.False(second.Allowed);
        Assert.Null(second.UnsuspendPackage);
        Assert.Empty(second.LockTaskPackages);
        Assert.True(first.Record.Unsuspended);
        Assert.Equal(Netflix, first.Record.Package);
    }

    [Fact]
    public void ARepeatRequestForTheRunningServiceIsRefusedToo()
    {
        var first = LockedSessionPlan.Start(Facts());

        Assert.False(LockedSessionPlan.Start(Facts(active: first.Record)).Allowed);
    }

    [Fact]
    public void AStartRecordsTheBootCountAndStartsUnconfirmed()
    {
        var plan = LockedSessionPlan.Start(Facts(bootCount: 12));

        Assert.Equal(12, plan.Record.BootCount);
        Assert.False(plan.Record.Confirmed);
        Assert.False(plan.Record.Advanced);
        Assert.Null(plan.Record.PausedSinceElapsedMs);
    }

    [Fact]
    public void ReleasePutsBackTheAllowlistAndTheFeaturesAndBringsPulpForward()
    {
        var start = LockedSessionPlan.Start(Facts(current: ["com.example.kept"], features: 2, suspended: true));

        var release = LockedSessionPlan.Release(start.Record, LockedReleaseReason.TitleEnded, new HashSet<string> { Netflix });

        Assert.Equal(["com.example.kept"], release.RestoreLockTaskPackages);
        Assert.Equal(2, release.RestoreLockTaskFeatures);
        Assert.Equal(Netflix, release.ResuspendPackage);
        Assert.Equal(PolicyContext.PulpPackage, release.BringForwardPackage);
        Assert.False(release.IsNothing);
    }

    [Fact]
    public void ReleaseResuspendsOnlyWhatThePlanWouldSuspendNow()
    {
        // D301: the service was freed for the session. If a policy pass has legitimately freed it since
        // (or never wanted it suspended), putting it back would undo that pass.
        var start = LockedSessionPlan.Start(Facts(suspended: true));

        Assert.Null(LockedSessionPlan.Release(start.Record, LockedReleaseReason.TitleEnded, new HashSet<string>()).ResuspendPackage);
        Assert.Null(LockedSessionPlan.Release(start.Record, LockedReleaseReason.TitleEnded, new HashSet<string> { "com.hulu.plus" }).ResuspendPackage);
        Assert.Equal(Netflix,
            LockedSessionPlan.Release(start.Record, LockedReleaseReason.TitleEnded, new HashSet<string> { Netflix }).ResuspendPackage);
        Assert.Null(LockedSessionPlan.Release(start.Record, LockedReleaseReason.TitleEnded).ResuspendPackage);
    }

    [Theory]
    [InlineData(null, "com.netflix.mediaclient", false)]
    [InlineData(new string[0], "com.netflix.mediaclient", false)]
    [InlineData(new[] { "com.hulu.plus" }, "com.netflix.mediaclient", false)]
    [InlineData(new[] { "com.netflix.mediaclient" }, "com.netflix.mediaclient", true)]
    public void AFailedListNamingThePackageIsAFailureAndAnyOtherIsNot(string[]? failed, string package, bool failedExpected)
    {
        Assert.Equal(failedExpected, LockedSessionPlan.DidFail(failed, package));
    }

    [Fact]
    public void ReleaseDoesNotResuspendWhatItNeverReleased()
    {
        var start = LockedSessionPlan.Start(Facts(suspended: false));

        Assert.Null(LockedSessionPlan.Release(start.Record, LockedReleaseReason.HomePressed, new HashSet<string> { Netflix }).ResuspendPackage);
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
    [InlineData(LockedReleaseReason.NeverStarted, true)]
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
            with
            {
                Title = "Harbor Lights", LastPositionMs = 61_000, LastDurationMs = 5_400_000, PausedSinceElapsedMs = 181_000,
                Confirmed = true, Advanced = true, BootCount = 9,
            };

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
        Assert.Equal(record.PausedSinceElapsedMs, back.PausedSinceElapsedMs);
        Assert.Equal(record.Confirmed, back.Confirmed);
        Assert.Equal(record.Advanced, back.Advanced);
        Assert.Equal(record.BootCount, back.BootCount);

        Assert.False(LockedSessionCodec.TryDecode(null, out _));
        Assert.False(LockedSessionCodec.TryDecode("", out _));
        Assert.False(LockedSessionCodec.TryDecode("{", out _));
        Assert.False(LockedSessionCodec.TryDecode("[]", out _));
        Assert.False(LockedSessionCodec.TryDecode("{\"package\":\"\"}", out _));
    }

    [Fact]
    public void ARecordWrittenBeforeTheNewFieldsStillReadsWithSafeDefaults()
    {
        // An older record has none of Confirmed, Advanced, BootCount or the elapsed pause: absent reads
        // as unknown boot, not advanced, and not paused. It also carries no freed-set to misread.
        const string old = "{\"Package\":\"com.netflix.mediaclient\",\"StartedAt\":\"2026-03-04T20:00:00+00:00\","
            + "\"StartedElapsedMs\":1000,\"PriorLockTaskPackages\":[\"a.b\"],\"PriorLockTaskFeatures\":0,"
            + "\"Unsuspended\":true,\"Title\":null,\"LastPositionMs\":0,\"LastDurationMs\":0,\"PausedSince\":null}";

        Assert.True(LockedSessionCodec.TryDecode(old, out var record));
        Assert.Equal(-1, record.BootCount);
        Assert.False(record.Advanced);
        Assert.Null(record.PausedSinceElapsedMs);
        Assert.True(record.Unsuspended);
        Assert.True(record.Confirmed, "A record from before the start check must not be timed out as never started.");
    }
}
