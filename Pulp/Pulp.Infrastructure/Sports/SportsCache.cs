using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pulp.Domain.Sports;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Infrastructure.Sports;

/// <summary>A cached league feed and when it was fetched.</summary>
public sealed record CachedLeague(LeagueFeed Feed, DateTimeOffset FetchedAt);

public sealed record SportsCacheRead(CacheReadStatus Status, CachedLeague? Entry, string? Problem);

/// <summary>
/// The on-device copy of each league's feed, so the Sports tab paints at once from what was last
/// fetched and <see cref="SportsFreshness"/> decides whether a fetch follows. Same shape as the
/// catalogue cache: one file per league, named by a hash of its id, written atomically; a damaged or
/// newer-format entry comes back <see cref="CacheReadStatus.Unreadable"/> with the reason, for the caller
/// to log before fetching again. The feed holds schedules and rank order, never a result, so the cache
/// holds none either. Hand-written JSON, so the trimmed Android build cannot strip a property.
/// </summary>
public sealed class SportsCache
{
    public const int FormatVersion = 1;

    private readonly string directory;
    private readonly object gate = new();

    public SportsCache(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        this.directory = directory;
    }

    public SportsCacheRead Read(LeagueId league)
    {
        var path = PathOf(league);
        lock (gate)
        {
            if (!File.Exists(path))
            {
                return new SportsCacheRead(CacheReadStatus.Missing, null, null);
            }

            try
            {
                return new SportsCacheRead(CacheReadStatus.Hit, Decode(File.ReadAllText(path)), null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Anything that stops an entry being read (a locked or unreadable file, a null where text should be, a
                // wrong-typed field) means "no cache" for this league: the caller logs the reason and fetches again.
                return new SportsCacheRead(CacheReadStatus.Unreadable, null, $"Cached league {league} could not be read: {exception.Message}");
            }
        }
    }

    public void Store(LeagueFeed feed, DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(feed);
        var text = Encode(feed, fetchedAt);
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            var path = PathOf(feed.League.Id);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, text, Encoding.UTF8);
            File.Move(temporary, path, overwrite: true);
        }
    }

    private string PathOf(LeagueId league)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(league.Value.ToLowerInvariant()));
        return Path.Combine(directory, Convert.ToHexStringLower(hash) + ".json");
    }

    private sealed class CacheFormatException(string message) : Exception(message);

    // ---- encoding ----

    private static string Instant(DateTimeOffset at) => at.ToString("O", CultureInfo.InvariantCulture);

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static readonly string[] Phases = ["preseason", "regular", "postseason", "off"];
    private static readonly string[] Stages = ["preseason", "regular", "playoffs", "championship"];

    private static string Encode(LeagueFeed feed, DateTimeOffset fetchedAt)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", FormatVersion);
            writer.WriteString("fetchedAt", Instant(fetchedAt));

            writer.WriteStartObject("league");
            writer.WriteString("id", feed.League.Id.Value);
            writer.WriteString("name", feed.League.Name);
            writer.WriteString("badge", feed.League.BadgeArt);
            writer.WriteString("phase", Phases[(int)feed.League.Phase]);
            writer.WriteString("seasonEnds", Day(feed.League.SeasonEnds));
            if (feed.League.NextDate is { } next)
            {
                writer.WriteString("nextDate", Day(next));
            }

            writer.WriteEndObject();

            WriteTeams(writer, "teams", feed.Teams);
            writer.WriteStartArray("schedule");
            foreach (var game in feed.Schedule)
            {
                writer.WriteStartObject();
                writer.WriteString("id", game.Id.Value);
                writer.WritePropertyName("home");
                WriteTeam(writer, game.Home);
                writer.WritePropertyName("away");
                WriteTeam(writer, game.Away);
                writer.WriteString("startsAt", Instant(game.StartsAt));
                writer.WriteString("stage", Stages[(int)game.Stage]);
                writer.WriteString("channel", game.Channel);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            WriteTeams(writer, "order", feed.Standings);
            writer.WriteStartArray("highlights");
            foreach (var highlight in feed.Highlights)
            {
                writer.WriteStartObject();
                writer.WriteString("videoId", highlight.VideoId);
                writer.WriteString("title", highlight.Title);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            if (feed.Championship is { } championship)
            {
                writer.WriteStartObject("championship");
                writer.WriteString("id", championship.Id.Value);
                writer.WriteString("name", championship.Name);
                writer.WriteString("startsAt", Instant(championship.StartsAt));
                WriteTeams(writer, "teams", championship.Teams);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteTeams(Utf8JsonWriter writer, string name, IReadOnlyList<Team> teams)
    {
        writer.WriteStartArray(name);
        foreach (var team in teams)
        {
            WriteTeam(writer, team);
        }

        writer.WriteEndArray();
    }

    private static void WriteTeam(Utf8JsonWriter writer, Team team)
    {
        writer.WriteStartObject();
        writer.WriteString("id", team.Id.Value);
        writer.WriteString("name", team.Name);
        writer.WriteString("badge", team.BadgeArt);
        writer.WriteEndObject();
    }

    // ---- decoding ----

    private static CachedLeague Decode(string text)
    {
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        var format = root.GetProperty("format").GetInt32();
        if (format != FormatVersion)
        {
            throw new CacheFormatException($"the entry is cache format {format}; this build reads {FormatVersion}");
        }

        var l = root.GetProperty("league");
        var league = new League(
            new LeagueId(Text(l, "id")),
            Text(l, "name"),
            Text(l, "badge"),
            (SeasonPhase)Word(Phases, Text(l, "phase")),
            ParseDay(Text(l, "seasonEnds")),
            l.TryGetProperty("nextDate", out _) ? ParseDay(Text(l, "nextDate")) : null);

        var championship = root.TryGetProperty("championship", out var c)
            ? new Championship(
                new GameId(Text(c, "id")),
                Text(c, "name"),
                ParseInstant(Text(c, "startsAt")),
                ReadTeams(c.GetProperty("teams")))
            : null;

        var feed = new LeagueFeed(
            league,
            ReadTeams(root.GetProperty("teams")),
            root.GetProperty("schedule").EnumerateArray().Select(game => new Game(
                new GameId(Text(game, "id")),
                ReadTeam(game.GetProperty("home")),
                ReadTeam(game.GetProperty("away")),
                ParseInstant(Text(game, "startsAt")),
                (GameStage)Word(Stages, Text(game, "stage")),
                Text(game, "channel"))).ToList(),
            ReadTeams(root.GetProperty("order")),
            root.GetProperty("highlights").EnumerateArray()
                .Select(highlight => new Highlight(Text(highlight, "videoId"), Text(highlight, "title"))).ToList(),
            championship);
        return new CachedLeague(feed, ParseInstant(Text(root, "fetchedAt")));
    }

    private static List<Team> ReadTeams(JsonElement array) => array.EnumerateArray().Select(ReadTeam).ToList();

    private static Team ReadTeam(JsonElement team) =>
        new(new TeamId(Text(team, "id")), Text(team, "name"), Text(team, "badge"));

    // A null or wrong-typed value is a damaged entry, not a null that slips into a record and fails later.
    private static string Text(JsonElement parent, string name)
    {
        var value = parent.GetProperty(name);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw new CacheFormatException($"'{name}' has no text")
            : throw new CacheFormatException($"'{name}' is {value.ValueKind}, not text");
    }

    private static DateOnly ParseDay(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseInstant(string text) => DateTimeOffset.ParseExact(text, "O", CultureInfo.InvariantCulture);

    private static int Word(string[] words, string? word)
    {
        var index = Array.IndexOf(words, word);
        return index >= 0 ? index : throw new CacheFormatException($"'{word}' is not a word this build knows");
    }
}
