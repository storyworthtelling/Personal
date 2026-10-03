using System.Net;
using System.Text;
using Foreword.Infrastructure.Google;
using Foreword.Infrastructure.Maps;
using Microsoft.Extensions.Options;
using Xunit;

namespace Foreword.Tests.Maps;

/// <summary>
/// Where home is, found rather than asked.
///
/// <para>Settings used to ask for the house twice — once as an address for the drives, once as a
/// latitude and longitude for the forecast — and both are facts Google already holds: the account's
/// home address (the one Google Maps labels Home) comes from the People API, and the point comes from
/// geocoding that address with the Maps key the page already has. The fields appear only when a
/// lookup comes back empty.</para>
///
/// <para>Response shapes are the published ones — people/get with <c>personFields=addresses</c>, and
/// the Geocoding API's <c>results[].geometry.location</c> — because a reader that guesses a field name
/// returns nothing and no error.</para>
/// </summary>
public class HomeLookupTests
{
    private const string PeopleJson = """
    {
      "resourceName": "people/me",
      "addresses": [
        {
          "metadata": { "primary": false },
          "type": "work",
          "formattedType": "Work",
          "formattedValue": "1 Office Park\nSpringfield, IL 62702\nUS"
        },
        {
          "metadata": { "primary": true },
          "type": "home",
          "formattedType": "Home",
          "formattedValue": "100 Main Street\nSpringfield, IL 62701\nUS"
        }
      ]
    }
    """;

    private const string GeocodeJson = """
    {
      "results": [
        {
          "formatted_address": "100 Main St, Springfield, IL 62701, USA",
          "geometry": { "location": { "lat": 39.7817, "lng": -89.6501 } }
        }
      ],
      "status": "OK"
    }
    """;

    // ---- the account's home address (People API) ----

    [Fact]
    public async Task TheAccountsHomeAddressIsReadAsOneLine()
    {
        var captured = new List<HttpRequestMessage>();
        var client = new GooglePeopleClient(Http(captured, PeopleJson));

        var home = await client.HomeAddress("token-123");

        Assert.Equal("100 Main Street, Springfield, IL 62701, US", home);
        var request = Assert.Single(captured);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("token-123", request.Headers.Authorization?.Parameter);
        Assert.Contains("personFields=addresses", request.RequestUri!.Query, StringComparison.Ordinal);
    }

    /// <summary>
    /// Only a <i>home</i> address is home. A work address is where one person's day goes, not where
    /// the household's starts, and treating it as home would measure every drive from the office.
    /// </summary>
    [Fact]
    public async Task AWorkAddressIsNotHome()
    {
        const string workOnly = """
        { "addresses": [ { "type": "work", "formattedValue": "1 Office Park\nSpringfield, IL" } ] }
        """;

        Assert.Null(await new GooglePeopleClient(Http([], workOnly)).HomeAddress("t"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "addresses": [] }""")]
    public async Task AnAccountWithNoAddressesHasNoHome(string json)
    {
        Assert.Null(await new GooglePeopleClient(Http([], json)).HomeAddress("t"));
    }

    /// <summary>A refused scope is "not found", never an exception out of a settings screen.</summary>
    [Fact]
    public async Task ARefusalIsNotFound()
    {
        var client = new GooglePeopleClient(Http([], "{}", HttpStatusCode.Forbidden));

        Assert.Null(await client.HomeAddress("t"));
    }

    // ---- the point (Geocoding API) ----

    [Fact]
    public async Task TheAddressIsGeocodedToAPoint()
    {
        var captured = new List<HttpRequestMessage>();
        var client = new GoogleGeocodingClient(Http(captured, GeocodeJson), Maps("key-abc"));

        var point = await client.Locate("100 Main Street, Springfield, IL");

        Assert.NotNull(point);
        Assert.Equal(39.7817, point!.Value.Latitude, 4);
        Assert.Equal(-89.6501, point.Value.Longitude, 4);

        var query = Assert.Single(captured).RequestUri!.Query;
        Assert.Contains("key=key-abc", query, StringComparison.Ordinal);
        Assert.Contains("address=100%20Main%20Street", query, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Geocoding API reports most failures as HTTP 200 with a status — an address it cannot find
    /// is <c>ZERO_RESULTS</c>, a key without the API enabled is <c>REQUEST_DENIED</c>. Both are "no
    /// point", so the page asks for one.
    /// </summary>
    [Theory]
    [InlineData("""{ "results": [], "status": "ZERO_RESULTS" }""")]
    [InlineData("""{ "results": [], "status": "REQUEST_DENIED", "error_message": "This API is not activated." }""")]
    public async Task AnAddressThatCannotBePlacedHasNoPoint(string json)
    {
        var client = new GoogleGeocodingClient(Http([], json), Maps("key"));

        Assert.Null(await client.Locate("Nowhere in particular"));
    }

    [Fact]
    public async Task WithoutAKeyNothingIsAsked()
    {
        var captured = new List<HttpRequestMessage>();
        var client = new GoogleGeocodingClient(Http(captured, GeocodeJson), Maps(string.Empty));

        Assert.Null(await client.Locate("100 Main Street"));
        Assert.Empty(captured);
    }

    // ---- putting it together ----

    [Fact]
    public async Task WithNothingSetBothAreFound()
    {
        var locator = Locator(GeocodeJson);

        var home = await locator.Fill(new HomeLocation(string.Empty, 0, 0), () => Task.FromResult<string?>("100 Main Street, Springfield"));

        Assert.Equal("100 Main Street, Springfield", home.Address);
        Assert.True(home.HasPoint);
        Assert.Equal(39.7817, home.Latitude, 4);
    }

    /// <summary>An address already entered is the owner's, and is never replaced by the account's.</summary>
    [Fact]
    public async Task AnEnteredAddressIsKeptAndOnlyThePointIsFound()
    {
        var askedTheAccount = false;
        var locator = Locator(GeocodeJson);

        var home = await locator.Fill(new HomeLocation("The farmhouse, Route 9", 0, 0), () =>
        {
            askedTheAccount = true;
            return Task.FromResult<string?>("100 Main Street");
        });

        Assert.False(askedTheAccount);
        Assert.Equal("The farmhouse, Route 9", home.Address);
        Assert.True(home.HasPoint);
    }

    [Fact]
    public async Task ASetPointIsNeverReGeocoded()
    {
        var captured = new List<HttpRequestMessage>();
        var locator = new HomeLocator(new GoogleGeocodingClient(Http(captured, GeocodeJson), Maps("key")));

        var home = await locator.Fill(new HomeLocation("100 Main Street", 40.0, -89.0), () => Task.FromResult<string?>(null));

        Assert.Empty(captured);
        Assert.Equal(40.0, home.Latitude);
    }

    [Fact]
    public async Task WhenTheAccountHasNoHomeTheAddressIsStillMissing()
    {
        var home = await Locator(GeocodeJson).Fill(HomeLocation.None, () => Task.FromResult<string?>(null));

        Assert.False(home.HasAddress);
        Assert.False(home.HasPoint);
    }

    /// <summary>
    /// A lookup that throws — a consent screen dismissed, a network gone — costs the lookup, not the
    /// page: whatever was already known is returned as it was.
    /// </summary>
    [Fact]
    public async Task AnAccountLookupThatFailsLeavesThingsAsTheyWere()
    {
        var home = await Locator(GeocodeJson).Fill(HomeLocation.None, () => throw new InvalidOperationException("dismissed"));

        Assert.Equal(HomeLocation.None, home);
    }

    [Fact]
    public async Task WhenTheAddressCannotBePlacedThePointIsStillMissing()
    {
        var home = await Locator("""{ "results": [], "status": "ZERO_RESULTS" }""")
            .Fill(new HomeLocation("100 Main Street", 0, 0), () => Task.FromResult<string?>(null));

        Assert.True(home.HasAddress);
        Assert.False(home.HasPoint);
    }

    private static HomeLocator Locator(string geocodeJson) =>
        new(new GoogleGeocodingClient(Http([], geocodeJson), Maps("key")));

    private static IOptions<MapsOptions> Maps(string key) => Options.Create(new MapsOptions { ApiKey = key });

    private static HttpClient Http(
        List<HttpRequestMessage> captured, string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new StubHandler(captured, json, status));

    private sealed class StubHandler(List<HttpRequestMessage> captured, string json, HttpStatusCode status)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            captured.Add(request);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
