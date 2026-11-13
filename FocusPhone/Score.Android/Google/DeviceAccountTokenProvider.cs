using Codex.Ui.Voice;
using Storyworthtelling.Voice.Health;
using Storyworthtelling.Google.Android;

namespace Score.Google;

// Supplies Google access tokens for the device account chosen during sign-in, using the core
// Android AccountManager (the system's Google account authenticator). No Play Services binding or
// stored secret is required. Scope is full Drive: the app reads the owner's music files and the shared
// voice profile/config, and — for Listening Genius — writes the summarized Taste Profile back to Drive,
// which read-only access can't do. The owner registers the broadened scope on the OAuth client.
public class DeviceAccountTokenProvider
{
    // AccountManager auth-token type: "oauth2:" followed by the space-separated scopes.
    internal const string TokenScope = "oauth2:https://www.googleapis.com/auth/drive";

    // The cache and the AccountManager fetch are the shared kit's (audit 01 §10); this head supplies its scopes,
    // exactly as they were (Android keys a granted consent by the token-type string), and where the email is kept.
    private readonly DeviceAccountTokens tokens = new(() => MusicSettingsStore.GoogleAccountEmail, TokenScope, "Sign in with Google first.",
        GoogleAccountNotice.Reporter(HeadCredentialHealth.Store()));

    public Task<string> GetAccessToken() => tokens.GetAccessToken();
}
