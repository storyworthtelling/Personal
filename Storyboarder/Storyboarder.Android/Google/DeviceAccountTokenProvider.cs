using global::Android.Accounts;

namespace Storyboarder.Android.Google;

// Supplies Google access tokens for the device account chosen during sign-in, using the core
// Android AccountManager (the system's Google account authenticator). No Play Services binding or
// stored secret is required. The token is good for both reading Docs and listing Drive files.
public class DeviceAccountTokenProvider
{
    // AccountManager auth-token type: "oauth2:" followed by the space-separated scopes.
    //
    // drive.file is a WRITE scope, needed to archive finished books beside their manuscript. It is the
    // narrowest write scope Drive offers — it grants access only to files this app itself creates, not to
    // the rest of the user's Drive — so reading the manuscript still relies on drive.readonly.
    //
    // Adding a scope changes what the consent screen asks for: it must also be registered on the OAuth
    // client in the Google Cloud project, and the user has to re-consent on the device. Until both happen,
    // token requests including this scope fail, which is why archiving defaults to off (ArchiveOptions).
    internal const string TokenScope =
        "oauth2:https://www.googleapis.com/auth/documents.readonly " +
        "https://www.googleapis.com/auth/drive.readonly " +
        "https://www.googleapis.com/auth/drive.file";

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

        var email = SettingsStore.GoogleAccountEmail;
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
                    "Could not get a Google token for the device account. Open Settings and sign in again.");

            lock (gate)
            {
                cachedToken = token;
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(45);
            }
            return token;
        });
    }
}
