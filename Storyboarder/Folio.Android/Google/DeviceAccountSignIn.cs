using global::Android.App;
using global::Android.Content;

namespace Folio.Android.Google;

// "Sign in with Google" for this head: the kit's one flow (Storyworthtelling.Maui.Google, audit 01 §10) with this
// head's request code, scopes and the place it keeps the chosen account. MainActivity routes the picker's answer
// back through HandleChooserResult.
public static class DeviceAccountSignIn
{
    public const int RequestCode = 9001;

    private static readonly Storyworthtelling.Maui.Google.DeviceAccountSignInFlow Flow = new(
        RequestCode, DeviceAccountTokenProvider.TokenScope, DeviceAccountTokenProvider.TokenScope, () => SettingsStore.GoogleAccountEmail, email => SettingsStore.GoogleAccountEmail = email);

    public static Task<string> SignIn(Activity activity) => Flow.SignIn(activity);

    public static void HandleChooserResult(Result resultCode, Intent? data) => Flow.HandleChooserResult(resultCode, data);
}
