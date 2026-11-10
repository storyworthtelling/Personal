using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using Storyworthtelling.Voice.Health;

namespace Storyworthtelling.Remarkable;

public class RemarkableAuthService : IRemarkableAuthProvider
{
    private const string UserTokenUrl =
        "https://webapp-prod.cloud.remarkable.engineering/token/json/2/user/new";

    private readonly HttpClient httpClient;
    private readonly IOptions<RemarkableOptions> options;
    private readonly ICredentialHealthSink? healthSink;
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    private string? userToken;
    private string? exchangedFor;
    private DateTimeOffset tokenExpiry = DateTimeOffset.MinValue;

    public RemarkableAuthService(
        HttpClient httpClient, IOptions<RemarkableOptions> options, ICredentialHealthSink? healthSink = null)
    {
        this.httpClient = httpClient;
        // Kept as the options, not their value: a re-pair takes effect on the next exchange, with no restart.
        this.options = options;
        this.healthSink = healthSink;
    }

    public async Task<string> GetUserToken(CancellationToken cancellationToken)
    {
        var deviceToken = options.Value.DeviceToken;
        if (IsCurrent(deviceToken))
            return userToken!;

        await refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (IsCurrent(deviceToken))
                return userToken!;

            return await FetchUserToken(deviceToken, cancellationToken);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    // A session token is reused only for the pairing it was exchanged for, and only until five minutes before it expires.
    private bool IsCurrent(string deviceToken) =>
        userToken != null && exchangedFor == deviceToken && DateTimeOffset.UtcNow < tokenExpiry.AddMinutes(-5);

    private async Task<string> FetchUserToken(string deviceToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, UserTokenUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // The device token is the credential exchanged here, so a 401/403 is that token being rejected.
            if (AuthFailure.IsHttpCredentialRejection((int)response.StatusCode, null))
                healthSink?.MarkFailing(RemarkableCredentialId.Value, $"{(int)response.StatusCode} {response.StatusCode}");
            throw new HttpRequestException(
                $"Failed to obtain reMarkable user token: {(int)response.StatusCode} ({response.StatusCode})",
                inner: null,
                response.StatusCode);
        }

        healthSink?.MarkOk(RemarkableCredentialId.Value);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = raw.Trim().Trim('"');
        var (_, expiry) = JwtPayload.Parse(token);

        userToken = token;
        exchangedFor = deviceToken;
        tokenExpiry = expiry;
        return token;
    }
}
