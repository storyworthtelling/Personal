using global::Android.Accounts;

namespace Ledger.Android.Google;

// Supplies Google access tokens for the device account chosen during sign-in, using the core Android
// AccountManager. No Play Services binding is required. Scope is read-only Drive — Ledger only READS the
// shared owner's-voice profile + config the other apps wrote; it never writes to Drive.
public class DeviceAccountTokenProvider
{
    // AccountManager auth-token type: "oauth2:" followed by the space-separated scopes.
    internal const string TokenScope = "oauth2:https://www.googleapis.com/auth/drive.readonly";

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
