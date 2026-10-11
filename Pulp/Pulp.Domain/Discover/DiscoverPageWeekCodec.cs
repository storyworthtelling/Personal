using System.Globalization;
using System.Text;
using System.Text.Json;
using Pulp.Domain.Core;

namespace Pulp.Domain.Discover;

/// <summary>A week read back: the week, or null with the reason it could not be read.</summary>
public sealed record DiscoverWeekRead(DiscoverPageWeek? Week, string? Problem);

/// <summary>
/// The kept week as one small JSON document on this phone. Unshared and re-derivable — the next deal
/// rebuilds it from the catalogue — so a garbled or older file is reported and replaced, never
/// repaired. Card rows are kept whole, so the page paints the stack before any network answers.
/// </summary>
public static class DiscoverPageWeekCodec
{
    public const int Version = 1;

    public static string Write(DiscoverPageWeek week)
    {
        ArgumentNullException.ThrowIfNull(week);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteNumber("version", Version);
            json.WriteString("viewer", week.Viewer.Value);
            json.WriteString("turnover", week.Turnover.ToString("O", CultureInfo.InvariantCulture));
            json.WriteString("until", week.Until.ToString("O", CultureInfo.InvariantCulture));
            json.WriteStartArray("added");
            foreach (var title in week.Added.OrderBy(title => title.Value, StringComparer.OrdinalIgnoreCase))
            {
                json.WriteStringValue(title.Value);
            }

            json.WriteEndArray();
            WriteRows(json, "films", week.Films);
            WriteRows(json, "shows", week.Shows);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static DiscoverWeekRead Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new DiscoverWeekRead(null, "The kept week is empty.");
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var version = root.GetProperty("version").GetInt32();
            if (version != Version)
            {
                return new DiscoverWeekRead(null, $"The kept week is version {version}; this build reads version {Version}.");
            }

            var week = new DiscoverPageWeek(
                new ViewerId(root.GetProperty("viewer").GetString() ?? string.Empty),
                DateTimeOffset.Parse(root.GetProperty("turnover").GetString() ?? string.Empty, CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(root.GetProperty("until").GetString() ?? string.Empty, CultureInfo.InvariantCulture),
                ReadRows(root.GetProperty("films")),
                ReadRows(root.GetProperty("shows")),
                root.GetProperty("added").EnumerateArray().Select(item => new TitleId(item.GetString() ?? string.Empty)).ToHashSet());
            return new DiscoverWeekRead(week, null);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        {
            return new DiscoverWeekRead(null, $"The kept week could not be read ({exception.GetType().Name}: {exception.Message}).");
        }
    }

    private static void WriteRows(Utf8JsonWriter json, string name, IEnumerable<DiscoverRow> rows)
    {
        json.WriteStartArray(name);
        foreach (var row in rows)
        {
            json.WriteStartObject();
            json.WriteString("id", row.Id.Value);
            json.WriteString("kind", row.Kind == TitleKind.Film ? "film" : "show");
            json.WriteString("name", row.Name);
            json.WriteString("kindLine", row.KindLine);
            json.WriteString("homeLine", row.HomeLine);
            json.WriteBoolean("yours", row.OnYourServices);
            if (row.Year is { } year)
            {
                json.WriteNumber("year", year);
            }

            if (row.PosterUrl is not null)
            {
                json.WriteString("poster", row.PosterUrl);
            }

            json.WriteStartArray("tags");
            foreach (var tag in row.Tags)
            {
                json.WriteStringValue(tag);
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        json.WriteEndArray();
    }

    private static List<DiscoverRow> ReadRows(JsonElement rows) =>
        rows.EnumerateArray().Select(row => new DiscoverRow(
            new TitleId(row.GetProperty("id").GetString() ?? string.Empty),
            row.GetProperty("kind").GetString() switch
            {
                "film" => TitleKind.Film,
                "show" => TitleKind.Show,
                var other => throw new FormatException($"'{other}' is not a title kind."),
            },
            row.GetProperty("name").GetString() ?? string.Empty,
            row.GetProperty("kindLine").GetString() ?? string.Empty,
            row.GetProperty("homeLine").GetString() ?? string.Empty,
            row.GetProperty("yours").GetBoolean(),
            row.TryGetProperty("poster", out var poster) ? poster.GetString() : null,
            row.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString() ?? string.Empty).ToList())
        {
            Year = row.TryGetProperty("year", out var year) ? year.GetInt32() : null,
        })
        .ToList();
}
