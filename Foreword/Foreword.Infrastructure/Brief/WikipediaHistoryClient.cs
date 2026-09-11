using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Foreword.Domain.Brief;
using Foreword.Domain.Interfaces;

namespace Foreword.Infrastructure.Brief;

/// <summary>
/// Wikipedia's on-this-day feed, which is keyless (F15). The path takes the month and day
/// zero-padded; a live response carries <c>events</c>, each with <c>year</c> and <c>text</c>.
///
/// <para>It asks for <c>events</c> rather than <c>all</c> or <c>selected</c>: <c>all</c> also
/// returns births, deaths and holidays, and a factoid that is somebody's birthday is a different
/// kind of line from a thing that happened.</para>
/// </summary>
public class WikipediaHistoryClient(
    HttpClient httpClient, ILogger<WikipediaHistoryClient> logger) : IHistoryClient
{
    private const string Feed = "https://en.wikipedia.org/api/rest_v1/feed/onthisday/events";

    public async Task<IReadOnlyList<HistoryNote>> OnThisDay(DateOnly date)
    {
        var url = $"{Feed}/{date.Month:D2}/{date.Day:D2}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // Wikimedia asks every client to identify itself, and an unidentified one can be
            // throttled or refused outright — which would read here as a day with no history in it.
            request.Headers.TryAddWithoutValidation("User-Agent", "Foreword/2.0 (personal use)");

            var response = await httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogInformation("On-this-day answered {Status} for {Date}", response.StatusCode, date);
                return [];
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

            if (!document.RootElement.TryGetProperty("events", out var events))
            {
                return [];
            }

            return
            [
                .. events.EnumerateArray()
                    .Select(Note)
                    .Where(note => note is not null)
                    .Select(note => note!),
            ];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // A factoid is the most skippable thing in the document and must never cost the morning.
            logger.LogInformation(ex, "On-this-day could not be read for {Date}", date);
            return [];
        }
    }

    private static HistoryNote? Note(JsonElement entry)
    {
        if (!entry.TryGetProperty("text", out var text) || text.GetString() is not { } sentence)
        {
            return null;
        }

        // The year arrives as a NUMBER in some entries and a STRING in others — the feed is built
        // from wiki markup, so both shapes are real and a reader that assumed one would silently
        // drop half the day.
        if (!entry.TryGetProperty("year", out var year))
        {
            return null;
        }

        var value = year.ValueKind switch
        {
            JsonValueKind.Number when year.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(
                year.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0,
        };

        return value == 0 ? null : new HistoryNote(value, sentence.Trim());
    }
}
