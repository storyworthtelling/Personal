using System.Net;
using Storyworthtelling.Google;
using Storyworthtelling.Google.Tests.Support;

namespace Storyworthtelling.Google.Tests;

public class GoogleWeatherClientTests
{
    private const string SampleJson = """
    {
      "temperature": { "degrees": 21.5, "unit": "CELSIUS" },
      "weatherCondition": { "description": { "text": "Partly cloudy" } },
      "relativeHumidity": 62
    }
    """;

    private static GoogleWeatherClient Client(FakeHttpMessageHandler handler, string? key = "wx-key") =>
        new(new HttpClient(handler), _ => Task.FromResult(key));

    [Fact]
    public async Task ParsesTemperatureConditionAndHumidity()
    {
        var handler = FakeHttpMessageHandler.Ok(SampleJson);
        var reading = await Client(handler).GetCurrentAsync(40.0, -74.0);

        Assert.NotNull(reading);
        Assert.Equal(21.5, reading!.TemperatureCelsius);
        Assert.Equal("Partly cloudy", reading.Condition);
        Assert.Equal(62, reading.RelativeHumidity);
        Assert.Contains("weather.googleapis.com", handler.Requests[0].ToString());
        Assert.Contains("unitsSystem=METRIC", handler.Requests[0].ToString());
    }

    [Fact]
    public async Task ReturnsNullWhenNoApiKey()
    {
        var handler = FakeHttpMessageHandler.Ok(SampleJson);
        var reading = await Client(handler, key: null).GetCurrentAsync(40.0, -74.0);

        Assert.Null(reading);
        Assert.Empty(handler.Requests); // no key: never even calls the API
    }

    [Fact]
    public async Task DegradesToNullOnAnErrorStatus()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.Forbidden, "nope");
        Assert.Null(await Client(handler).GetCurrentAsync(40.0, -74.0));
    }

    [Fact]
    public async Task DegradesToNullOnMalformedJson()
    {
        var handler = FakeHttpMessageHandler.Ok("{ not valid json");
        Assert.Null(await Client(handler).GetCurrentAsync(40.0, -74.0));
    }

    [Fact]
    public async Task DegradesToNullWhenTemperatureMissing()
    {
        var handler = FakeHttpMessageHandler.Ok("""{ "weatherCondition": { "description": { "text": "x" } } }""");
        Assert.Null(await Client(handler).GetCurrentAsync(40.0, -74.0));
    }
}
