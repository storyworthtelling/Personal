using System.Text.RegularExpressions;
using Codex.Domain.Apps;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Guard G19, Flyleaf's half: the contract Pulp asks for a locked hand-off over (P12 Release G, D288,
/// D296, D297). The expected values are typed out here on purpose, because the guard exists to notice
/// them moving; Pulp's own <c>LockedOpenContractTests</c> reads <c>LockedOpen.cs</c> by content.
/// Nothing fails at run time if they drift: the broadcast is dropped, Pulp's title never opens, and
/// the dog-ear says only that it may not have. The second half of the file pins what the receiver and
/// the controller are allowed to touch, because a signed sender must not be able to lock-task an
/// arbitrary app and a release must never be left half-done.
/// </summary>
public class LockedOpenContractTests
{
    [Fact]
    public void TheWireNamesAreFrozen()
    {
        Assert.Equal("com.storyworthtelling.focusphone.launcher", LockedOpenHandoff.Package);
        Assert.Equal("com.storyworthtelling.focusphone.launcher.LockedOpenReceiver", LockedOpenHandoff.Receiver);
        Assert.Equal("com.storyworthtelling.focusphone.launcher.action.LOCKED_OPEN", LockedOpenHandoff.Action);
        Assert.Equal("com.storyworthtelling.focusphone.launcher.RECEIVE_LOCKED_OPEN", LockedOpenHandoff.Permission);
        Assert.Equal("package", LockedOpenHandoff.PackageExtra);
        Assert.Equal("link", LockedOpenHandoff.LinkExtra);
        Assert.Equal("lockedSession", LockedOpenHandoff.PreferencesName);
        Assert.Equal(PolicyContext.LauncherPackage, LockedOpenHandoff.Package);
    }

    /// <summary>
    /// The answer crosses as the ordered broadcast's result code (D301): Activity.RESULT_OK (-1) only
    /// when a session started, 0 for anything else. Pulp reads the same two numbers from this file.
    /// </summary>
    [Fact]
    public void TheResultCodesAreFrozenAndStartedIsAndroidsResultOk()
    {
        Assert.Equal(-1, LockedOpenHandoff.ResultStarted);
        Assert.Equal(0, LockedOpenHandoff.ResultRefused);
            }

    [Fact]
    public void TheReceiverAnswersAnOrderedBroadcastWithTheControllersOwnVerdict()
    {
        var source = Read("Flyleaf.Android", "LockedOpenReceiver.cs");

        Assert.Contains("IsOrderedBroadcast", source, StringComparison.Ordinal);
        Assert.Contains("ResultCode = ", source, StringComparison.Ordinal);
        Assert.Contains("LockedOpenHandoff.ResultStarted", source, StringComparison.Ordinal);
        Assert.Contains("LockedOpenHandoff.ResultRefused", source, StringComparison.Ordinal);
        // The started/refused answer is the controller's return value, never an assumption.
        Assert.Matches(@"var started = LockedSessionController\.Start\(", source);
    }

    [Fact]
    public void TheReceiverIsRegisteredUnderTheContractsNames()
    {
        var source = Read("Flyleaf.Android", "LockedOpenReceiver.cs");

        Assert.Contains("Name = LockedOpenHandoff.Receiver", source, StringComparison.Ordinal);
        Assert.Contains("Permission = LockedOpenHandoff.Permission", source, StringComparison.Ordinal);
        Assert.Contains("Exported = true", source, StringComparison.Ordinal);
        Assert.Contains("[IntentFilter([LockedOpenHandoff.Action])]", source, StringComparison.Ordinal);
        Assert.Contains("intent?.Action != LockedOpenHandoff.Action", source, StringComparison.Ordinal);
        Assert.Contains("GetStringExtra(LockedOpenHandoff.PackageExtra)", source, StringComparison.Ordinal);
        Assert.Contains("GetStringExtra(LockedOpenHandoff.LinkExtra)", source, StringComparison.Ordinal);
    }

    /// <summary>A receiver naming a permission nobody declares is silently unenforceable.</summary>
    [Fact]
    public void ThePermissionIsDeclaredAtSignatureLevel()
    {
        Assert.Matches(
            @"\[assembly: Permission\(Name = LockedOpenHandoff\.Permission, ProtectionLevel = Protection\.Signature\)\]",
            Read("Flyleaf.Android", "LockedOpenReceiver.cs"));
    }

    [Fact]
    public void TheReceiverReadsThroughTheCodecAndStartsOnlyThroughTheController()
    {
        var source = Read("Flyleaf.Android", "LockedOpenReceiver.cs");

        Assert.Contains("LockedOpenCodec.TryRead(", source, StringComparison.Ordinal);
        Assert.Contains("LockedSessionController.Start(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartActivity", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetLockTask", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The controller asks the gate and the pure plan before it changes anything, and a rejected plan
    /// ends the method before the first policy call.
    /// </summary>
    [Fact]
    public void TheControllerConsultsTheGateAndThePlanBeforeAnyPolicyCall()
    {
        var source = Read("Flyleaf.Android", "LockedSessionController.cs");
        var start = source.IndexOf("public static bool Start(", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var body = source[start..source.IndexOf("public static void OnHome(", StringComparison.Ordinal)];

        var gate = body.IndexOf("LockedOpenGate.Decide(", StringComparison.Ordinal);
        var plan = body.IndexOf("LockedSessionPlan.Start(", StringComparison.Ordinal);
        var save = body.IndexOf("store.Save(plan.Record)", StringComparison.Ordinal);
        var allowlist = body.IndexOf("SetLockTaskPackages(", StringComparison.Ordinal);
        var launch = body.IndexOf("OpenLocked(", StringComparison.Ordinal);

        Assert.True(gate >= 0 && plan > gate, "The gate must be asked before the plan is made.");
        Assert.True(save > plan, "The record must be saved after the plan and before any policy call.");
        Assert.True(allowlist > save, "The allowlist must be changed only after the record is on disk.");
        Assert.True(launch > allowlist, "The app must be opened only after the allowlist is in place.");
        Assert.Contains("PlannedSuspendedPackages(", body, StringComparison.Ordinal);
        Assert.Contains("new HiddenAppsStore(context).Load()", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// Lock task is Release G's alone: the policy pass never touches it, and nothing else in the head
    /// does either, so the allowlist cannot be widened by a second route.
    /// </summary>
    [Fact]
    public void OnlyTheControllerChangesTheLockTaskAllowlistOrFeatures()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "FocusPhone"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}Codex.Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"\.(SetLockTaskPackages|SetLockTaskFeatures|StartLockTask|SetLockTaskEnabled)\("))
            .Select(Path.GetFileName)
            .Distinct()
            .ToList();

        Assert.Equal(["LockedSessionController.cs"], offenders);
    }

    /// <summary>
    /// The only package a request may un-suspend is the plan's own <c>UnsuspendPackage</c>, and the only
    /// one release may suspend again is the plan's <c>ResuspendPackage</c>.
    /// </summary>
    [Fact]
    public void TheControllerUnsuspendsAndResuspendsOnlyWhatThePlanNames()
    {
        var source = Read("Flyleaf.Android", "LockedSessionController.cs");
        var calls = Regex.Matches(source, @"SetPackagesSuspended\(([^;]*)\);").Select(match => match.Groups[1].Value).ToList();

        Assert.Equal(2, calls.Count);
        Assert.Contains(calls, call => call.Contains("[unsuspend], false", StringComparison.Ordinal));
        Assert.Contains(calls, call => call.Contains("[resuspend], true", StringComparison.Ordinal));
        Assert.Contains("plan.UnsuspendPackage is { } unsuspend", source, StringComparison.Ordinal);
        Assert.Contains("plan.ResuspendPackage is { } resuspend", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A failed un-suspend must not open the film locked, and a failed re-suspend keeps the record for
    /// a retry (D301): both read the list SetPackagesSuspended returns.
    /// </summary>
    [Fact]
    public void TheControllerHonoursTheFailedListOfBothSuspendCalls()
    {
        var source = Read("Flyleaf.Android", "LockedSessionController.cs");

        Assert.Matches(@"var \w+ = dpm\.SetPackagesSuspended\(admin, \[unsuspend\], false\);", source);
        Assert.Matches(@"var \w+ = dpm\.SetPackagesSuspended\(admin, \[resuspend\], true\);", source);
        Assert.Equal(2, Regex.Matches(source, @"LockedSessionPlan\.DidFail\(").Count);
    }

    /// <summary>
    /// Everything that runs while a session is live reads the monotonic clock (D301): a wall-clock
    /// change must not end or extend a lock. The wall clock appears only at start (to stamp the record)
    /// and at process start (to notice it moved).
    /// </summary>
    [Fact]
    public void TheRunningRulesAreGivenElapsedRealtimeNotTheWallClock()
    {
        var source = Read("Flyleaf.Android", "LockedSessionController.cs");

        Assert.Contains("LockedSessionRules.OnHome(record, SystemClock.ElapsedRealtime())", source, StringComparison.Ordinal);
        Assert.Contains("var elapsed = SystemClock.ElapsedRealtime();", source, StringComparison.Ordinal);
        Assert.Contains("LockedSessionRules.OnTick(record, elapsed)", source, StringComparison.Ordinal);
        Assert.Contains("LockedSessionRules.OnReading(record, reading, SystemClock.ElapsedRealtime())", source, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(source, @"DateTimeOffset\.UtcNow").Count);
    }

    /// <summary>
    /// Android 14 blocks a background activity start unless the sender opts in; the repo already learned
    /// that for the call screen. The locked launch goes through a PendingIntent with the same opt-in (D301).
    /// </summary>
    [Fact]
    public void TheLockedLaunchIsAPendingIntentWithTheBackgroundStartOptIn()
    {
        var source = Read("Flyleaf.Android", "LockedSessionController.cs");
        var start = source.IndexOf("private static void OpenLocked(", StringComparison.Ordinal);
        var body = source[start..source.IndexOf("private static void BringForward(", StringComparison.Ordinal)];

        Assert.Contains("PendingIntent.GetActivity(", body, StringComparison.Ordinal);
        Assert.Contains("SetLockTaskEnabled(true)", body, StringComparison.Ordinal);
        Assert.Contains("CallScreen.OptInToBackgroundStart(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("context.StartActivity(", body, StringComparison.Ordinal);
        Assert.Contains("SetPendingIntentBackgroundActivityStartMode", Read("Flyleaf.Android", "CallScreen.cs"), StringComparison.Ordinal);
    }

    /// <summary>The start arms its own early alarm so a film that never opens is let go (D301).</summary>
    [Fact]
    public void TheStartArmsTheShortStartCheckAndTheTickFollowsTheRulesNextTick()
    {
        var source = Read("Flyleaf.Android", "LockedSessionController.cs");

        Assert.Contains("LockedSessionRules.NextTick(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (Java.Lang.Exception", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The link-domain allowlist beside the package catalogue equals the one Pulp's catalogue carries,
    /// package for package (D301). Read from Pulp's source by content.
    /// </summary>
    [Fact]
    public void TheLinkDomainsEqualPulpsServiceCatalogueDomainsPackageForPackage()
    {
        var path = Path.Combine(RepoRoot(), "Pulp", "Pulp.Domain", "Services", "ServiceCatalogue.cs");
        var source = File.ReadAllText(path);
        var start = source.IndexOf("Public { get; } = new(", StringComparison.Ordinal);
        var block = source[start..source.IndexOf("]);", start, StringComparison.Ordinal)];
        var ours = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match service in Regex.Matches(block, @"new\(new ServiceId\([^)]*\),\s*""[^""]*"",\s*\[([^\]]*)\],[^\n]*linkDomains:\s*\[([^\]]*)\]"))
        {
            var domains = string.Join(",", Regex.Matches(service.Groups[2].Value, @"""([^""]*)""").Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal));
            foreach (Match package in Regex.Matches(service.Groups[1].Value, @"""([^""]*)"""))
            {
                ours[package.Groups[1].Value] = domains;
            }
        }

        var theirs = LockedOpenCatalogue.LinkDomains.ToDictionary(
            pair => pair.Key, pair => string.Join(",", pair.Value.Order(StringComparer.Ordinal)), StringComparer.Ordinal);

        Assert.NotEmpty(ours);
        Assert.Equal(ours.OrderBy(pair => pair.Key, StringComparer.Ordinal), theirs.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void ReleaseRestoresThePolicyBeforeItClearsTheRecordAndNeverClearsAfterAFailure()
    {
        var source = Read("Flyleaf.Android", "LockedSessionController.cs");
        var start = source.IndexOf("private static void ReleaseCore(", StringComparison.Ordinal);
        var body = source[start..source.IndexOf("private static void OpenLocked(", StringComparison.Ordinal)];

        var restore = body.IndexOf("SetLockTaskPackages(", StringComparison.Ordinal);
        var catchAt = body.IndexOf("catch (Exception", StringComparison.Ordinal);
        var returnAt = body.IndexOf("return;", catchAt, StringComparison.Ordinal);
        var clear = body.IndexOf("store.Clear()", StringComparison.Ordinal);
        var forward = body.IndexOf("BringForward(", clear, StringComparison.Ordinal);

        Assert.True(restore >= 0 && catchAt > restore, "The allowlist must be restored inside the guarded block.");
        Assert.True(returnAt > catchAt && returnAt < clear, "A failed restore must return before the record is cleared.");
        Assert.True(clear > catchAt && forward > clear, "Pulp comes forward only after the record is cleared.");
    }

    [Fact]
    public void EveryTriggerThatEndsASessionIsWired()
    {
        Assert.Contains("LockedSessionController.OnHome(this)", Read("Flyleaf.Android", "HomeActivity.cs"), StringComparison.Ordinal);
        Assert.Contains("LockedSessionController.EndStaleAtStart(this)", Read("Flyleaf.Android", "FocusApplication.cs"), StringComparison.Ordinal);
        Assert.Contains("LockedSessionController.OnReading(context, reading, Send)", Read("Flyleaf.Android", "PlaybackSessionWatcher.cs"), StringComparison.Ordinal);
        Assert.Contains("LockedSessionController.OnTick(context)", Read("Flyleaf.Android", "LockedSessionTickReceiver.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheClockReceiverIsNotExported()
    {
        Assert.Contains("Exported = false", Read("Flyleaf.Android", "LockedSessionTickReceiver.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The streaming catalogue here and Pulp's own are one list. Read from Pulp's source by content so a
    /// service added on either side, or a package that moves, fails here instead of refusing the title.
    /// </summary>
    [Fact]
    public void TheCatalogueEqualsPulpsServiceCataloguePackageForPackage()
    {
        var path = Path.Combine(RepoRoot(), "Pulp", "Pulp.Domain", "Services", "ServiceCatalogue.cs");
        Assert.True(File.Exists(path), $"Pulp's ServiceCatalogue.cs was not found at {path}.");
        var source = File.ReadAllText(path);
        var start = source.IndexOf("Public { get; } = new(", StringComparison.Ordinal);
        var block = source[start..source.IndexOf("]);", start, StringComparison.Ordinal)];
        // Each service line carries its packages in the first bracket; the merchant names are in the last.
        var firstBrackets = Regex.Matches(block, @"new\(new ServiceId\([^)]*\),\s*""[^""]*"",\s*\[([^\]]*)\]")
            .SelectMany(match => Regex.Matches(match.Groups[1].Value, @"""([^""]*)""").Select(quoted => quoted.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(firstBrackets);
        Assert.Equal(firstBrackets.Order(StringComparer.Ordinal), LockedOpenCatalogue.Packages.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Locked sessions run six hours at the longest, and a session turns on Home and the keyguard only.
    /// Moving either is a decision about the lockdown, not a refactor.
    /// </summary>
    [Fact]
    public void ThePolicyConstantsOfALockedSessionAreFrozen()
    {
        Assert.Equal(TimeSpan.FromHours(6), LockedSessionRules.MaxDuration);
        Assert.Equal(4 | 32, LockTaskFeatureFlags.Session);
    }

    private static string Read(string project, string file)
    {
        var path = Path.Combine(RepoRoot(), "FocusPhone", project, file);
        Assert.True(File.Exists(path), $"{file} not found at {path}.");
        return File.ReadAllText(path);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
