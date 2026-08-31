using global::Android.Accounts;

namespace Storyboarder.Android.Google;

// Supplies Google access tokens for the device account chosen during sign-in, using the core
// Android AccountManager (the system's Google account authenticator). No Play Services binding or
// stored secret is required. The token is good for both reading Docs and listing Drive files.
public class DeviceAccountTokenProvider
{
    // AccountManager auth-token type: "oauth2:" followed by the space-separated scopes.
    //
    // Full drive (read/write) rather than the narrower drive.readonly + drive.file. The narrow pair only
    // ever sees files THIS app created — which is fine for reading a manuscript and archiving beside it, but
    // cannot reach the ONE shared owner's-voice profile file that every com.storyworthtelling app must read
    // and write (drive.file scopes each app to its own copy). Full drive lets all the apps find that single
    // file by name (see Storyworthtelling.Voice/DriveTextFile). It supersedes drive.readonly and drive.file.
    //
    // Changing scopes changes what the consent screen asks for: each scope must also be registered on the
    // OAuth client in the Google Cloud project, and the user has to re-consent on the device. Until both
    // happen, token requests including a scope fail (which is why archiving defaults to off, ArchiveOptions,
    // and why a voice profile that can't be reached simply degrades to neutral copy).
    internal const string TokenScope =
        "oauth2:https://www.googleapis.com/auth/documents.readonly " +
        "https://www.googleapis.com/auth/drive";

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
