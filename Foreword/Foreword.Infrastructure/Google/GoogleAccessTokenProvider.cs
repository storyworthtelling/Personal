using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Foreword.Infrastructure.Google;

// Exchanges the stored refresh token for short-lived access tokens against Google's OAuth
// token endpoint, caching the access token until shortly before it expires. The client secret
// is sent only when present, so this works for both desktop clients (id + secret) and native
// Android clients (id + PKCE, no secret). The HttpClient is injected so the Android host can
// route it through AndroidMessageHandler.
public class GoogleAccessTokenProvider : IGoogleAccessTokenProvider
{
    public const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private readonly HttpClient httpClient;
    private readonly GoogleOptions options;

    private string? cachedToken;
    private DateTimeOffset expiresAt;

    public GoogleAccessTokenProvider(HttpClient httpClient, IOptions<GoogleOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public async Task<string> GetAccessToken()
    {
        if (cachedToken is not null && DateTimeOffset.UtcNow < expiresAt)
            return cachedToken;

        if (string.IsNullOrWhiteSpace(options.RefreshToken))
            throw new InvalidOperationException("No Google refresh token configured. Sign in with Google first.");

        var fields = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = options.RefreshToken
        };
        if (!string.IsNullOrEmpty(options.ClientSecret))
            fields["client_secret"] = options.ClientSecret;

        using var response = await httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(fields));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Google token refresh failed: {(int)response.StatusCode} ({response.StatusCode}): {body}",
                inner: null,
                response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        var token = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Google token response had no access_token.");
        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;

        cachedToken = token;
        expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60); // refresh a minute early
        return token;
    }

    // Exchanges an authorization code (from the in-app consent flow) for a refresh token.
    // Static so the Android sign-in glue can call it without resolving the provider.
    public static async Task<string> ExchangeCodeForRefreshToken(
        HttpClient httpClient,
        string clientId,
        string? clientSecret,
        string code,
        string codeVerifier,
        string redirectUri)
    {
        var fields = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri
        };
        if (!string.IsNullOrEmpty(clientSecret))
            fields["client_secret"] = clientSecret;

        using var response = await httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(fields));
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Google code exchange failed: {(int)response.StatusCode} ({response.StatusCode}): {body}",
                inner: null,
                response.StatusCode);

        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("refresh_token", out var rt) && rt.GetString() is { } value
            ? value
            : throw new InvalidOperationException(
                "Google did not return a refresh_token. Ensure access_type=offline and prompt=consent were used.");
    }
}
