using System.Text.Json;

namespace Storyworthtelling.Google;

/// <summary>
/// Reads current conditions from the Google Weather API (weather.googleapis.com, public since 2025). The API
/// key is read live (from the vault, app-side) via a provider delegate so a rotated key is picked up without
/// a restart; a null/blank key or any failure degrades to a null reading rather than throwing.
/// </summary>
public sealed class GoogleWeatherClient : IWeather
{
    private const string Endpoint = "https://weather.googleapis.com/v1/currentConditions:lookup";

    private readonly HttpClient http;
    private readonly Func<CancellationToken, Task<string?>> apiKeyProvider;

    public GoogleWeatherClient(HttpClient http, Func<CancellationToken, Task<string?>> apiKeyProvider)
    {
        this.http = http;
        this.apiKeyProvider = apiKeyProvider;
    }

    public async Task<WeatherReading?> GetCurrentAsync(
        double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        var key = await apiKeyProvider(cancellationToken);
        if (string.IsNullOrWhiteSpace(key))
            return null;

        try
        {
            var url = $"{Endpoint}?key={Uri.EscapeDataString(key)}" +
                      $"&location.latitude={latitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                      $"&location.longitude={longitude.ToString(System.Globalization.CultureInfo.InvariantCulture)}" +
                      "&unitsSystem=METRIC";
            using var response = await http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return Parse(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Weather is a nicety; a network blip or a shape change never breaks the caller's screen.
            return null;
        }
    }

    // Parses the documented shape defensively: temperature.degrees (Celsius under METRIC),
    // weatherCondition.description.text, relativeHumidity. Missing pieces degrade rather than throw.
    private static WeatherReading? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("temperature", out var temp) ||
            !temp.TryGetProperty("degrees", out var degrees) ||
            !degrees.TryGetDouble(out var celsius))
            return null;

        var condition = root.TryGetProperty("weatherCondition", out var wc) &&
                        wc.TryGetProperty("description", out var desc) &&
                        desc.TryGetProperty("text", out var text)
            ? text.GetString() ?? string.Empty
            : string.Empty;

        int? humidity = root.TryGetProperty("relativeHumidity", out var rh) && rh.TryGetInt32(out var h)
            ? h
            : null;

        return new WeatherReading(celsius, condition, humidity);
    }
}
