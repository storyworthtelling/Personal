using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Codex.Domain.Music;

namespace Codex.Infrastructure.Music;

/// <summary>The code and link the pairing screen shows the owner.</summary>
public sealed record TidalDeviceAuthorization(
    string DeviceCode, string UserCode, string VerificationUri, string VerificationUriComplete,
    int ExpiresIn, int Interval);

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
    string? UserId = null);

/// <summary>
/// TIDAL's device-code sign-in, against <c>auth.tidal.com/v1/</c> directly rather than
/// through the bound Kotlin auth module: the SDK's suspend functions bind as raw
/// <c>Kotlin.Coroutines.IContinuation</c> methods with no managed async wrapper, which
/// makes them exercisable only on a device — the same reasoning that keeps the
/// catalogue lookup off the binding. Device-code is the flow the locked-down phone can
/// actually use: the phone shows a code, the owner approves it on another device's
/// browser, and this polls until that approval lands.
/// </summary>
public sealed class TidalDeviceAuthClient(HttpClient httpClient)
{
    private const string DeviceAuthUrl = "https://auth.tidal.com/v1/oauth2/device_authorization";
    private const string TokenUrl = "https://auth.tidal.com/v1/oauth2/token";

    // r_usr/w_usr read and write the account's own data; w_sub is what lets playback
    // charge against the account's subscription rather than only reading its catalogue.
    private const string Scope = "r_usr w_usr w_sub";

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Asks TIDAL for a device code. <paramref name="clientSecret"/> authenticates the
    /// client, and is required: RFC 8628 section 3.1 says a confidential client MUST
    /// authenticate at this endpoint as well as at the token endpoint, and Score is
    /// confidential because it holds a secret. Only a client registered without one
    /// passes an empty string, and then no header is sent — an empty Basic credential
    /// is a different wrong request, not a neutral one.
    /// </summary>
    public async Task<TidalDeviceAuthorization> RequestDeviceCodeAsync(
        string clientId, string clientSecret, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, DeviceAuthUrl)
        {
            Content = new FormUrlEncodedContent([
                new("client_id", clientId),
                new("scope", Scope),
            ]),
        };

        if (!string.IsNullOrEmpty(clientSecret))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}")));
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // EnsureSuccessStatusCode would drop the body, and the body is the only
            // part that says WHY. A bare "400" is what sent the last round of this
            // back with nothing to act on.
            throw new HttpRequestException(
                $"TIDAL device_authorization refused the request with {(int)response.StatusCode} " +
                $"{response.StatusCode}: {Describe(json)}");
        }

        var reply = JsonSerializer.Deserialize<DeviceAuthReply>(json, SerializerOptions)
            ?? throw new HttpRequestException("TIDAL device_authorization returned an empty body.");

        return new TidalDeviceAuthorization(
            reply.DeviceCode, reply.UserCode, reply.VerificationUri, reply.VerificationUriComplete,
            reply.ExpiresIn, reply.Interval);
    }

    /// <summary>One poll of the token endpoint for a device code still being approved.</summary>
    public Task<TidalTokenPollResult> PollAsync(
        string clientId, string clientSecret, string deviceCode, CancellationToken cancellationToken) =>
        PostToken(clientId, clientSecret, new FormUrlEncodedContent([
            new("client_id", clientId),
            new("device_code", deviceCode),
            new("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
            new("scope", Scope),
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

    /// <summary>
    /// Polls until the owner approves, is denied, or the code's window runs out.
    ///
    /// <paramref name="delay"/> is injected rather than calling <see cref="Task.Delay(TimeSpan)"/>
    /// directly so a test can drive the whole wait through instantly; the budget it is
    /// spent against is the sum of intervals actually awaited, not the wall clock, so
    /// that same fake delay also governs when the loop gives up.
    /// </summary>
    public async Task<TidalTokenPollResult> PollUntilAuthorizedAsync(
        string clientId, string clientSecret, string deviceCode,
        int intervalSeconds, int expiresInSeconds,
        Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(intervalSeconds);
        var budget = TimeSpan.FromSeconds(expiresInSeconds);
        var elapsed = TimeSpan.Zero;

        while (true)
        {
            var result = await PollAsync(clientId, clientSecret, deviceCode, cancellationToken);
            if (result.Status is TidalTokenPollStatus.Success or TidalTokenPollStatus.Denied)
            {
                return result;
            }

            // RFC 8628: on slow_down, widen the interval by at least 5s before trying again.
            if (result.Status == TidalTokenPollStatus.SlowDown)
            {
                interval += TimeSpan.FromSeconds(5);
            }

            elapsed += interval;
            if (elapsed >= budget)
            {
                return new TidalTokenPollResult(TidalTokenPollStatus.Expired);
            }

            await delay(interval, cancellationToken);
        }
    }

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

        return new TidalTokenPollResult(TidalPairing.Interpret(reply?.Error) switch
        {
            TidalPairingSignal.Pending => TidalTokenPollStatus.Pending,
            TidalPairingSignal.SlowDown => TidalTokenPollStatus.SlowDown,
            TidalPairingSignal.Denied => TidalTokenPollStatus.Denied,
            TidalPairingSignal.Expired => TidalTokenPollStatus.Expired,
            _ => TidalTokenPollStatus.Error,
        });
    }

    private sealed record DeviceAuthReply(
        string DeviceCode, string UserCode, string VerificationUri, string VerificationUriComplete,
        int ExpiresIn, int Interval);

    private sealed record TokenReply(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        string? Error,
        TokenUser? User);

    private sealed record TokenUser(long UserId);

    private sealed record ErrorReply(
        string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
}
