namespace Storyworthtelling.Google;

/// <summary>The shared current-weather read the flagship's workout adaptation (and anything else) consumes.</summary>
public interface IWeather
{
    /// <summary>
    /// Current conditions at a location, or null when weather is unavailable (no API key yet, or a failed
    /// call) — the suite degrades quietly rather than throwing, so a missing key never breaks a screen.
    /// </summary>
    Task<WeatherReading?> GetCurrentAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}

/// <summary>A minimal current-weather snapshot: temperature in Celsius, a human condition, humidity when reported.</summary>
public sealed record WeatherReading(double TemperatureCelsius, string Condition, int? RelativeHumidity);
