using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.Domain.Music;

namespace Codex.Infrastructure.Music;

/// <summary>What one poll of the token endpoint came back as.</summary>
public enum TidalTokenPollStatus
{
    /// <summary>The owner has not approved the code yet.</summary>
    Pending,

    /// <summary>Approved. <see cref="TidalTokenPollResult.AccessToken"/> and
    /// <see cref="TidalTokenPollResult.RefreshToken"/> are set.</summary>
    Success,

    /// <summary>Polling too fast; the caller should widen its interval.</summary>
    SlowDown,

    /// <summary>The owner declined, or the app's access was revoked.</summary>
    Denied,

    /// <summary>The device code's own window closed. Needs a fresh code, not another poll.</summary>
    Expired,

    /// <summary>A response the pairing flow was not written against.</summary>
    Error,
}

public sealed record TidalTokenPollResult(
    TidalTokenPollStatus Status,
    string? AccessToken = null,
    string? RefreshToken = null,
    int ExpiresIn = 0,
    string? UserId = null,
    string? Detail = null);

/// <summary>
/// TIDAL's token endpoint, against <c>auth.tidal.com/v1/</c> directly rather than through the bound
/// Kotlin auth module: the SDK's suspend functions bind as raw <c>Kotlin.Coroutines.IContinuation</c>
/// methods with no managed async wrapper, which makes them exercisable only on a device. Two grants: the
/// PKCE sign-in's code exchange and the refresh. The device-code flow it once also ran was superseded by
/// PKCE and removed (orphan audit §18).
/// </summary>
public sealed class TidalAuthClient(HttpClient httpClient)
{
    private const string TokenUrl = "https://auth.tidal.com/v1/oauth2/token";

    // One list for every grant, decided where the sign-in is (see TidalSignIn.Scope for
    // why it is the developer-portal set).
    private const string Scope = TidalSignIn.Scope;

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Exchanges the authorization code the browser redirect carried for tokens,
    /// proving the exchange belongs to the sign-in that started it by sending the
    /// PKCE verifier — the secret the redirect could not carry.
    /// </summary>
    public Task<TidalTokenPollResult> ExchangeCodeAsync(
        string clientId, string clientSecret, string code, string codeVerifier, string redirectUri,
        CancellationToken cancellationToken) =>
        PostToken(clientId, clientSecret, new FormUrlEncodedContent([
            new("client_id", clientId),
            new("grant_type", "authorization_code"),
            new("code", code),
            new("code_verifier", codeVerifier),
            new("redirect_uri", redirectUri),
        ]), cancellationToken);

    /// <summary>Exchanges the long-lived refresh token stored from pairing for a fresh
    /// access token. Called once per session, the same shape as a reMarkable device
    /// token being exchanged for a short-lived user JWT.</summary>
    public Task<TidalTokenPollResult> RefreshAsync(
        string clientId, string clientSecret, string refreshToken, CancellationToken cancellationToken) =>
        PostToken(clientId, clientSecret, new FormUrlEncodedContent([
            new("client_id", clientId),
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken),
        ]), cancellationToken);

    // OAuth errors are {"error","error_description"}; anything else is quoted whole so
    // an unexpected shape still reaches the person reading the log.
    private static string Describe(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(empty body)";
        }

        try
        {
            var reply = JsonSerializer.Deserialize<ErrorReply>(body, SerializerOptions);
            if (!string.IsNullOrWhiteSpace(reply?.Error))
            {
                return string.IsNullOrWhiteSpace(reply.ErrorDescription)
                    ? reply.Error
                    : $"{reply.Error} — {reply.ErrorDescription}";
            }
        }
        catch (JsonException)
        {
            // Not JSON. The raw body is still the most useful thing to carry.
        }

        return body.Length > 400 ? body[..400] : body;
    }

    private async Task<TidalTokenPollResult> PostToken(
        string clientId, string clientSecret, FormUrlEncodedContent content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var reply = JsonSerializer.Deserialize<TokenReply>(json, SerializerOptions);

        if (!string.IsNullOrEmpty(reply?.AccessToken))
        {
            return new TidalTokenPollResult(
                TidalTokenPollStatus.Success, reply.AccessToken, reply.RefreshToken, reply.ExpiresIn,
                reply.User?.UserId.ToString());
        }

        var status = TidalPairing.Interpret(reply?.Error) switch
        {
            TidalPairingSignal.Pending => TidalTokenPollStatus.Pending,
            TidalPairingSignal.SlowDown => TidalTokenPollStatus.SlowDown,
            TidalPairingSignal.Denied => TidalTokenPollStatus.Denied,
            TidalPairingSignal.Expired => TidalTokenPollStatus.Expired,
            _ => TidalTokenPollStatus.Error,
        };

        // The five RFC 8628 device-flow codes above cover the polling loop's own retry decisions,
        // but this never checked whether the call actually succeeded, and discarded the body on
        // anything it didn't recognize — so the browser/PKCE exchange endpoint's OWN error shape
        // (a numeric subStatus + userMessage, no "error" field at all, e.g. TIDAL's "1002") fell
        // through to a bare Error status with nothing TIDAL actually said kept anywhere. That is
        // why "1002 Error" reached the owner with nothing in Settings or Diagnostics to show for
        // it. Populated whenever the call didn't succeed, regardless of which shape TIDAL used.
        var detail = response.IsSuccessStatusCode
            ? null
            : reply switch
            {
                { SubStatus: { } subStatus } => string.IsNullOrWhiteSpace(reply.UserMessage)
                    ? $"TIDAL error {subStatus}"
                    : $"TIDAL error {subStatus}: {reply.UserMessage}",
                _ => Describe(json),
            };

        return new TidalTokenPollResult(status, Detail: detail);
    }

    private sealed record TokenReply(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        string? Error,
        // TIDAL's own REST error shape on this endpoint — distinct from the RFC 8628 "error"
        // field above, and case-insensitively matched against the reply's camelCase keys.
        int? SubStatus,
        string? UserMessage,
        TokenUser? User);

    private sealed record TokenUser(long UserId);

    private sealed record ErrorReply(
        string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
}
