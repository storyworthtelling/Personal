using System.Text.RegularExpressions;
using Codex.Domain.Apps;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Guard G20, Flyleaf's half: the timed-release contract Pulp asks for before a streaming title opens (the
/// Entertainment block). The wire names are typed out here on purpose, and Pulp's copy
/// (<c>Pulp.Domain/LockedOpen/TimedReleaseHandOff.cs</c>) is read by content and held equal to them, so a
/// drift on either side fails here rather than the release being dropped and the title staying shut.
/// </summary>
public class TimedReleaseContractTests
{
    [Fact]
    public void TheWireNamesAreFrozen()
    {
        Assert.Equal("com.storyworthtelling.focusphone.launcher", TimedReleaseHandoff.Package);
        Assert.Equal("com.storyworthtelling.focusphone.launcher.TimedReleaseReceiver", TimedReleaseHandoff.Receiver);
        Assert.Equal("com.storyworthtelling.focusphone.launcher.action.TIMED_RELEASE", TimedReleaseHandoff.Action);
        Assert.Equal("com.storyworthtelling.focusphone.launcher.RECEIVE_LOCKED_OPEN", TimedReleaseHandoff.Permission);
        Assert.Equal("package", TimedReleaseHandoff.PackageExtra);
        Assert.Equal("link", TimedReleaseHandoff.LinkExtra);
        Assert.Equal("minutes", TimedReleaseHandoff.MinutesExtra);
        Assert.Equal("timedReleases", TimedReleaseHandoff.PreferencesName);
        Assert.Equal(-1, TimedReleaseHandoff.ResultReleased);
        Assert.Equal(0, TimedReleaseHandoff.ResultRefused);
        Assert.Equal(360, TimedReleaseHandoff.MaxMinutes);
        Assert.Equal(1, TimedReleaseHandoff.MinMinutes);
    }

    [Fact]
    public void PulpsCopyOfTheContractIsTheSameContract()
    {
        var pulp = ReadRepoFile("Pulp", "Pulp.Domain", "LockedOpen", "TimedReleaseHandOff.cs");
        var expected = new Dictionary<string, string>
        {
            ["Package"] = TimedReleaseHandoff.Package,
            ["Receiver"] = TimedReleaseHandoff.Receiver,
            ["Action"] = TimedReleaseHandoff.Action,
            ["PackageExtra"] = TimedReleaseHandoff.PackageExtra,
            ["LinkExtra"] = TimedReleaseHandoff.LinkExtra,
            ["MinutesExtra"] = TimedReleaseHandoff.MinutesExtra,
        };
        foreach (var (name, value) in expected)
        {
            Assert.Matches(new Regex($"public const string {name} = \"{Regex.Escape(value)}\";"), pulp);
        }

        Assert.Matches($@"public const int ResultReleased = {TimedReleaseHandoff.ResultReleased};", pulp);
        Assert.Matches($@"public const int ResultRefused = {TimedReleaseHandoff.ResultRefused};", pulp);
        Assert.Matches($@"public const int MaxMinutes = {TimedReleaseHandoff.MaxMinutes};", pulp);
        Assert.Matches($@"public const int MinMinutes = {TimedReleaseHandoff.MinMinutes};", pulp);
        // Pulp holds the locked open's permission as a literal of its own; the value must be the same one.
        Assert.Contains($"public const string Permission = \"{TimedReleaseHandoff.Permission}\";", pulp, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadRegistersTheReceiverAndAppliesTheWindow()
    {
        var receiver = ReadRepoFile("FocusPhone", "Flyleaf.Android", "TimedReleaseReceiver.cs");
        Assert.Contains("Name = TimedReleaseHandoff.Receiver", receiver, StringComparison.Ordinal);
        // The release travels under the locked open's permission, which ContractPermissions.cs already declares and requests.
        Assert.Contains("Permission = LockedOpenHandoff.Permission", receiver, StringComparison.Ordinal);
        Assert.Contains("[IntentFilter([TimedReleaseHandoff.Action])]", receiver, StringComparison.Ordinal);
        Assert.Contains("TimedReleaseController.Release(", receiver, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLifecycleHooksAreWired()
    {
        // The window closes by an alarm and a policy pass; both must be reached from the app's own lifecycle.
        Assert.Contains("TimedReleaseController.OnProcessStart(this)", ReadRepoFile("FocusPhone", "Flyleaf.Android", "FocusApplication.cs"), StringComparison.Ordinal);
        Assert.Contains("TimedReleasedPackages: Codex.Domain.Apps.TimedReleases.Active(", ReadRepoFile("FocusPhone", "Flyleaf.Android", "PolicyManager.cs"), StringComparison.Ordinal);
    }

    private static string ReadRepoFile(params string[] segments)
    {
        // Walk up from the test binary to the repository root, then read the file by its path.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "FocusPhone")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var path = Path.Combine(directory!.FullName, Path.Combine(segments));
        Assert.True(File.Exists(path), $"Couldn’t find {path}.");
        return File.ReadAllText(path);
    }
}
