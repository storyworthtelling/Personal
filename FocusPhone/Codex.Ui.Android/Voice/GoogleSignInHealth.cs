using Android.Content;
using Android.Widget;
using Storyworthtelling.Voice.Health;

namespace Codex.Ui.Voice;

/// <summary>
/// What a native head says when Google turned its background sign-in away (audit 11 §4): the notice behind the
/// fold on the app's own screen and the "Sign in again" wording on its Settings row. Both read the head's credential
/// health, which <see cref="VoiceGoogleAccount"/>'s token source writes; signing in again clears it.
/// </summary>
public static class GoogleSignInHealth
{
    public const string SignInAgain = "Sign in again";

    /// <summary>Whether Settings should offer <see cref="SignInAgain"/>.</summary>
    public static bool NeedsSignIn() => GoogleAccountNotice.ShowsSignInAgain(HeadCredentialHealth.Store().Load());

    /// <summary>The Settings button's label: <paramref name="usual"/>, or <see cref="SignInAgain"/> while refused.</summary>
    public static string ButtonLabel(string usual) => NeedsSignIn() ? SignInAgain : usual;

    /// <summary>
    /// States the sign-in notice on <paramref name="overlay"/>, or withdraws it. Safe on every resume.
    /// <paramref name="openSettings"/> is the notice's action, when the screen has a way to Settings.
    /// </summary>
    public static void ShowNotice(Context context, FrameLayout overlay, Action? openSettings = null)
    {
        var notice = GoogleAccountNotice.For(HeadCredentialHealth.Store().Load());
        DogEar.SetNotice(context, overlay, GoogleAccountNotice.Key, notice is null
            ? null
            : new DogEar.Notice(notice.Headline, notice.Detail, DogEar.Hues.Problem,
                openSettings is null ? null : "Open Settings", openSettings));
    }
}
