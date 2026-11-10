using Storyworthtelling.Google.Android;

namespace Index.Android.Google;

// Supplies Google access tokens for the device account chosen during sign-in, using the core Android
// AccountManager. No Play Services binding is required. Scope is full Drive: Ledger reads the shared
// owner's-voice profile/config AND writes its section of the encrypted secrets vault (Plaid credentials +
// each bank's access token) back to Drive, which read-only can't do. The owner re-registers this scope on
// the OAuth client — until then, vault publish/restore degrade quietly and the app works from local settings.
public class DeviceAccountTokenProvider
{
    // AccountManager auth-token type: "oauth2:" followed by the space-separated scopes.
    internal const string TokenScope = "oauth2:https://www.googleapis.com/auth/drive";

    // The cache and the AccountManager fetch are the shared kit's (audit 01 §10); this head supplies its scopes,
    // exactly as they were (Android keys a granted consent by the token-type string), and where the email is kept.
    private readonly DeviceAccountTokens tokens = new(() => SettingsStore.GoogleAccountEmail, TokenScope, "Sign in with Google first.");

    public Task<string> GetAccessToken() => tokens.GetAccessToken();
}
