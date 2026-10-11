using System.Text.RegularExpressions;
using Pulp.Domain;
using Pulp.Domain.Sessions;

namespace Pulp.Tests.Sessions;

/// <summary>
/// Guard G18, Pulp's half. Flyleaf relays playback sessions to a component, action and permission
/// nobody checks at run time: a drifted name drops the broadcast and Pulp never sees a title. Every
/// wire name here is read out of <c>PlaybackSessions.cs</c> in the launcher's Domain by content, never
/// retyped, so a change on either side fails here.
/// </summary>
public class SessionContractTests
{
    private const string FlyleafContract = "FocusPhone/Codex.Domain/Apps/PlaybackSessions.cs";
    private const string ReceiverPath = "Pulp.Android/Sessions/SessionReceiver.cs";

    [Theory]
    [InlineData("Package", nameof(SessionHandOff.Package))]
    [InlineData("Receiver", nameof(SessionHandOff.Receiver))]
    [InlineData("Action", nameof(SessionHandOff.Action))]
    [InlineData("Permission", nameof(SessionHandOff.Permission))]
    [InlineData("SessionExtra", nameof(SessionHandOff.SessionExtra))]
    [InlineData("FieldPackage", nameof(SessionHandOff.FieldPackage))]
    [InlineData("FieldTitle", nameof(SessionHandOff.FieldTitle))]
    [InlineData("FieldPositionMs", nameof(SessionHandOff.FieldPositionMs))]
    [InlineData("FieldDurationMs", nameof(SessionHandOff.FieldDurationMs))]
    [InlineData("FieldState", nameof(SessionHandOff.FieldState))]
    [InlineData("StatePlaying", nameof(SessionHandOff.StatePlaying))]
    [InlineData("StatePaused", nameof(SessionHandOff.StatePaused))]
    [InlineData("StateStopped", nameof(SessionHandOff.StateStopped))]
    public void EveryWireNameEqualsFlyleafsByteForByte(string flyleafName, string ourName)
    {
        var ours = (string)typeof(SessionHandOff).GetField(ourName)!.GetValue(null)!;

        Assert.Equal(FlyleafConstant(flyleafName), ours);
    }

    [Fact]
    public void TheReceiverAndActionAndPermissionLiveUnderPulpsPackage()
    {
        Assert.Equal(PulpIdentity.ApplicationId, SessionHandOff.Package);
        Assert.StartsWith(PulpIdentity.ApplicationId + ".", SessionHandOff.Receiver, StringComparison.Ordinal);
        Assert.StartsWith(PulpIdentity.ApplicationId + ".", SessionHandOff.Action, StringComparison.Ordinal);
        Assert.Equal(PulpIdentity.ApplicationId + ".RECEIVE_SESSIONS", SessionHandOff.Permission);
    }

    [Fact]
    public void TheReceiverIsRegisteredExportedUnderThePermission()
    {
        var source = ReceiverSource();

        Assert.Contains("Name = SessionHandOff.Receiver", source, StringComparison.Ordinal);
        Assert.Contains("Permission = SessionHandOff.Permission", source, StringComparison.Ordinal);
        Assert.Contains("Exported = true", source, StringComparison.Ordinal);
        Assert.Contains("[IntentFilter([SessionHandOff.Action])]", source, StringComparison.Ordinal);
        Assert.Contains("intent?.Action != SessionHandOff.Action", source, StringComparison.Ordinal);
        Assert.Contains("GetStringExtra(SessionHandOff.SessionExtra)", source, StringComparison.Ordinal);
    }

    /// <summary>A receiver naming a permission nobody declares is never granted to anyone, Flyleaf included.</summary>
    [Fact]
    public void ThePermissionIsDeclaredAtSignatureLevel()
    {
        Assert.Matches(
            @"\[assembly: Permission\(Name = SessionHandOff\.Permission, ProtectionLevel = Protection\.Signature\)\]",
            ReceiverSource());
    }

    [Fact]
    public void TheReceiverDecodesBeforeAnythingElseAndRecordsOnlyThroughTheHook()
    {
        var source = ReceiverSource();

        Assert.Contains("SessionDecoder.TryDecode(", source, StringComparison.Ordinal);
        Assert.Contains("SessionReading.Interpret(", source, StringComparison.Ordinal);
        Assert.Contains("SessionHooks.Record", source, StringComparison.Ordinal);
    }

    private static string ReceiverSource()
    {
        var path = Path.Combine(PulpSource.Root(), ReceiverPath);
        Assert.True(File.Exists(path), $"{ReceiverPath} was not found.");
        return File.ReadAllText(path);
    }

    private static string FlyleafConstant(string name)
    {
        var path = Path.Combine(PulpSource.Root(), "..", FlyleafContract);
        Assert.True(File.Exists(path), $"{FlyleafContract} not found at {path}; is FocusPhone checked out?");
        var match = Regex.Match(
            File.ReadAllText(path), $@"public const string {Regex.Escape(name)}\s*=\s*""([^""]*)""\s*;");
        Assert.True(match.Success, $"Could not find PlaybackSessionHandoff.{name} in {FlyleafContract}.");
        return match.Groups[1].Value;
    }
}
