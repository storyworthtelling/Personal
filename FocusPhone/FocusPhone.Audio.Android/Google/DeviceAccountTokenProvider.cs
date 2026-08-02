using global::Android.Accounts;

namespace FocusPhone.Audio.Google;

// Supplies Google access tokens for the device account chosen during sign-in, using the core
// Android AccountManager (the system's Google account authenticator). No Play Services binding or
// stored secret is required. Scope is full Drive: the app reads the owner's music files and the shared
// voice profile/config, and — for Listening Genius — writes the summarized Taste Profile back to Drive,
// which read-only access can't do. The owner registers the broadened scope on the OAuth client.
public class DeviceAccountTokenProvider
{
    // AccountManager auth-token type: "oauth2:" followed by the space-separated scopes.
    internal const string TokenScope = "oauth2:https://www.googleapis.com/auth/drive";

    private readonly object gate = new();
    private string? cachedToken;
    private DateTimeOffset expiresAt;

    public Task<string> GetAccessToken()
    {
        lock (gate)
        {
            if (cachedToken is not null && DateTimeOffset.UtcNow < expiresAt)
                return Task.FromResult(cachedToken);
        }

        var email = MusicSettingsStore.GoogleAccountEmail;
        if (string.IsNullOrEmpty(email))
            throw new InvalidOperationException("Sign in with Google first.");

        return Task.Run(() =>
        {
            var manager = AccountManager.Get(global::Android.App.Application.Context)!;
            var account = new Account(email, "com.google");

            // Drop any cached token first so we never hand out a stale one, then fetch fresh.
            lock (gate)
            {
                if (cachedToken is not null)
                    manager.InvalidateAuthToken("com.google", cachedToken);
            }

            var token = manager.BlockingGetAuthToken(account, TokenScope, true)
                ?? throw new InvalidOperationException(
                    "Could not get a Google token for the device account. Sign in with Google again.");

            lock (gate)
            {
                cachedToken = token;
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(45);
            }
            return token;
        });
    }
}
