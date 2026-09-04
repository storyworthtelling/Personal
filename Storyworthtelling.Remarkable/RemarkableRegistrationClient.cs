using System.Text;
using System.Text.Json;

namespace Storyworthtelling.Remarkable;

// Wraps the one-time device-code registration POST (previously hand-inlined in each app's settings page), so
// every app pairs through the shared library instead of duplicating the request.
public class RemarkableRegistrationClient : IRemarkableRegistration
{
    private const string DeviceTokenUrl =
        "https://webapp-prod.cloud.remarkable.engineering/token/json/2/device/new";
    private const string DeviceDescription = "mobile-android";

    private readonly HttpClient httpClient;

    public RemarkableRegistrationClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<string> Register(string oneTimeCode, CancellationToken cancellationToken = default)
    {
        var body = JsonSerializer.Serialize(new
        {
            code = oneTimeCode.Trim(),
            deviceDesc = DeviceDescription,
            deviceID = Guid.NewGuid().ToString()
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, DeviceTokenUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"reMarkable registration failed: {(int)response.StatusCode} ({response.StatusCode})",
                inner: null,
                response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        return raw.Trim().Trim('"');
    }
}
