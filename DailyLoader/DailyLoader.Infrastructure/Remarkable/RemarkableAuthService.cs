using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace DailyLoader.Infrastructure.Remarkable;

public class RemarkableAuthService : IRemarkableAuthProvider
{
    private const string UserTokenUrl =
        "https://webapp-prod.cloud.remarkable.engineering/token/json/2/user/new";

    private readonly HttpClient httpClient;
    private readonly RemarkableOptions options;
    private readonly SemaphoreSlim refreshLock = new(1, 1);

    private string? userToken;
    private DateTimeOffset tokenExpiry = DateTimeOffset.MinValue;

    public RemarkableAuthService(HttpClient httpClient, IOptions<RemarkableOptions> options)
    {
        this.httpClient = httpClient;
        this.options = options.Value;
    }

    public async Task<string> GetUserToken()
    {
        if (userToken != null && DateTimeOffset.UtcNow < tokenExpiry.AddMinutes(-5))
            return userToken;

        await refreshLock.WaitAsync();
        try
        {
            if (userToken != null && DateTimeOffset.UtcNow < tokenExpiry.AddMinutes(-5))
                return userToken;

            return await FetchUserToken();
        }
        finally
        {
            refreshLock.Release();
        }
    }

    private async Task<string> FetchUserToken()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, UserTokenUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.DeviceToken);
        request.Content = new StringContent(string.Empty, Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Failed to obtain reMarkable user token: {(int)response.StatusCode} ({response.StatusCode})",
                inner: null,
                response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        var token = raw.Trim().Trim('"');
        var (_, expiry) = JwtPayload.Parse(token);

        userToken = token;
        tokenExpiry = expiry;
        return token;
    }
}
