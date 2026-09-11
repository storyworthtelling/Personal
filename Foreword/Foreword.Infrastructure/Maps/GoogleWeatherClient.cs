using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Weather;

namespace Foreword.Infrastructure.Maps;

/// <summary>
/// The weather page's four calls: the daily forecast, the hourly forecast, pollen, and air quality.
///
/// <para>Four separate Maps Platform APIs behind one seam, because the page does not care which
/// call a number came from. Each is awaited separately and each failure is swallowed on its own —
/// the document goes out at half past six whatever any one service is doing, and a morning with no
/// forecast still has a calendar on it.</para>
///
/// <para>Every field name here was read out of the published reference rather than remembered. A
/// JSON reader that guesses does not throw: it returns an empty page and no error at all.</para>
/// </summary>
public class GoogleWeatherClient(HttpClient httpClient, IOptions<MapsOptions> options) : IWeatherClient
{
    private const string WeatherBase = "https://weather.googleapis.com/v1";
    private const string PollenBase = "https://pollen.googleapis.com/v1";
    private const string AirQualityBase = "https://airquality.googleapis.com/v1";

    // The week strip is seven days; the daily endpoint allows up to ten.
    private const int WeekDays = 7;

    // The timeline draws 6am to 10pm, and the weather page shares its axis so an event and the
    // weather at that hour line up across the two sheets.
    private const int FirstHour = 6;
    private const int LastHour = 22;

    private readonly MapsOptions options = options.Value;

    public async Task<WeatherReport> GetReport(DateOnly today)
    {
        if (!options.HasLocation)
        {
            return WeatherReport.Nothing;
        }

        var daily = await Read(DailyUrl());
        var hourly = await Read(HourlyUrl());
        var pollen = await Read(PollenUrl());
        var air = await Post($"{AirQualityBase}/currentConditions:lookup?key={options.ApiKey}", AirBody());

        var week = daily is null ? [] : Week(daily.Value);
        var hours = hourly is null ? [] : Hours(hourly.Value, today);

        var allergy = new List<AllergyReading>();
        if (pollen is not null) allergy.AddRange(Pollen(pollen.Value));
        if (air is not null && AirQuality(air.Value) is { } reading) allergy.Add(reading);

        return new WeatherReport
        {
            Today = week.Count == 0 && hours.Count == 0 ? null : Today(today, week, hours, daily),
            Week = week,
            Allergy = allergy,
        };
    }

    // ---- the calls ----

    private string DailyUrl() =>
        $"{WeatherBase}/forecast/days:lookup?key={options.ApiKey}&{Location()}"
        + $"&days={WeekDays}&pageSize={WeekDays}&unitsSystem=IMPERIAL";

    private string HourlyUrl() =>
        $"{WeatherBase}/forecast/hours:lookup?key={options.ApiKey}&{Location()}"
        + $"&hours=24&pageSize=24&unitsSystem=IMPERIAL";

    private string PollenUrl() =>
        $"{PollenBase}/forecast:lookup?key={options.ApiKey}"
        + $"&location.latitude={Number(options.HomeLatitude)}"
        + $"&location.longitude={Number(options.HomeLongitude)}&days=1";

    private string AirBody() =>
        $$"""
        {"location":{"latitude":{{Number(options.HomeLatitude)}},"longitude":{{Number(options.HomeLongitude)}}},
         "languageCode":"en"}
        """;

    private string Location() =>
        $"location.latitude={Number(options.HomeLatitude)}&location.longitude={Number(options.HomeLongitude)}";

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A failed call is nothing rather than an exception. Each of the four answers a different part
    /// of the page and none of them is worth losing the other three for — or the document.
    /// </summary>
    private async Task<JsonElement?> Read(string url)
    {
        try
        {
            var response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return Parse(await response.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private async Task<JsonElement?> Post(string url, string body)
    {
        try
        {
            var response = await httpClient.PostAsync(
                url, new StringContent(body, Encoding.UTF8, "application/json"));
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return Parse(await response.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private static JsonElement? Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---- reading the answers ----

    private static TodayWeather Today(
        DateOnly date, IReadOnlyList<WeekDay> week, IReadOnlyList<HourWeather> hours, JsonElement? daily)
    {
        var first = week.FirstOrDefault(d => d.Date == date) ?? week.FirstOrDefault();
        var peak = hours.Count == 0 ? null : (DateTime?)hours.MaxBy(h => h.Degrees).Hour;

        return new TodayWeather
        {
            Date = date,
            Hours = hours,
            High = first?.High ?? 0,
            Low = first?.Low ?? 0,
            HighAt = peak,
            Condition = first?.Condition ?? string.Empty,
            Sun = daily is null ? new SunAndMoon(null, null, string.Empty) : Sun(daily.Value),
        };
    }

    private static IReadOnlyList<WeekDay> Week(JsonElement daily)
    {
        if (!daily.TryGetProperty("forecastDays", out var days))
        {
            return [];
        }

        return [.. days.EnumerateArray().Select(day => new WeekDay(
            Date(day),
            Text(day, "daytimeForecast", "weatherCondition", "description", "text"),
            Degrees(day, "maxTemperature"),
            Degrees(day, "minTemperature"),
            Percent(day)))];
    }

    private static IReadOnlyList<HourWeather> Hours(JsonElement hourly, DateOnly today)
    {
        if (!hourly.TryGetProperty("forecastHours", out var hours))
        {
            return [];
        }

        return
        [
            .. hours.EnumerateArray()
                .Select(hour => new HourWeather(
                    Start(hour),
                    Round(hour, "temperature"),
                    PrecipitationPercent(hour)))
                // The drawn day only. The endpoint answers in whole hours from now, which before
                // dawn means a run that spills past ten at night into tomorrow morning — hours the
                // chart has no axis for and would otherwise compress the rest of the day to fit.
                .Where(hour => DateOnly.FromDateTime(hour.Hour) == today
                    && hour.Hour.Hour >= FirstHour && hour.Hour.Hour <= LastHour)
                .OrderBy(hour => hour.Hour)
        ];
    }

    private static SunAndMoon Sun(JsonElement daily)
    {
        if (!daily.TryGetProperty("forecastDays", out var days)
            || days.EnumerateArray().FirstOrDefault() is not { ValueKind: JsonValueKind.Object } first)
        {
            return new SunAndMoon(null, null, string.Empty);
        }

        return new SunAndMoon(
            Moment(first, "sunEvents", "sunriseTime"),
            Moment(first, "sunEvents", "sunsetTime"),
            Text(first, "moonEvents", "moonPhase"));
    }

    /// <summary>
    /// Ragweed, grass and tree — and ragweed is the one that needs a different array. Grass and tree
    /// are pollen TYPES; ragweed is a PLANT, so a reader that walked only pollenTypeInfo would print
    /// "Weed" through the whole of ragweed season, which is the reason the row is on the page.
    /// </summary>
    private static IEnumerable<AllergyReading> Pollen(JsonElement pollen)
    {
        if (!pollen.TryGetProperty("dailyInfo", out var days)
            || days.EnumerateArray().FirstOrDefault() is not { ValueKind: JsonValueKind.Object } day)
        {
            yield break;
        }

        if (Index(day, "plantInfo", "RAGWEED") is { } ragweed)
        {
            yield return ragweed with { Name = "Ragweed" };
        }

        if (Index(day, "pollenTypeInfo", "GRASS") is { } grass)
        {
            yield return grass with { Name = "Grass" };
        }

        if (Index(day, "pollenTypeInfo", "TREE") is { } tree)
        {
            yield return tree with { Name = "Tree" };
        }
    }

    private static AllergyReading? Index(JsonElement day, string array, string code)
    {
        if (!day.TryGetProperty(array, out var entries))
        {
            return null;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!string.Equals(Text(entry, "code"), code, StringComparison.Ordinal)
                || !entry.TryGetProperty("indexInfo", out var index))
            {
                continue;
            }

            // Category is the API's own word for the level — "None", "Low", "High" — which is what
            // the page prints. Nothing here invents a phrase for a number.
            return new AllergyReading(
                Text(entry, "displayName"),
                Text(index, "category"),
                index.TryGetProperty("value", out var value) ? value.GetInt32() : 0);
        }

        return null;
    }

    /// <summary>
    /// Air quality shares the pollen meter, and it is not measured on the same scale: an AQI runs to
    /// 100 and beyond where the Universal Pollen Index stops at five. Drawn as it comes it would
    /// fill every step of the bar on a perfectly ordinary day, so it is placed on the five steps by
    /// its own bands — which is also the order the AQI's own categories run in.
    /// </summary>
    private static AllergyReading? AirQuality(JsonElement air)
    {
        if (!air.TryGetProperty("indexes", out var indexes)
            || indexes.EnumerateArray().FirstOrDefault() is not { ValueKind: JsonValueKind.Object } index
            || !index.TryGetProperty("aqi", out var aqi))
        {
            return null;
        }

        var value = aqi.GetInt32();
        var category = Text(index, "category");

        return new AllergyReading(
            "Air quality",
            string.IsNullOrWhiteSpace(category) ? value.ToString(CultureInfo.InvariantCulture)
                : $"{value} · {category}",
            AirSteps(value));
    }

    // The Universal AQI runs the other way from a pollen index — 100 is clean air and 0 is not — so
    // a low number is a full meter, which is what the row's other three readings already mean.
    private static int AirSteps(int universalAqi) => universalAqi switch
    {
        >= 80 => 0,
        >= 60 => 1,
        >= 40 => 2,
        >= 20 => 3,
        >= 1 => 4,
        _ => 5,
    };

    // ---- small readers ----

    private static DateOnly Date(JsonElement day) =>
        day.TryGetProperty("displayDate", out var date)
            ? new DateOnly(
                date.GetProperty("year").GetInt32(),
                date.GetProperty("month").GetInt32(),
                date.GetProperty("day").GetInt32())
            : default;

    private static DateTime Start(JsonElement hour) =>
        hour.TryGetProperty("interval", out var interval)
        && interval.TryGetProperty("startTime", out var start)
        && start.GetString() is { } text
        && DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var moment)
            ? moment.ToLocalTime()
            : default;

    private static TimeOnly? Moment(JsonElement day, params string[] path) =>
        Text(day, path) is { Length: > 0 } text
        && DateTime.TryParse(text, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var moment)
            ? TimeOnly.FromDateTime(moment.ToLocalTime())
            : null;

    // Degrees arrive as a decimal and the page has no room for one. Rounded rather than cast: a low
    // reported a degree colder than forecast every single morning is the kind of wrongness nobody
    // would ever trace back to a truncation.
    private static int Degrees(JsonElement day, string field) =>
        day.TryGetProperty(field, out var temperature)
        && temperature.TryGetProperty("degrees", out var degrees)
            ? (int)Math.Round(degrees.GetDouble(), MidpointRounding.AwayFromZero)
            : 0;

    private static int Round(JsonElement hour, string field) => Degrees(hour, field);

    private static int Percent(JsonElement day) =>
        day.TryGetProperty("daytimeForecast", out var part) ? PrecipitationPercent(part) : 0;

    private static int PrecipitationPercent(JsonElement part) =>
        part.TryGetProperty("precipitation", out var precipitation)
        && precipitation.TryGetProperty("probability", out var probability)
        && probability.TryGetProperty("percent", out var percent)
            ? percent.GetInt32()
            : 0;

    private static string Text(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var step in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(step, out current))
            {
                return string.Empty;
            }
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() ?? string.Empty : string.Empty;
    }
}
