using Android.Gms.Auth;
using DailyLoader.Infrastructure.Google;

namespace DailyLoader.Android.Google;

// Supplies Google access tokens for the device account selected during sign-in. Google Play
// Services issues and refreshes the tokens, so the app stores no client secret or refresh token.
public class PlayServicesGoogleTokenProvider : IGoogleAccessTokenProvider
{
    // "oauth2:" prefix followed by the space-separated scopes Play Services should mint a token for.
    private const string TokenScope =
        "oauth2:https://www.googleapis.com/auth/calendar https://www.googleapis.com/auth/tasks";

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

        var email = CredentialStore.GoogleAccountEmail;
        if (string.IsNullOrEmpty(email))
            throw new InvalidOperationException("Sign in with Google first.");

        return Task.Run(() =>
        {
            var context = global::Android.App.Application.Context;
            var token = GoogleAuthUtil.GetToken(context, email, TokenScope)
                ?? throw new InvalidOperationException("Google Play Services returned no access token.");

            lock (gate)
            {
                cachedToken = token;
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(50); // Play Services tokens last ~1 hour
            }
            return token;
        });
    }
}
