using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pulp.Domain.Availability;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.Ratings;

namespace Pulp.Infrastructure.Catalogue;

/// <summary>A cached title and when it was fetched.</summary>
public sealed record CachedTitle(CatalogueTitle Title, CacheStamp Stamp);

public enum CacheReadStatus
{
    Hit,
    Missing,

    /// <summary>The entry is damaged or from a newer cache format; the caller logs <see cref="CacheRead.Problem"/> and fetches again.</summary>
    Unreadable,
}

public sealed record CacheRead(CacheReadStatus Status, CachedTitle? Entry, string? Problem);

/// <summary>
/// The on-device copy of fetched titles (D204), so a screen paints at once from what was last
/// fetched and <see cref="CatalogueFreshness"/> decides whether a lookup follows. One file per
/// title in the app's cache directory — named by a hash of the id, compared without case — each
/// written atomically. A cache is re-derivable, so a damaged entry is not an error to stop on, but it
/// is not hidden either: it comes back as <see cref="CacheReadStatus.Unreadable"/> with the reason,
/// for the caller to log before fetching again. Hand-written JSON rather than reflection, so the
/// trimmed Android build cannot strip a property.
/// </summary>
public sealed class CatalogueCache
{
    public const int FormatVersion = 1;

    private static readonly string[] OfferKinds = ["subscription", "free", "addon", "rent", "buy"];
    private static readonly string[] Qualities = ["sd", "hd", "uhd"];

    private readonly string directory;
    private readonly object gate = new();

    public CatalogueCache(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = directory;
    }

    public CacheRead Read(TitleId id)
    {
        var path = PathOf(id);
        lock (gate)
        {
            if (!File.Exists(path))
            {
                return new CacheRead(CacheReadStatus.Missing, null, null);
            }

            try
            {
                return new CacheRead(CacheReadStatus.Hit, Decode(File.ReadAllText(path)), null);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException or CacheFormatException)
            {
                return new CacheRead(CacheReadStatus.Unreadable, null, $"Cached title {id} could not be read: {exception.Message}");
            }
        }
    }

    public void Store(CatalogueTitle title, DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(title);
        Write(new CachedTitle(title, new CacheStamp(fetchedAt, Invalidated: false)));
    }

    /// <summary>Marks a cached title stale (the changes feed touched it); keeps the copy to paint. Nothing cached, nothing to do.</summary>
    public void Invalidate(TitleId id)
    {
        lock (gate)
        {
            var read = Read(id);
            if (read.Entry is { } entry)
            {
                Write(entry with { Stamp = entry.Stamp with { Invalidated = true } });
            }
        }
    }

    private void Write(CachedTitle entry)
    {
        var text = Encode(entry);
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            var path = PathOf(entry.Title.Id);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, text, Encoding.UTF8);
            File.Move(temporary, path, overwrite: true);
        }
    }

    private string PathOf(TitleId id)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(id.Value.ToLowerInvariant()));
        return Path.Combine(directory, Convert.ToHexStringLower(hash) + ".json");
    }

    private sealed class CacheFormatException(string message) : Exception(message);

    // ---- encoding ----

    private static string Encode(CachedTitle entry)
    {
        var title = entry.Title;
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", FormatVersion);
            writer.WriteString("fetchedAt", entry.Stamp.FetchedAt.ToString("O", CultureInfo.InvariantCulture));
            writer.WriteBoolean("invalidated", entry.Stamp.Invalidated);
            writer.WriteStartObject("title");
            writer.WriteString("id", title.Id.Value);
            writer.WriteString("kind", title.Kind == TitleKind.Film ? "film" : "show");
            writer.WriteString("name", title.Name);
            OptionalString(writer, "imdbId", title.ImdbId);
            OptionalNumber(writer, "year", title.Year);
            writer.WriteStartArray("genres");
            foreach (var genre in title.Genres)
            {
                writer.WriteStringValue(genre);
            }

            writer.WriteEndArray();
            writer.WriteString("synopsis", title.Synopsis);
            OptionalString(writer, "posterUrl", title.PosterUrl);
            OptionalNumber(writer, "runtimeMinutes", title.RuntimeMinutes);
            OptionalNumber(writer, "seasonCount", title.SeasonCount);
            OptionalNumber(writer, "episodeCount", title.EpisodeCount);
            writer.WriteString("rating", title.Rating.Code);
            WriteOffers(writer, title.Offers);
            if (title.Ended is { } ended)
            {
                writer.WriteBoolean("ended", ended);
            }

            writer.WriteStartArray("seasons");
            foreach (var season in title.Seasons)
            {
                writer.WriteStartObject();
                writer.WriteNumber("number", season.Number);
                writer.WriteString("name", season.Name);
                writer.WriteStartArray("episodes");
                foreach (var episode in season.Episodes)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("season", episode.Episode.Season);
                    writer.WriteNumber("number", episode.Episode.Number);
                    writer.WriteString("name", episode.Name);
                    writer.WriteString("synopsis", episode.Synopsis);
                    OptionalNumber(writer, "airYear", episode.AirYear);
                    OptionalString(writer, "airDate", episode.AirDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    OptionalString(writer, "airsAt", episode.AirsAt?.ToString("O", CultureInfo.InvariantCulture));
                    WriteOffers(writer, episode.Offers);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteOffers(Utf8JsonWriter writer, IReadOnlyList<TitleOffer> offers)
    {
        writer.WriteStartArray("offers");
        foreach (var offer in offers)
        {
            writer.WriteStartObject();
            writer.WriteString("service", offer.Service.Value);
            writer.WriteString("serviceName", offer.ServiceName);
            writer.WriteBoolean("known", offer.KnownService);
            writer.WriteString("kind", OfferKinds[(int)offer.Kind]);
            writer.WriteString("quality", Qualities[(int)offer.Quality]);
            OptionalString(writer, "price", offer.Price?.ToString(CultureInfo.InvariantCulture));
            OptionalString(writer, "currency", offer.Currency);
            writer.WriteString("link", offer.Link);
            OptionalString(writer, "videoLink", offer.VideoLink);
            OptionalString(writer, "expiresOn", offer.ExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteBoolean("expiresSoon", offer.ExpiresSoon);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static void OptionalString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value);
        }
    }

    private static void OptionalNumber(Utf8JsonWriter writer, string name, int? value)
    {
        if (value is not null)
        {
            writer.WriteNumber(name, value.Value);
        }
    }

    // ---- decoding ----

    private static CachedTitle Decode(string text)
    {
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        var format = root.GetProperty("format").GetInt32();
        if (format != FormatVersion)
        {
            throw new CacheFormatException($"the entry is cache format {format}; this build reads {FormatVersion}");
        }

        var stamp = new CacheStamp(
            DateTimeOffset.ParseExact(root.GetProperty("fetchedAt").GetString()!, "O", CultureInfo.InvariantCulture),
            root.GetProperty("invalidated").GetBoolean());
        var t = root.GetProperty("title");
        var kind = t.GetProperty("kind").GetString() switch
        {
            "film" => TitleKind.Film,
            "show" => TitleKind.Show,
            var other => throw new CacheFormatException($"title kind '{other}' is not known"),
        };

        var title = new CatalogueTitle
        {
            Id = new TitleId(t.GetProperty("id").GetString()!),
            Kind = kind,
            Name = t.GetProperty("name").GetString()!,
            ImdbId = Text(t, "imdbId"),
            Year = Number(t, "year"),
            Genres = t.GetProperty("genres").EnumerateArray().Select(genre => genre.GetString()!).ToList(),
            Synopsis = t.GetProperty("synopsis").GetString()!,
            PosterUrl = Text(t, "posterUrl"),
            RuntimeMinutes = Number(t, "runtimeMinutes"),
            SeasonCount = Number(t, "seasonCount"),
            EpisodeCount = Number(t, "episodeCount"),
            Rating = ContentRating.Parse(t.GetProperty("rating").GetString()),
            Offers = ReadOffers(t),
            Ended = t.TryGetProperty("ended", out var ended) ? ended.GetBoolean() : null,
            Seasons = t.GetProperty("seasons").EnumerateArray().Select(season => new CatalogueSeason(
                season.GetProperty("number").GetInt32(),
                season.GetProperty("name").GetString()!,
                season.GetProperty("episodes").EnumerateArray().Select(episode => new CatalogueEpisode(
                    new EpisodeRef(episode.GetProperty("season").GetInt32(), episode.GetProperty("number").GetInt32()),
                    episode.GetProperty("name").GetString()!,
                    episode.GetProperty("synopsis").GetString()!,
                    Number(episode, "airYear"),
                    Text(episode, "airDate") is { } date ? DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
                    Text(episode, "airsAt") is { } at ? DateTimeOffset.ParseExact(at, "O", CultureInfo.InvariantCulture) : null,
                    ReadOffers(episode))).ToList())).ToList(),
        };

        return new CachedTitle(title, stamp);
    }

    private static IReadOnlyList<TitleOffer> ReadOffers(JsonElement owner) =>
        owner.GetProperty("offers").EnumerateArray().Select(offer => new TitleOffer(
            new ServiceId(offer.GetProperty("service").GetString()!),
            offer.GetProperty("serviceName").GetString()!,
            offer.GetProperty("known").GetBoolean(),
            (OfferKind)Word(OfferKinds, offer.GetProperty("kind").GetString()),
            (VideoQuality)Word(Qualities, offer.GetProperty("quality").GetString()),
            Text(offer, "price") is { } price ? decimal.Parse(price, NumberStyles.Number, CultureInfo.InvariantCulture) : null,
            Text(offer, "currency"),
            offer.GetProperty("link").GetString()!,
            Text(offer, "videoLink"),
            Text(offer, "expiresOn") is { } expires ? DateOnly.ParseExact(expires, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
            offer.GetProperty("expiresSoon").GetBoolean())).ToList();

    private static int Word(string[] words, string? word)
    {
        var index = Array.IndexOf(words, word);
        return index >= 0 ? index : throw new CacheFormatException($"'{word}' is not a word this build knows");
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}
