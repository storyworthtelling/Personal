using System.Globalization;
using System.Text;
using System.Text.Json;
using Codex.Domain.Design;
using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>
/// One filed ghost as the search sees it: who, when, what kind, and the first words, never the
/// whole message. <see cref="Who"/> starts as the contact key; the head swaps in the contact's
/// name, which is what the owner will describe ("the thing Robin sent").
/// </summary>
public sealed record FiledEntry(
    string SourceId,
    DateTimeOffset FiledAtUtc,
    DateTimeOffset SentAtUtc,
    TimelineItemKind Kind,
    bool Incoming,
    string ContactKey,
    string? Subject,
    string? Excerpt)
{
    public string Who { get; init; } = ContactKey;
}

/// <summary>
/// The numbered list of filed entries Claude chooses from, built once per search. The same text
/// goes on every "show more" so the cached prompt matches and the second ask costs a tenth of
/// the first. Lines are numbers rather than ids: shorter to send and to answer, and an answer
/// can only name a line that exists.
/// </summary>
public sealed class FiledCatalogue
{
    /// <summary>About 30,000 tokens: every filed thing for most archives, the best of a large one.</summary>
    public const int DefaultMaxChars = 120_000;

    private const int SubjectChars = 120;
    private const int ExcerptChars = 200;

    private readonly IReadOnlyList<string> ids;
    private readonly Dictionary<string, int> lines;

    private FiledCatalogue(string text, IReadOnlyList<string> ids, int total)
    {
        Text = text;
        this.ids = ids;
        Total = total;
        lines = ids.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index + 1);
    }

    public string Text { get; }

    /// <summary>How many entries made the catalogue.</summary>
    public int Sent => ids.Count;

    /// <summary>How many were filed and settled, sent or not.</summary>
    public int Total { get; }

    public string? IdAt(int line) => line >= 1 && line <= ids.Count ? ids[line - 1] : null;

    public int? LineOf(string sourceId) => lines.TryGetValue(sourceId, out var line) ? line : null;

    /// <summary>
    /// Every entry when they fit. When they do not, the entries the query's words appear in come
    /// first (an old message named outright must not be cut for being old), then the newest of
    /// the rest. Read newest first either way.
    /// </summary>
    public static FiledCatalogue Build(IReadOnlyList<FiledEntry> entries, string query, int maxChars = DefaultMaxChars)
    {
        var words = FiledWords.Of(query);
        var priority = entries
            .OrderByDescending(entry => FiledWords.Score(entry, words) > 0)
            .ThenByDescending(entry => entry.SentAtUtc)
            .ToList();

        var chosen = new List<FiledEntry>();
        var length = 0;
        foreach (var entry in priority)
        {
            // Measured with the widest line number, so renumbering cannot push it over.
            var cost = Line(entries.Count, entry).Length + 1;
            if (length + cost > maxChars)
            {
                continue;
            }
            chosen.Add(entry);
            length += cost;
        }

        var ordered = chosen.OrderByDescending(entry => entry.SentAtUtc).ToList();
        var text = string.Join('\n', ordered.Select((entry, index) => Line(index + 1, entry)));
        return new FiledCatalogue(text, ordered.Select(entry => entry.SourceId).ToList(), entries.Count);
    }

    private static string Line(int number, FiledEntry entry) => string.Join(" | ",
        number.ToString(CultureInfo.InvariantCulture),
        entry.SentAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        KindWord(entry),
        Clean(entry.Who, SubjectChars),
        Clean(entry.Subject, SubjectChars),
        Clean(entry.Excerpt, ExcerptChars));

    private static string KindWord(FiledEntry entry)
    {
        var kind = entry.Kind switch
        {
            TimelineItemKind.Email => "email",
            TimelineItemKind.Message => "text",
            TimelineItemKind.Call => "call",
            _ => "voicemail",
        };
        return entry.Kind == TimelineItemKind.Voicemail ? "voicemail from" : $"{kind} {(entry.Incoming ? "from" : "to")}";
    }

    // One line per entry: a newline would start a fake line and a pipe a fake column.
    private static string Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }
        var flat = string.Join(' ', value.Replace('|', '/')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= max ? flat : flat[..max] + "…";
    }
}

/// <summary>One result Claude picked, and whether it said the fit was strong or only possible.</summary>
public sealed record FiledPick(string SourceId, bool Strong);

/// <summary>
/// What Claude answered, read strictly. A line that is not in the catalogue, one already shown,
/// a repeat, or anything past five is <b>rejected and counted</b>, because each is the model
/// doing the job badly and the log should show it.
/// </summary>
public sealed record FiledAnswer(IReadOnlyList<FiledPick> Picks, int Rejected, bool Garbled)
{
    public const int MaxPicks = 5;

    public static FiledAnswer Parse(string? reply, FiledCatalogue catalogue, IReadOnlyCollection<string> shown)
    {
        var garbled = new FiledAnswer([], 0, Garbled: true);
        if (string.IsNullOrWhiteSpace(reply))
        {
            return garbled;
        }
        var start = reply.IndexOf('{');
        var end = reply.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return garbled;
        }

        try
        {
            using var document = JsonDocument.Parse(reply[start..(end + 1)]);
            if (!document.RootElement.TryGetProperty("matches", out var matches)
                || matches.ValueKind != JsonValueKind.Array)
            {
                return garbled;
            }

            var picks = new List<FiledPick>();
            var rejected = 0;
            foreach (var match in matches.EnumerateArray())
            {
                var id = match.ValueKind == JsonValueKind.Object
                    && match.TryGetProperty("line", out var line)
                    && line.TryGetInt32(out var number)
                        ? catalogue.IdAt(number)
                        : null;
                if (id is null || shown.Contains(id) || picks.Any(pick => pick.SourceId == id) || picks.Count >= MaxPicks)
                {
                    rejected++;
                    continue;
                }
                var strong = match.TryGetProperty("fit", out var fit)
                    && fit.ValueKind == JsonValueKind.String
                    && string.Equals(fit.GetString(), "strong", StringComparison.OrdinalIgnoreCase);
                picks.Add(new FiledPick(id, strong));
            }
            return new FiledAnswer(picks, rejected, Garbled: false);
        }
        catch (JsonException)
        {
            return garbled;
        }
    }
}

/// <summary>
/// Searching by the query's own words: what Filed falls back on without a key or when Claude
/// cannot be reached, how a large archive decides what to send, and the log's cross-check on
/// Claude's picks. Filler words are dropped and accents folded, as the filters do.
/// </summary>
public static class FiledWords
{
    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "from", "about", "that", "this", "with", "was", "were", "what", "when", "where",
        "who", "which", "some", "something", "thing", "things", "one", "sent", "send", "email", "emails",
        "text", "texts", "message", "messages", "looking", "find", "last", "you", "your", "our", "his", "her",
        "their", "they", "them", "had", "has", "have", "are", "but", "not", "any", "all", "there", "into",
        "over", "can", "could", "would", "should", "did", "does", "got", "get", "said", "saying",
    };

    public static IReadOnlyList<string> Of(string query) =>
        new string(FilterMatch.Fold(query).Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 3 && !Filler.Contains(word))
            .Distinct()
            .ToList();

    public static int Score(FiledEntry entry, IReadOnlyList<string> words)
    {
        if (words.Count == 0)
        {
            return 0;
        }
        var haystack = FilterMatch.Fold($"{entry.Who} {entry.ContactKey} {entry.Subject} {entry.Excerpt}");
        return words.Count(word => haystack.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>The best <paramref name="take"/> by words matched, newest first among equals.</summary>
    public static IReadOnlyList<string> Find(
        IReadOnlyList<FiledEntry> entries, string query, IReadOnlyCollection<string> exclude, int take)
    {
        var words = Of(query);
        return entries
            .Where(entry => !exclude.Contains(entry.SourceId))
            .Select(entry => (entry, score: Score(entry, words)))
            .Where(pair => pair.score > 0)
            .OrderByDescending(pair => pair.score)
            .ThenByDescending(pair => pair.entry.SentAtUtc)
            .Take(take)
            .Select(pair => pair.entry.SourceId)
            .ToList();
    }

    /// <summary>Entries every content word of the query appears in.</summary>
    public static IReadOnlyList<string> Complete(IReadOnlyList<FiledEntry> entries, string query)
    {
        var words = Of(query);
        return words.Count == 0
            ? []
            : entries.Where(entry => Score(entry, words) == words.Count).Select(entry => entry.SourceId).ToList();
    }
}

/// <summary>
/// One search, across however many times the owner asks for more: what has been shown, in
/// order, and whether the last ask found nothing new.
/// </summary>
public sealed record FiledSearchSession(string Query, int Round, IReadOnlyList<string> Shown, bool Exhausted)
{
    private IReadOnlyList<int> RoundEnds { get; init; } = [];

    public static FiledSearchSession Start(string query) => new(query.Trim(), 0, [], false);

    public FiledSearchSession Record(IReadOnlyList<string> found)
    {
        var added = found.Where(id => !Shown.Contains(id)).Distinct().ToList();
        var shown = Shown.Concat(added).ToList();
        return this with
        {
            Round = Round + 1,
            Shown = shown,
            Exhausted = added.Count == 0,
            RoundEnds = [.. RoundEnds, shown.Count],
        };
    }

    /// <summary>Where a result sat in everything shown, counting from 1.</summary>
    public int? Rank(string sourceId)
    {
        var index = Shown.ToList().IndexOf(sourceId);
        return index < 0 ? null : index + 1;
    }

    /// <summary>Which ask brought a result.</summary>
    public int? RoundOf(string sourceId)
    {
        if (Rank(sourceId) is not { } rank)
        {
            return null;
        }
        var round = RoundEnds.ToList().FindIndex(end => rank <= end);
        return round < 0 ? null : round + 1;
    }
}

/// <summary>
/// The words of the call. The instruction and the catalogue are the system prompt, identical
/// for every ask in a search so it caches; what changes — the date, the query, what has been
/// shown — goes in the user turn after it.
/// </summary>
public static class FiledSearchPrompt
{
    public static string System(FiledCatalogue catalogue) =>
        "You find things in a person's archive of filed correspondence: emails, texts, calls and voicemails. "
        + "Each catalogue line is: line number | date sent | kind and direction | person | subject | opening words. "
        + "They describe what they are looking for in plain English; it may name a person, a topic, a rough time "
        + "or only what the message was for. Pick the lines that fit, best first, at most 5. Pick fewer, or none, "
        + "when nothing else fits well: an empty answer is better than a padded one. Mark each pick \"strong\" when "
        + "it clearly is what they describe and \"weak\" when it only might be. Never pick a line listed as already "
        + "shown. Reply with ONLY JSON: {\"matches\":[{\"line\":12,\"fit\":\"strong\"}]}\n\nCatalogue:\n"
        + catalogue.Text;

    public static string Ask(string query, IReadOnlyList<int> shownLines, DateOnly today) =>
        new StringBuilder()
            .Append("Today is ").Append(today.ToString("yyyy-MM-dd (dddd)", CultureInfo.InvariantCulture)).Append(".\n")
            .Append("Looking for: ").Append(query.Trim()).Append('\n')
            .Append("Already shown: ")
            .Append(shownLines.Count == 0 ? "none" : string.Join(", ", shownLines))
            .ToString();
}
