using System.Reflection;
using Storyworthtelling.Diagnostics;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// Plan §4.2 (G2): every browser or account sign-in leaves a trail that says whether a token came back and,
/// if not, why. Each flow on the loop is listed here with where its trail is kept: Post's Gmail sign-in and every
/// other head's Google sign-in ride the shared device-account sign-in (VoiceGoogleAccount), whose plain overload
/// trails into the head log by default; Score's music account has its own picker, trailed the same way; TIDAL's two
/// flows; Flyleaf's redirect hop; and the bed's sign-in on Constitution's Sleep Number trail.
/// </summary>
public class SignInFlowsCoveredTests
{
    private const string DeviceAccount = "FocusPhone/Codex.Ui.Android/Voice/VoiceGoogleAccount.cs";
    private const string PostSettings = "FocusPhone/Post.Android/SettingsArea.cs";
    private const string Head = "FocusPhone/Codex.Ui.Android/Diagnostics/HeadDiagnostics.cs";
    private const string ScoreSignIn = "FocusPhone/Score.Android/Google/DeviceAccountSignIn.cs";
    private const string TidalPairing = "FocusPhone/Score.Android/TidalPairingActivity.cs";
    private const string FlyleafBrowser = "FocusPhone/Flyleaf.Android/RestrictedBrowserActivity.cs";
    private const string SleepIq = "Constitution/Constitution.Infrastructure/SleepNumber/SleepIqClient.cs";

    [Fact]
    public void TheCoveredFlowsAreKnownFlowKinds()
    {
        var kinds = typeof(SignInFlowKinds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var kind in new[]
        {
            SignInFlowKinds.GmailApi, SignInFlowKinds.GoogleDeviceAccount, SignInFlowKinds.TidalPkce,
            SignInFlowKinds.TidalDevice, SignInFlowKinds.RedirectHop, SignInFlowKinds.SleepNumber,
        })
        {
            Assert.Contains(kind, kinds);
        }
    }

    [Fact]
    public void EveryPlainGoogleSignInIsTrailedIntoTheHeadLog()
    {
        var source = LogLoopSource.File(DeviceAccount);

        Assert.Contains("SignIn(activity, SignInFlowKinds.GoogleDeviceAccount, ToHeadLog)", source, StringComparison.Ordinal);
        Assert.Contains("HeadDiagnostics.Current?.SignIn(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryScreenResumeChecksForASignInThatNeverCameBack()
    {
        var source = LogLoopSource.File(Head);

        Assert.Contains("RegisterActivityLifecycleCallbacks(", source, StringComparison.Ordinal);
        Assert.Contains("VoiceGoogleAccount.CheckPendingSignIn(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ScoresMusicAccountPickerIsTrailed()
    {
        var source = LogLoopSource.File(ScoreSignIn);

        foreach (var step in new[] { "SignInFlowKinds.GoogleDeviceAccount", ".LeftApp(", ".AccountChosen(", ".AccountCancelled(", ".TokenReceived(", ".CheckNoReturn(", "HeadDiagnostics.Current?.SignIn(" })
        {
            Assert.Contains(step, source, StringComparison.Ordinal);
        }
        Assert.Contains("DeviceAccountSignIn.CheckPending(", LogLoopSource.File("FocusPhone/Score.Android/ScoreActivity.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void BothTidalFlowsAreTrailed()
    {
        var pairing = LogLoopSource.File(TidalPairing);

        Assert.Contains("TidalSignInTrails.", pairing, StringComparison.Ordinal);
        Assert.Contains("HeadDiagnostics.Current?.SignIn(", pairing, StringComparison.Ordinal);
        Assert.Contains("CheckNoReturn(", pairing, StringComparison.Ordinal);
        Assert.Contains("SignInFlowKinds.TidalPkce", LogLoopSource.File("FocusPhone/Codex.Infrastructure/Music/TidalSignInTrails.cs"), StringComparison.Ordinal);
        Assert.Contains("SignInFlowKinds.TidalDevice", LogLoopSource.File("FocusPhone/Codex.Infrastructure/Music/TidalSignInTrails.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void FlyleafRecordsTheHopBothWays()
    {
        var browser = LogLoopSource.File(FlyleafBrowser);

        Assert.Contains("RedirectHop.Handed(", browser, StringComparison.Ordinal);
        Assert.Contains("RedirectHop.Blocked(", browser, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBedsSignInSaysWhatTheServiceAnsweredAndNeverThePassword()
    {
        var client = LogLoopSource.File(SleepIq);
        var outcomes = System.Text.RegularExpressions.Regex.Matches(client, @"log\.Outcome\([^;]*;").Select(m => m.Value).ToList();

        Assert.Contains(outcomes, line => line.Contains("{status}", StringComparison.Ordinal));
        Assert.DoesNotContain(outcomes, line => line.Contains("assword", StringComparison.Ordinal));
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
