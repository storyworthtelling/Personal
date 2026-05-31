using System.Security.Cryptography;
using System.Text;
using DailyLoader.Infrastructure.Google;
using Microsoft.Maui.Authentication;

namespace DailyLoader.Android.Google;

// Drives the in-app Google consent flow: opens the system browser via WebAuthenticator with a
// PKCE challenge, receives the authorization code on the custom scheme, and exchanges it for a
// refresh token (which is stored in CredentialStore).
public static class GoogleOAuthFlow
{
    private const string AuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string Scopes =
        "https://www.googleapis.com/auth/calendar https://www.googleapis.com/auth/tasks";

    public static async Task<string> SignIn(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new InvalidOperationException("Enter your Google client id first.");

        var verifier = RandomUrlSafe(64);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var redirectUri = $"{ReverseClientId(clientId)}:/oauth2redirect";

        var authUrl =
            $"{AuthEndpoint}?client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            "&response_type=code" +
            $"&scope={Uri.EscapeDataString(Scopes)}" +
            "&access_type=offline&prompt=consent" +
            $"&code_challenge={challenge}&code_challenge_method=S256";

        var result = await WebAuthenticator.Default.AuthenticateAsync(
            new Uri(authUrl), new Uri(redirectUri));

        if (!result.Properties.TryGetValue("code", out var code) || string.IsNullOrEmpty(code))
            throw new InvalidOperationException("Google sign-in did not return an authorization code.");

        using var http = new HttpClient(new Xamarin.Android.Net.AndroidMessageHandler());
        var refreshToken = await GoogleAccessTokenProvider.ExchangeCodeForRefreshToken(
            http, clientId, clientSecret: null, code, verifier, redirectUri);

        CredentialStore.GoogleRefreshToken = refreshToken;
        return refreshToken;
    }

    // "123-abc.apps.googleusercontent.com" -> "com.googleusercontent.apps.123-abc"
    private static string ReverseClientId(string clientId)
    {
        const string suffix = ".apps.googleusercontent.com";
        var prefix = clientId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? clientId[..^suffix.Length]
            : clientId;
        return $"com.googleusercontent.apps.{prefix}";
    }

    private static string RandomUrlSafe(int byteLength) =>
        Base64Url(RandomNumberGenerator.GetBytes(byteLength));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
