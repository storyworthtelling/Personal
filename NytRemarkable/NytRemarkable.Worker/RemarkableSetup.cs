using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace NytRemarkable.Worker;

// Run with --setup to perform first-time reMarkable device registration.
// Visit https://my.remarkable.com/device/mobile/connect to get the one-time code.
public static class RemarkableSetup
{
    private const string RegisterUrl =
        "https://webapp-prod.cloud.remarkable.engineering/token/json/2/device/new";

    public static async Task RegisterDevice(IServiceProvider services)
    {
        Console.Write("One-time code from my.remarkable.com/device/mobile/connect: ");
        var code = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(code))
        {
            Console.Error.WriteLine("No code entered. Aborting.");
            return;
        }

        var httpClientFactory = services.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient();

        var body = JsonSerializer.Serialize(new
        {
            code,
            deviceDesc = "desktop-linux",
            deviceID = Guid.NewGuid().ToString()
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, RegisterUrl);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"Registration failed: {(int)response.StatusCode} ({response.StatusCode})");
            return;
        }

        var raw = await response.Content.ReadAsStringAsync();
        var deviceToken = raw.Trim().Trim('"');

        Console.WriteLine();
        Console.WriteLine("Registration successful. Set this as your device token:");
        Console.WriteLine();
        Console.WriteLine(deviceToken);
        Console.WriteLine();
        Console.WriteLine("Add to configuration:");
        Console.WriteLine("  dotnet user-secrets set \"Remarkable:DeviceToken\" \"<token above>\"");
        Console.WriteLine("  or set environment variable:  Remarkable__DeviceToken=<token above>");
    }
}
