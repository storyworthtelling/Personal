using System.Net;
using Foreword.Domain.Weather;
using Foreword.Infrastructure.Maps;
using Microsoft.Extensions.Options;
using Xunit;

namespace Foreword.Tests.Weather;

/// <summary>
/// The Maps Platform weather calls, against responses in the shape the published API actually
/// returns — the field names were read out of the reference before a line of this was written,
/// because a JSON reader that guesses is a reader that returns an empty page and no error.
/// </summary>
public class GoogleWeatherClientTests
{
    private static readonly DateOnly Day = new(2026, 9, 13);

    // The daily forecast, trimmed to the fields the page reads. Shape from
    // weather/reference/rest/v1/forecast.days/lookup.
    private const string DailyJson = """
    {
      "forecastDays": [
        {
          "displayDate": { "year": 2026, "month": 9, "day": 13 },
          "daytimeForecast": {
            "weatherCondition": { "type": "PARTLY_CLOUDY", "description": { "text": "Clearing" } },
            "precipitation": { "probability": { "type": "RAIN", "percent": 30 } }
          },
          "nighttimeForecast": {
            "weatherCondition": { "type": "CLEAR", "description": { "text": "Clear" } },
            "precipitation": { "probability": { "type": "NONE", "percent": 0 } }
          },
          "maxTemperature": { "degrees": 78.2, "unit": "FAHRENHEIT" },
          "minTemperature": { "degrees": 56.6, "unit": "FAHRENHEIT" },
          "sunEvents": {
            "sunriseTime": "2026-09-13T10:32:00Z",
            "sunsetTime": "2026-09-13T23:21:00Z"
          },
          "moonEvents": { "moonPhase": "WANING_GIBBOUS" }
        },
        {
          "displayDate": { "year": 2026, "month": 9, "day": 14 },
          "daytimeForecast": {
            "weatherCondition": { "type": "CLEAR", "description": { "text": "Sunny" } },
            "precipitation": { "probability": { "type": "NONE", "percent": 5 } }
          },
          "maxTemperature": { "degrees": 81.0, "unit": "FAHRENHEIT" },
          "minTemperature": { "degrees": 60.4, "unit": "FAHRENHEIT" }
        }
      ]
    }
    """;

    // The hourly forecast. Shape from weather/reference/rest/v1/forecast.hours/lookup.
    private const string HourlyJson = """
    {
      "forecastHours": [
        {
          "interval": { "startTime": "2026-09-13T10:00:00Z", "endTime": "2026-09-13T11:00:00Z" },
          "displayDateTime": { "year": 2026, "month": 9, "day": 13, "hours": 6 },
          "weatherCondition": { "type": "RAIN", "description": { "text": "Rain" } },
          "temperature": { "degrees": 58.1, "unit": "FAHRENHEIT" },
          "precipitation": { "probability": { "type": "RAIN", "percent": 80 } }
        },
        {
          "interval": { "startTime": "2026-09-13T11:00:00Z", "endTime": "2026-09-13T12:00:00Z" },
          "displayDateTime": { "year": 2026, "month": 9, "day": 13, "hours": 7 },
          "weatherCondition": { "type": "RAIN", "description": { "text": "Rain" } },
          "temperature": { "degrees": 59.4, "unit": "FAHRENHEIT" },
          "precipitation": { "probability": { "type": "RAIN", "percent": 55 } }
        },
        {
          "interval": { "startTime": "2026-09-13T12:00:00Z", "endTime": "2026-09-13T13:00:00Z" },
          "displayDateTime": { "year": 2026, "month": 9, "day": 13, "hours": 8 },
          "weatherCondition": { "type": "CLOUDY", "description": { "text": "Cloudy" } },
          "temperature": { "degrees": 66.9, "unit": "FAHRENHEIT" },
          "precipitation": { "probability": { "type": "NONE", "percent": 5 } }
        },
        {
          "interval": { "startTime": "2026-09-13T13:00:00Z", "endTime": "2026-09-13T14:00:00Z" },
          "displayDateTime": { "year": 2026, "month": 9, "day": 13, "hours": 9 },
          "weatherCondition": { "type": "CLEAR", "description": { "text": "Sunny" } },
          "temperature": { "degrees": 78.4, "unit": "FAHRENHEIT" },
          "precipitation": { "probability": { "type": "NONE", "percent": 0 } }
        }
      ]
    }
    """;

    // The pollen forecast. Ragweed is a PLANT; grass and tree are pollen TYPES — the API reports
    // them in two different arrays, and the page wants one of each.
    private const string PollenJson = """
    {
      "regionCode": "US",
      "dailyInfo": [
        {
          "date": { "year": 2026, "month": 9, "day": 13 },
          "pollenTypeInfo": [
            { "code": "GRASS", "displayName": "Grass",
              "indexInfo": { "code": "UPI", "value": 1, "category": "Low" }, "inSeason": true },
            { "code": "TREE", "displayName": "Tree",
              "indexInfo": { "code": "UPI", "value": 0, "category": "None" }, "inSeason": false },
            { "code": "WEED", "displayName": "Weed",
              "indexInfo": { "code": "UPI", "value": 4, "category": "High" }, "inSeason": true }
          ],
          "plantInfo": [
            { "code": "RAGWEED", "displayName": "Ragweed",
              "indexInfo": { "code": "UPI", "value": 4, "category": "High" }, "inSeason": true }
          ]
        }
      ]
    }
    """;

    private const string AirJson = """
    {
      "dateTime": "2026-09-13T10:00:00Z",
      "regionCode": "us",
      "indexes": [
        { "code": "uaqi", "displayName": "Universal AQI", "aqi": 50, "aqiDisplay": "50",
          "category": "Moderate air quality", "dominantPollutant": "o3" }
      ]
    }
    """;

    private static GoogleWeatherClient Client(Func<HttpRequestMessage, string> respond) =>
        new(new HttpClient(new StubHandler(respond)),
            Options.Create(new MapsOptions
            {
                ApiKey = "key-abc",
                HomeLatitude = 40.8259,
                HomeLongitude = -74.2090,
            }));

    private static string Route(HttpRequestMessage request)
    {
        var url = request.RequestUri!.ToString();
        if (url.Contains("forecast/days:lookup", StringComparison.Ordinal)) return DailyJson;
        if (url.Contains("forecast/hours:lookup", StringComparison.Ordinal)) return HourlyJson;
        if (url.Contains("pollen.googleapis.com", StringComparison.Ordinal)) return PollenJson;
        if (url.Contains("airquality.googleapis.com", StringComparison.Ordinal)) return AirJson;
        throw new InvalidOperationException($"Nothing stubbed for {url}");
    }

    [Fact]
    public async Task TodayCarriesTheTemperaturesAndTheSky()
    {
        var report = await Client(Route).GetReport(Day);

        Assert.NotNull(report.Today);
        Assert.Equal(78, report.Today!.High);
        Assert.Equal(57, report.Today.Low);
        Assert.Equal("Clearing", report.Today.Condition);
    }

    /// <summary>
    /// Degrees come back as a decimal and the page has no room for one. 56.6 rounds to 57 rather
    /// than truncating to 56 — a low reported a degree colder than forecast every single morning is
    /// the kind of wrongness nobody would ever trace back to a cast.
    /// </summary>
    [Fact]
    public async Task DegreesAreRoundedRatherThanTruncated()
    {
        var report = await Client(Route).GetReport(Day);

        Assert.Equal(57, report.Today!.Low);
        Assert.Equal(78, report.Today.High);
    }

    [Fact]
    public async Task TheHoursComeBackInOrderWithTheirChanceOfRain()
    {
        var report = await Client(Route).GetReport(Day);

        var hours = report.Today!.Hours;
        Assert.Equal(4, hours.Count);
        Assert.Equal(58, hours[0].Degrees);
        Assert.Equal(80, hours[0].PrecipitationPercent);
        Assert.Equal(0, hours[3].PrecipitationPercent);
        Assert.True(hours[0].Hour < hours[3].Hour);
    }

    /// <summary>
    /// The peak is read off the hours rather than taken from the daily high, so the "at 3pm" in the
    /// headline is a time that exists in the chart under it.
    /// </summary>
    [Fact]
    public async Task TheHighIsMarkedAtTheHourItLands()
    {
        var report = await Client(Route).GetReport(Day);

        Assert.Equal(report.Today!.Hours.MaxBy(h => h.Degrees).Hour, report.Today.HighAt);
    }

    [Fact]
    public async Task TheWeekCarriesEachDaysWordAndNumbers()
    {
        var report = await Client(Route).GetReport(Day);

        Assert.Equal(2, report.Week.Count);
        Assert.Equal(new DateOnly(2026, 9, 13), report.Week[0].Date);
        Assert.Equal("Clearing", report.Week[0].Condition);
        Assert.Equal(30, report.Week[0].PrecipitationPercent);
        Assert.Equal(81, report.Week[1].High);
    }

    [Fact]
    public async Task SunriseSunsetAndTheMoonAreCarried()
    {
        var report = await Client(Route).GetReport(Day);

        Assert.NotNull(report.Today!.Sun.Sunrise);
        Assert.NotNull(report.Today.Sun.Sunset);
        Assert.Equal("WANING_GIBBOUS", report.Today.Sun.MoonPhase);
    }

    /// <summary>
    /// Ragweed leads, and it is the one that comes from a different array: grass and tree are pollen
    /// TYPES, ragweed is a PLANT. A reader that only walked pollenTypeInfo would silently report
    /// "Weed" where the page says Ragweed, in the middle of ragweed season, which is the reason the
    /// row exists at all.
    /// </summary>
    [Fact]
    public async Task TheAllergyStripIsRagweedGrassTreeAndAir()
    {
        var report = await Client(Route).GetReport(Day);

        Assert.Equal(["Ragweed", "Grass", "Tree", "Air quality"],
            report.Allergy.Select(a => a.Name));

        Assert.Equal("High", report.Allergy[0].Category);
        Assert.Equal(4, report.Allergy[0].Value);
    }

    /// <summary>
    /// Air quality is an AQI, not a 0-5 index, and it shares the meter with the pollen readings — so
    /// it is placed on the same five steps rather than drawn at 50 out of 5, which would fill every
    /// step of the bar on an ordinary day.
    /// </summary>
    [Fact]
    public async Task AirQualityIsPlacedOnTheSameFiveSteps()
    {
        var report = await Client(Route).GetReport(Day);

        var air = report.Allergy.Single(a => a.Name == "Air quality");
        Assert.Equal("50 · Moderate air quality", air.Category);
        Assert.InRange(air.Value, 0, AllergyReading.Steps);
    }

    /// <summary>
    /// Four calls, four ways to fail. A pollen call that falls over must not take the temperatures
    /// down with it — the document goes out at half past six whatever the weather service is doing.
    /// </summary>
    [Fact]
    public async Task OneFailedCallDoesNotTakeTheOthersWithIt()
    {
        var report = await Client(request =>
            request.RequestUri!.ToString().Contains("pollen", StringComparison.Ordinal)
                ? throw new HttpRequestException("pollen is down")
                : Route(request)).GetReport(Day);

        Assert.Equal(78, report.Today!.High);
        Assert.Equal(2, report.Week.Count);
        Assert.Single(report.Allergy);   // air quality survived on its own
    }

    [Fact]
    public async Task WithNoKeyNothingIsCalledAndNothingComesBack()
    {
        var client = new GoogleWeatherClient(
            new HttpClient(new StubHandler(_ => throw new InvalidOperationException("must not call"))),
            Options.Create(new MapsOptions()));

        Assert.True((await client.GetReport(Day)).IsEmpty);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request)),
            });
    }
}
