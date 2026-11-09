using Storyworthtelling.Google.Android;
using Foreword.Infrastructure.Google;

namespace Foreword.Android.Google;

// Supplies Google access tokens for the device account chosen during sign-in, using the core
// Android AccountManager (the system's Google account authenticator). No Play Services binding
// or stored secret is required.
public class DeviceAccountTokenProvider : IGoogleAccessTokenProvider
{
    // AccountManager auth-token type: "oauth2:" followed by the space-separated scopes.
    // Calendar + Tasks for the delivery augmenter, plus full Drive so the app can read the shared
    // owner's-voice profile/config (the greeting) AND write its section of the encrypted secrets vault (NYT
    // cookie/key, reMarkable token, Anthropic key) back to Drive, which read-only can't do. Changing the
    // scope changes the consent screen, so it must be registered on this app's OAuth client and re-consented
    // on the device; until then, vault publish/restore degrade quietly and the app works from local settings.
    internal const string TokenScope =
        "oauth2:https://www.googleapis.com/auth/calendar https://www.googleapis.com/auth/tasks " +
        "https://www.googleapis.com/auth/drive";

    // The cache and the AccountManager fetch are the shared kit's (audit 01 §10); this head supplies its scopes,
    // exactly as they were (Android keys a granted consent by the token-type string), and where the email is kept.
    private readonly DeviceAccountTokens tokens = new(() => CredentialStore.GoogleAccountEmail, TokenScope, "Sign in with Google first.");

    public Task<string> GetAccessToken() => tokens.GetAccessToken();
}
