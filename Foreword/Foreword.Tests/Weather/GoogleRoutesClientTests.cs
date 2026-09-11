using System.Net;
using System.Text.Json;
using Foreword.Infrastructure.Maps;
using Microsoft.Extensions.Options;
using Xunit;

namespace Foreword.Tests.Weather;

/// <summary>
/// The Routes API call behind the calendar's travel bands, against the shape the published API
/// actually returns: POST to <c>directions/v2:computeRoutes</c>, key in <c>X-Goog-Api-Key</c>, and —
/// the part that is easy to miss — a <c>X-Goog-FieldMask</c> header, without which the response
/// comes back with no fields in it at all.
/// </summary>
public class GoogleRoutesClientTests
{
    private static readonly DateTime Morning = new(2026, 9, 13, 7, 0, 0, DateTimeKind.Local);

    private static GoogleRoutesClient Client(StubHandler handler) =>
        new(new HttpClient(handler), Options.Create(new MapsOptions { ApiKey = "key-abc" }));

    [Fact]
    public async Task ADriveComesBackInWholeMinutes()
    {
        // 1500 seconds is twenty-five minutes.
        var handler = new StubHandler(_ => """{"routes":[{"duration":"1500s","distanceMeters":19000}]}""");

        var measured = await Client(handler).Measure([("Home", "Veterans Park")], Morning);

        Assert.Equal(25, measured.Legs.Single().Minutes);
        Assert.Equal("Home", measured.Legs.Single().From);
        Assert.Equal("Veterans Park", measured.Legs.Single().To);
    }

    /// <summary>
    /// Seconds round to the nearest minute rather than down. A twenty-nine-and-a-half-minute drive
    /// reported as twenty-nine is a band drawn half a minute short of the truth, every time.
    /// </summary>
    [Fact]
    public async Task SecondsRoundToTheNearestMinute()
    {
        var handler = new StubHandler(_ => """{"routes":[{"duration":"1770s"}]}""");

        Assert.Equal(30, (await Client(handler).Measure([("a", "b")], Morning)).Legs.Single().Minutes);
    }

    /// <summary>
    /// The field mask is not optional and not a nicety: without it the Routes API returns routes
    /// with no fields on them, so every drive would come back unmeasured and the page would look
    /// exactly like a day with no driving in it.
    /// </summary>
    [Fact]
    public async Task TheFieldMaskAndKeyAreSentAsHeaders()
    {
        var handler = new StubHandler(_ => """{"routes":[{"duration":"600s"}]}""");

        await Client(handler).Measure([("a", "b")], Morning);

        var sent = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Contains("directions/v2:computeRoutes", sent.Url, StringComparison.Ordinal);
        Assert.Equal("key-abc", sent.Headers["X-Goog-Api-Key"]);
        Assert.Contains("routes.duration", sent.Headers["X-Goog-FieldMask"], StringComparison.Ordinal);
    }

    /// <summary>
    /// The ends are sent as free text, because that is what a calendar's location field contains —
    /// "Veterans Park", not a pair of coordinates. The API takes an address waypoint for exactly
    /// this.
    /// </summary>
    [Fact]
    public async Task ThePlacesAreSentAsAddresses()
    {
        var handler = new StubHandler(_ => """{"routes":[{"duration":"600s"}]}""");

        await Client(handler).Measure([("Home", "The school")], Morning);

        using var body = JsonDocument.Parse(handler.Requests.Single().Body);
        Assert.Equal("Home", body.RootElement.GetProperty("origin").GetProperty("address").GetString());
        Assert.Equal("The school", body.RootElement.GetProperty("destination").GetProperty("address").GetString());
    }

    /// <summary>
    /// Traffic is the whole point of measuring a drive at all, and traffic needs a time. The
    /// departure is sent as the RFC-3339 instant the leg is actually made, not "now" — the run is
    /// at half past six and the drive is at four in the afternoon.
    /// </summary>
    [Fact]
    public async Task TheDepartureTimeIsSentSoTrafficIsAccountedFor()
    {
        var handler = new StubHandler(_ => """{"routes":[{"duration":"600s"}]}""");

        await Client(handler).Measure([("a", "b")], Morning);

        using var body = JsonDocument.Parse(handler.Requests.Single().Body);
        Assert.Equal("TRAFFIC_AWARE", body.RootElement.GetProperty("routingPreference").GetString());
        Assert.Equal(Morning.ToUniversalTime(),
            body.RootElement.GetProperty("departureTime").GetDateTime().ToUniversalTime());
    }

    /// <summary>
    /// A leg that cannot be routed is left out, not reported as zero. TravelPlan draws nothing for
    /// an unknown drive, and a zero would say "you are already there".
    /// </summary>
    [Fact]
    public async Task ALegWithNoRouteIsLeftOut()
    {
        var handler = new StubHandler(request =>
            request.Contains("Nowhere", StringComparison.Ordinal)
                ? """{"routes":[]}"""
                : """{"routes":[{"duration":"600s"}]}""");

        var measured = await Client(handler).Measure([("Home", "Nowhere"), ("Home", "The school")], Morning);

        Assert.Equal("The school", measured.Legs.Single().To);
    }

    [Fact]
    public async Task AFailedLegDoesNotTakeTheOthersWithIt()
    {
        var handler = new StubHandler(request =>
            request.Contains("Nowhere", StringComparison.Ordinal)
                ? throw new HttpRequestException("routes is down")
                : """{"routes":[{"duration":"600s"}]}""");

        var measured = await Client(handler).Measure([("Home", "Nowhere"), ("Home", "The school")], Morning);

        Assert.Equal("The school", measured.Legs.Single().To);
    }

    /// <summary>
    /// The same pair asked for twice is one call. A family's day repeats legs — both parents leaving
    /// the same house for the same field — and Maps Platform bills per request.
    /// </summary>
    [Fact]
    public async Task ARepeatedLegIsOnlyMeasuredOnce()
    {
        var handler = new StubHandler(_ => """{"routes":[{"duration":"600s"}]}""");

        var measured = await Client(handler)
            .Measure([("Home", "The school"), ("Home", "The school")], Morning);

        Assert.Single(handler.Requests);
        Assert.Single(measured.Legs);
    }

    [Fact]
    public async Task WithNoKeyNothingIsCalled()
    {
        var client = new GoogleRoutesClient(
            new HttpClient(new StubHandler(_ => throw new InvalidOperationException("must not call"))),
            Options.Create(new MapsOptions()));

        Assert.Empty((await client.Measure([("a", "b")], Morning)).Legs);
    }

    private sealed class StubHandler(Func<string, string> respond) : HttpMessageHandler
    {
        public List<(string Url, HttpMethod Method, string Body, Dictionary<string, string> Headers)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((
                request.RequestUri!.ToString(),
                request.Method,
                body,
                request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value))));

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(respond(body)) };
        }
    }
}
