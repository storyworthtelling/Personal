using global::Android.App;
using global::Android.Content;

namespace Foreword.Android.Google;

// "Sign in with Google" for this head: the kit's one flow (Storyworthtelling.Maui.Google, audit 01 §10) with this
// head's request code, scopes and the place it keeps the chosen account. MainActivity routes the picker's answer
// back through HandleChooserResult.
public static class DeviceAccountSignIn
{
    public const int RequestCode = 9001;

    private static readonly Storyworthtelling.Maui.Google.DeviceAccountSignInFlow Flow = new(
        RequestCode, DeviceAccountTokenProvider.TokenScope, "google calendar tasks drive", () => CredentialStore.GoogleAccountEmail, email => CredentialStore.GoogleAccountEmail = email);

    public static Task<string> SignIn(Activity activity) => Flow.SignIn(activity);

    public static void HandleChooserResult(Result resultCode, Intent? data) => Flow.HandleChooserResult(resultCode, data);

    /// <summary>A token for one extra scope, with its own consent (the People API's home address).</summary>
    public static Task<string?> TokenFor(Activity activity, string scope) => Flow.TokenFor(activity, scope);
}
