using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Foreword.Domain.Health;
using Foreword.Domain.Interfaces;
using Foreword.Domain.Weather;
using Storyworthtelling.Voice.Health;

namespace Foreword.Infrastructure.Maps;

/// <summary>
/// How long each of the day's drives takes, from the Routes API.
///
/// <para>The ends are sent as free text, because that is what a calendar's location field holds —
/// "Veterans Park", not a pair of coordinates — and the API takes an address waypoint for exactly
/// that. The departure time is the moment the leg is actually made rather than "now": the run is at
/// half past six and the drive is at four in the afternoon, and traffic is the only reason to ask a
/// router instead of a map.</para>
///
/// <para>The <c>X-Goog-FieldMask</c> header is not a nicety. Without it the API returns routes with
/// no fields on them — so every drive would come back unmeasured, no error anywhere, and the page
/// would look exactly like a day with no driving in it.</para>
/// </summary>
public class GoogleRoutesClient(
    HttpClient httpClient,
    IOptions<MapsOptions> options,
    ICredentialHealthSink? healthSink = null) : IRouteClient
{
    private const string Endpoint = "https://routes.googleapis.com/directions/v2:computeRoutes";
    private const string FieldMask = "routes.duration,routes.distanceMeters";

    private readonly MapsOptions options = options.Value;

    public async Task<TravelLegs> Measure(
        IReadOnlyList<(string From, string To)> legs, DateTime departAfter)
    {
        if (!options.IsConfigured)
        {
            return TravelLegs.None;
        }

        var measured = new List<(string From, string To, int Minutes)>();

        // Deduplicated: a family's day repeats legs — both parents leaving the same house for the
        // same field — and Maps Platform bills per request.
        foreach (var leg in legs.Distinct())
        {
            if (await Minutes(leg.From, leg.To, departAfter) is { } minutes)
            {
                measured.Add((leg.From, leg.To, minutes));
            }
        }

        return new TravelLegs(measured);
    }

    private async Task<int?> Minutes(string from, string to, DateTime departAfter)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(Body(from, to, departAfter), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", options.ApiKey);
        request.Headers.TryAddWithoutValidation("X-Goog-FieldMask", FieldMask);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                // Only a rejection flags the key; a throttle or a 500 is what a working key does on
                // a bad morning, and one of those should not put a "paste your key" box on screen.
                if (AuthFailure.IsHttpCredentialRejection((int)response.StatusCode, null))
                {
                    healthSink?.MarkFailing(
                        CredentialIds.Maps, $"{(int)response.StatusCode} {response.StatusCode}");
                }

                return null;
            }

            healthSink?.MarkOk(CredentialIds.Maps);
            return Read(await response.Content.ReadAsStringAsync());
        }
        catch (HttpRequestException)
        {
            // One unroutable leg is not worth losing the rest of the day's bands over.
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }

    private static string Body(string from, string to, DateTime departAfter) =>
        JsonSerializer.Serialize(new
        {
            origin = new { address = from },
            destination = new { address = to },
            travelMode = "DRIVE",
            routingPreference = "TRAFFIC_AWARE",
            departureTime = departAfter.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
            units = "IMPERIAL",
        });

    /// <summary>
    /// Duration comes back as a protobuf duration string — "1500s" — not a number. Rounded to the
    /// nearest minute rather than truncated: half a minute short, every time, on every band.
    /// </summary>
    private static int? Read(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("routes", out var routes)
                || routes.EnumerateArray().FirstOrDefault() is not { ValueKind: JsonValueKind.Object } route
                || !route.TryGetProperty("duration", out var duration)
                || duration.GetString() is not { } text)
            {
                return null;
            }

            var seconds = text.TrimEnd('s', 'S');
            return double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? (int)Math.Round(value / 60.0, MidpointRounding.AwayFromZero)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
