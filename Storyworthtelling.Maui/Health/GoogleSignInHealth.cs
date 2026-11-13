using Storyworthtelling.Folds;
using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Maui.Health;

/// <summary>
/// What a MAUI head says when Google turned its background sign-in away (audit 11 §4): the notice behind the fold
/// and the "Sign in again" wording on its setup page. Both read the head's credential health, which the head's
/// <c>DeviceAccountTokens</c> writes; <c>DeviceAccountSignInFlow</c> clears it on a sign-in.
/// </summary>
public static class GoogleSignInHealth
{
    public const string SignInAgain = "Sign in again";

    public static bool NeedsSignIn() => GoogleAccountNotice.ShowsSignInAgain(MauiCredentialHealth.Store().Load());

    /// <summary>The setup button's label: <paramref name="usual"/>, or <see cref="SignInAgain"/> while refused.</summary>
    public static string ButtonLabel(string usual) => NeedsSignIn() ? SignInAgain : usual;

    /// <summary>States the sign-in notice on <paramref name="folds"/>, or withdraws it. Returns whether one is waiting.</summary>
    public static bool Apply(DogEarState folds)
    {
        var notice = GoogleAccountNotice.For(MauiCredentialHealth.Store().Load());
        folds.SetNotice(GoogleAccountNotice.Key, notice is null
            ? null
            : new DogEarNotice(notice.Headline, notice.Detail, DogEarHue.Problem));
        return notice is not null;
    }
}
