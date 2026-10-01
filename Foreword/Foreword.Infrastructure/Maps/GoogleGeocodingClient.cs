using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Foreword.Infrastructure.Maps;

/// <summary>
/// An address to a point, from the Geocoding API — the forecast, pollen and air-quality calls take a
/// latitude and longitude, and asking the owner to copy one out of a map was the step this replaces.
///
/// <para>Same key as the rest of Maps Platform, with the Geocoding API enabled on the project. Most of
/// its failures arrive as HTTP 200 with a <c>status</c> — <c>ZERO_RESULTS</c> for an address it cannot
/// place, <c>REQUEST_DENIED</c> for a key without the API — so the status is read, not the HTTP code.
/// Neither marks the key as failing: a key that works for routes and weather but has not had
/// Geocoding switched on is a working key, and flagging it would put a "paste your key" box on a
/// screen whose key is fine.</para>
/// </summary>
public class GoogleGeocodingClient(HttpClient httpClient, IOptions<MapsOptions> options)
{
    private const string Endpoint = "https://maps.googleapis.com/maps/api/geocode/json";

    private readonly MapsOptions options = options.Value;

    public async Task<(double Latitude, double Longitude)?> Locate(string address)
    {
        if (!options.IsConfigured || string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var uri = $"{Endpoint}?address={Uri.EscapeDataString(address.Trim())}"
            + $"&key={Uri.EscapeDataString(options.ApiKey)}";

        using var response = await httpClient.GetAsync(uri);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        if (!root.TryGetProperty("status", out var status) || status.GetString() != "OK"
            || !root.TryGetProperty("results", out var results)
            || results.ValueKind != JsonValueKind.Array
            || results.GetArrayLength() == 0)
        {
            return null;
        }

        var location = results[0].GetProperty("geometry").GetProperty("location");

        return (location.GetProperty("lat").GetDouble(), location.GetProperty("lng").GetDouble());
    }
}
