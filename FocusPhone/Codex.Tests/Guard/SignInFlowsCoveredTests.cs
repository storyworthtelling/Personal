using System.Reflection;
using Storyworthtelling.Diagnostics;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Plan §4.2 (G2): every browser or account sign-in leaves a trail that says whether a token came back and,
/// if not, why. Each flow on the loop is listed here with where its trail is kept; Phase 1 covers Post's
/// Gmail sign-in, which rides the shared device-account sign-in (VoiceGoogleAccount). Phase 2 adds TIDAL,
/// Flyleaf's redirect hop, the other device-account heads and Sleep Number.
/// </summary>
public class SignInFlowsCoveredTests
{
    private const string DeviceAccount = "FocusPhone/Codex.Ui.Android/Voice/VoiceGoogleAccount.cs";
    private const string PostSettings = "FocusPhone/Post.Android/SettingsArea.cs";

    [Fact]
    public void TheCoveredFlowsAreKnownFlowKinds()
    {
        var kinds = typeof(SignInFlowKinds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(SignInFlowKinds.GmailApi, kinds);
    }

    [Fact]
    public void TheDeviceAccountSignInRecordsEveryWayItCanEnd()
    {
        var source = LogLoopSource.File(DeviceAccount);

        foreach (var step in new[]
        {
            "SignInTrail.Start(", ".LeftApp(", ".AccountChosen(", ".AccountCancelled(", ".ConsentShown(",
            ".ConsentDenied(", ".RecoverableAuth(", ".TokenReceived(", ".CheckNoReturn(",
        })
        {
            Assert.Contains(step, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheDeviceAccountSignInNeverHandsATokenToTheTrail()
    {
        var source = LogLoopSource.File(DeviceAccount);
        var tokenStep = source[source.IndexOf(".TokenReceived(", StringComparison.Ordinal)..];
        var call = tokenStep[..tokenStep.IndexOf(';')];

        Assert.DoesNotContain("token)", call, StringComparison.Ordinal);
        Assert.DoesNotContain("cachedToken", call, StringComparison.Ordinal);
    }

    [Fact]
    public void PostsGmailSignInIsTrailedAndChecksForNoReturnOnResume()
    {
        var settings = LogLoopSource.File(PostSettings);

        Assert.Contains("SignInFlowKinds.GmailApi", settings, StringComparison.Ordinal);
        Assert.Contains("PostDiagnostics.LogSignIn", settings, StringComparison.Ordinal);
        Assert.Contains("VoiceGoogleAccount.CheckPendingSignIn(", settings, StringComparison.Ordinal);
    }
}
