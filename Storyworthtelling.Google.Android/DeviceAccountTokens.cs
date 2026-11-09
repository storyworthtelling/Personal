using global::Android.Accounts;

namespace Storyworthtelling.Google.Android;

/// <summary>
/// Google access tokens for the device account chosen at sign-in, through the core Android AccountManager (no Play
/// Services binding, no stored secret). Four heads and the native voice account each kept a copy differing only in
/// the scopes and the wording (audit 01 §10). The cache rule is <see cref="CachedAccessToken"/>.
/// </summary>
/// <param name="accountEmail">The signed-in account, read on every call.</param>
/// <param name="tokenType">An <c>oauth2:</c> token type, from <see cref="GoogleScopes.ToTokenType"/>.</param>
/// <param name="notSignedIn">What to say when nobody has signed in yet.</param>
public sealed class DeviceAccountTokens(Func<string?> accountEmail, string tokenType, string notSignedIn = "Sign in with Google first.")
{
    private const string GoogleAccountType = "com.google";

    private readonly CachedAccessToken cache = new();

    public string TokenType { get; } = tokenType;

    public Task<string> GetAccessToken()
    {
        var email = accountEmail();
        if (string.IsNullOrEmpty(email))
        {
            throw new InvalidOperationException(notSignedIn);
        }

        return cache.GetAsync(stale =>
        {
            var manager = AccountManager.Get(global::Android.App.Application.Context)!;
            if (stale is not null)
            {
                // Drop the cached token first so a stale one is never handed out again.
                manager.InvalidateAuthToken(GoogleAccountType, stale);
            }

            return manager.BlockingGetAuthToken(new Account(email, GoogleAccountType), TokenType, true)
                ?? throw new InvalidOperationException(
                    "Could not get a Google token for the device account. Sign in with Google again.");
        });
    }
}
