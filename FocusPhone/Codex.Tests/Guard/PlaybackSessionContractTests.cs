using System.Text.RegularExpressions;
using Codex.Domain.Apps;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Guard G18, Flyleaf's half: the contract playback sessions travel to Pulp over (finding 2.3, D30,
/// D288). The expected values are typed out here on purpose, because the guard exists to notice them
/// moving; Pulp's own <c>SessionContractTests</c> reads <c>PlaybackSessions.cs</c> by content. Nothing
/// fails at run time if they drift: the broadcast is dropped and Pulp never learns a title was seen.
/// </summary>
public class PlaybackSessionContractTests
{
    [Fact]
    public void TheWireNamesAreFrozen()
    {
        Assert.Equal("com.storyworthtelling.pulp", PlaybackSessionHandoff.Package);
        Assert.Equal("com.storyworthtelling.pulp.SessionReceiver", PlaybackSessionHandoff.Receiver);
        Assert.Equal("com.storyworthtelling.pulp.action.PLAYBACK_SESSION", PlaybackSessionHandoff.Action);
        Assert.Equal("com.storyworthtelling.pulp.RECEIVE_SESSIONS", PlaybackSessionHandoff.Permission);
        Assert.Equal("session", PlaybackSessionHandoff.SessionExtra);
        Assert.Equal("package", PlaybackSessionHandoff.FieldPackage);
        Assert.Equal("title", PlaybackSessionHandoff.FieldTitle);
        Assert.Equal("positionMs", PlaybackSessionHandoff.FieldPositionMs);
        Assert.Equal("durationMs", PlaybackSessionHandoff.FieldDurationMs);
        Assert.Equal("state", PlaybackSessionHandoff.FieldState);
        Assert.Equal("playing", PlaybackSessionHandoff.StatePlaying);
        Assert.Equal("paused", PlaybackSessionHandoff.StatePaused);
        Assert.Equal("stopped", PlaybackSessionHandoff.StateStopped);
        Assert.Equal(PolicyContext.PulpPackage, PlaybackSessionHandoff.Package);
    }

    [Fact]
    public void ThePackageEqualsPulpsOwnIdentity()
    {
        var identity = File.ReadAllText(Path.Combine(RepoRoot(), "Pulp", "Pulp.Domain", "PulpIdentity.cs"));
        var declared = Regex.Match(identity, @"public const string ApplicationId = ""([^""]*)""");
        Assert.True(declared.Success);
        Assert.Equal(declared.Groups[1].Value, PlaybackSessionHandoff.Package);
    }

    [Fact]
    public void TheEncodedPayloadUsesTheDeclaredFieldNames()
    {
        var text = PlaybackSessionCodec.Encode(new PlaybackSessionReading(
            "com.example.streamer", "Harbor Lights", 1000, 2000, PlaybackSessionState.Paused));

        Assert.Contains("\"package\":", text, StringComparison.Ordinal);
        Assert.Contains("\"title\":", text, StringComparison.Ordinal);
        Assert.Contains("\"positionMs\":1000", text, StringComparison.Ordinal);
        Assert.Contains("\"durationMs\":2000", text, StringComparison.Ordinal);
        Assert.Contains("\"state\":\"paused\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRelayBroadcastsExplicitlyUnderThePermissionAndDeclaresIt()
    {
        var source = RelaySource();

        Assert.Contains("new ComponentName(PlaybackSessionHandoff.Package, PlaybackSessionHandoff.Receiver)", source, StringComparison.Ordinal);
        Assert.Contains("new Intent(PlaybackSessionHandoff.Action)", source, StringComparison.Ordinal);
        Assert.Contains("PutExtra(PlaybackSessionHandoff.SessionExtra,", source, StringComparison.Ordinal);
        Assert.Contains("SendBroadcast(intent, PlaybackSessionHandoff.Permission)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SendBroadcast(intent)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetPackage(", source, StringComparison.Ordinal);
        Assert.Matches(@"\[assembly: UsesPermission\(PlaybackSessionHandoff\.Permission\)\]", source);
    }

    /// <summary>Only packages Pulp named, from the stored hidden-apps set, ever leave the phone.</summary>
    [Fact]
    public void TheRelayAsksTheDecisionAndTheStoredSetBeforeSending()
    {
        var source = RelaySource();

        Assert.Contains(".Offer(", source, StringComparison.Ordinal);
        Assert.Contains("new HiddenAppsStore(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheListenerStartsTheRelay()
    {
        var listener = File.ReadAllText(Path.Combine(RepoRoot(), "FocusPhone", "Flyleaf.Android", "TileNotificationListener.cs"));

        Assert.Contains("PlaybackSessionWatcher", listener, StringComparison.Ordinal);
    }

    private static string RelaySource()
    {
        var path = Path.Combine(RepoRoot(), "FocusPhone", "Flyleaf.Android", "PlaybackSessionWatcher.cs");
        Assert.True(File.Exists(path), $"PlaybackSessionWatcher.cs not found at {path}.");
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
