using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Sports;
using Pulp.Infrastructure.Catalogue;

namespace Pulp.Infrastructure.Sports;

/// <summary>
/// The PWHL, read from the one thing its own site offers for the purpose: the calendar file behind the schedule page's
/// "Sync to Your Calendar" link (D354). It is a static iCalendar file on the site's asset host, linked so that a
/// calendar app can subscribe to it; reading it is what a calendar app does with it. Nothing else of the league's
/// properties is touched: the site's terms (item xi) forbid automated scripts on its pages, and its stats feed needs
/// a key, so neither is requested here.
/// <para>
/// Shape, measured live: a vCalendar of vEvents, each with <c>UID</c>, a UTC <c>DTSTART</c> (<c>20261205T200000Z</c>),
/// <c>LOCATION</c>, a fixed <c>DESCRIPTION</c> and a <c>SUMMARY</c> of "Away @ Home". Only <c>UID</c>, <c>DTSTART</c> and
/// <c>SUMMARY</c> are read, and a calendar can hold a score in a SUMMARY, so a SUMMARY is accepted only when both
/// sides are bare club names (letters, spaces, a dot or an apostrophe); anything with a digit, bracket, colon, dash or
/// another separator is a refused entry, not a trimmed one (G15). A start that is not plain UTC is refused rather than
/// guessed at. Every game is the regular season because that is the file: a playoff would be a different file, and the
/// league has no championship here. Standings are empty; a calendar has no order and one would reveal results anyway.
/// </para>
/// </summary>
public sealed class PwhlCalendarSource : ISportsSource
{
    /// <summary>
    /// The regular-season file. It is a per-season asset on the site's content host, so it is replaced by a new link each
    /// season; when it stops answering the league shows as unavailable (the cached copy stands) until this is updated.
    /// </summary>
    public const string CalendarUrl = "https://assets.contentstack.io/v3/assets/bltebdb4296e05d53db/bltf9cbf8a71653fae4/FULL_Regular%20Season.ics";

    private static readonly LeagueId Served = new("pwhl");

    // A subscribing calendar client polls such a file every few hours, and the schedule changes only when the league
    // moves a game, so six hours is the polite rate: a refresh this phone does not need is a request the host never
    // asked a subscriber to make. (It was ten minutes, which is a browser's rate, not a subscriber's.)
    private static readonly TimeSpan Reuse = TimeSpan.FromHours(6);

    // A league has far fewer clubs than this. More distinct names than the cap means the file is not the league's
    // schedule (or has been tampered with), and the whole file is refused rather than trimmed to what looks right.
    private const int MostClubs = 16;

    // Words that belong to a result, never to a club's name. A bare club name can carry none, so a SUMMARY with one is
    // refused even though it has no digit: "Harbor Gulls won @ Bay Herons" is a result written in letters.
    private static readonly Regex Refused = new(
        @"\b(?:won|wins|beat|beats|def|final|lost|lose|ot|so|shootout|draw|tied)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex ClubName = new(@"^\p{L}[\p{L}\p{M} .'’]{0,38}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ProviderHttp http;
    private readonly Func<DateTimeOffset> clock;
    private readonly Dictionary<string, string> identify = new()
    {
        ["User-Agent"] = "Pulp (storyworthtelling household guide; reads the PWHL's calendar file as a calendar app would, at most every six hours)",
    };

    private (DateTimeOffset At, ParsedCalendar Calendar)? held;

    public PwhlCalendarSource(HttpClient httpClient, ILookupCounter? counter = null, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        http = new ProviderHttp(httpClient, LookupSource.PwhlCalendar, null, counter, null);
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<LeagueId> Leagues { get; } = [Served];

    public async Task<LeagueSchedule> GetScheduleAsync(LeagueId requested, SportsWindow window, CancellationToken cancellationToken)
    {
        RequireServed(requested);
        var calendar = await CalendarAsync(cancellationToken);
        var inWindow = calendar.Games
            .Where(game => DateOnly.FromDateTime(game.StartsAt.UtcDateTime) >= window.From && DateOnly.FromDateTime(game.StartsAt.UtcDateTime) <= window.To)
            .ToList();
        return new LeagueSchedule(LeagueOf(calendar, window.Today), calendar.Teams, inWindow, null);
    }

    // A calendar has no rank order (and one would reveal results), so there is nothing to ask for and no request is made.
    public async Task<IReadOnlyList<Team>> GetStandingsOrderAsync(LeagueId requested, CancellationToken cancellationToken)
    {
        RequireServed(requested);
        await Task.Yield();
        return [];
    }

    private static void RequireServed(LeagueId requested)
    {
        if (requested != Served)
        {
            throw new ArgumentException($"The PWHL calendar source was not given the league '{requested}'.", nameof(requested));
        }
    }

    private async Task<ParsedCalendar> CalendarAsync(CancellationToken cancellationToken)
    {
        var current = held;
        if (current is { } kept && clock() - kept.At < Reuse && clock() >= kept.At)
        {
            return kept.Calendar;
        }

        var what = "the calendar file";
        var response = await http.GetAsync(new Uri(CalendarUrl), identify, what, cancellationToken);
        var parsed = Parse(response.Body, what);
        held = (clock(), parsed);
        return parsed;
    }

    private ParsedCalendar Parse(string body, string what)
    {
        var lines = Unfold(body);
        if (!lines.Any(line => string.Equals(line.Trim(), "BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase)))
        {
            throw http.Malformed(what, "it is not a calendar");
        }

        var games = new List<Game>();
        var teams = new Dictionary<TeamId, Team>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string?>? entry = null;
        foreach (var line in lines)
        {
            var trimmed = line.TrimEnd();
            if (string.Equals(trimmed, "BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                entry = [];
                continue;
            }

            if (string.Equals(trimmed, "END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (entry is not null && GameOf(entry) is { } game && seen.Add(game.Id.Value))
                {
                    games.Add(game);
                    teams.TryAdd(game.Home.Id, game.Home);
                    teams.TryAdd(game.Away.Id, game.Away);
                }

                entry = null;
                continue;
            }

            if (entry is not null)
            {
                Take(entry, trimmed);
            }
        }

        if (games.Count == 0)
        {
            throw http.Malformed(what, "no entry in it is a game between two clubs with a plain UTC start");
        }

        if (teams.Count > MostClubs)
        {
            throw http.Malformed(what, $"it names {teams.Count} clubs and a league has at most {MostClubs}");
        }

        return new ParsedCalendar(games.OrderBy(game => game.StartsAt).ThenBy(game => game.Id.Value, StringComparer.Ordinal).ToList(), teams.Values.OrderBy(team => team.Name, StringComparer.Ordinal).ToList());
    }

    // RFC 5545 folding: a line that starts with a space or a tab continues the one before it.
    private static List<string> Unfold(string body)
    {
        var lines = new List<string>();
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t') && lines.Count > 0)
            {
                lines[^1] += line[1..];
            }
            else
            {
                lines.Add(line);
            }
        }

        return lines;
    }

    // The three named properties and no other; a start with parameters (a time zone) is held as unusable.
    private static void Take(Dictionary<string, string?> entry, string line)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return;
        }

        var head = line[..colon];
        var semicolon = head.IndexOf(';', StringComparison.Ordinal);
        var name = (semicolon < 0 ? head : head[..semicolon]).ToUpperInvariant();
        if (name is not ("UID" or "DTSTART" or "SUMMARY"))
        {
            return;
        }

        entry[name] = name == "DTSTART" && semicolon >= 0 ? null : Unescape(line[(colon + 1)..]);
    }

    private static string Unescape(string value)
    {
        var text = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '\\' && index + 1 < value.Length && value[index + 1] is '\\' or ',' or ';')
            {
                text.Append(value[++index]);
            }
            else
            {
                text.Append(value[index]);
            }
        }

        return text.ToString();
    }

    private static Game? GameOf(Dictionary<string, string?> entry)
    {
        if (!entry.TryGetValue("UID", out var uid) || string.IsNullOrWhiteSpace(uid)
            || !entry.TryGetValue("DTSTART", out var start) || start is null
            || !entry.TryGetValue("SUMMARY", out var summary) || summary is null
            || !DateTimeOffset.TryParseExact(start, "yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var startsAt))
        {
            return null;
        }

        var sides = summary.Split(" @ ", StringSplitOptions.None).Select(side => side.Trim()).ToList();
        if (sides.Count != 2 || sides.Any(side => !ClubName.IsMatch(side) || Refused.IsMatch(side)))
        {
            return null;
        }

        var away = TeamOf(sides[0]);
        var home = TeamOf(sides[1]);
        if (away is null || home is null || away.Id == home.Id)
        {
            return null;
        }

        return new Game(new GameId(uid.Trim()), home, away, startsAt, GameStage.RegularSeason, string.Empty);
    }

    private static Team? TeamOf(string name)
    {
        var plain = new StringBuilder();
        foreach (var letter in name.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(char.ToLowerInvariant(letter));
            }
        }

        var slug = Regex.Replace(plain.ToString(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? null : new Team(new TeamId("pwhl:" + slug), name, string.Empty);
    }

    // Before the first game, or after the last, the league is between seasons; the calendar says nothing more.
    private static League LeagueOf(ParsedCalendar calendar, DateOnly today)
    {
        var first = DateOnly.FromDateTime(calendar.Games[0].StartsAt.UtcDateTime);
        var last = DateOnly.FromDateTime(calendar.Games[^1].StartsAt.UtcDateTime);
        if (today < first)
        {
            return new League(Served, "PWHL", string.Empty, SeasonPhase.OffSeason, last, first);
        }

        return today > last
            ? new League(Served, "PWHL", string.Empty, SeasonPhase.OffSeason, last, null)
            : new League(Served, "PWHL", string.Empty, SeasonPhase.RegularSeason, last, null);
    }

    private sealed record ParsedCalendar(IReadOnlyList<Game> Games, IReadOnlyList<Team> Teams);
}
