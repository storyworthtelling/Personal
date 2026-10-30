using System.Text.RegularExpressions;

namespace Constitution.Domain.Diagnostics;

/// <summary>
/// The Body and vault trails (<c>body.log</c>, <c>vault.log</c>) as the Diagnostics export may carry them (review S8).
/// The trails are where the app says what the platform and the bed answered ("body read steps: ok, 2 readings"), so
/// without them the owner's device test (D97) has nothing to bring back. They are written to hold no value, but a
/// promise made where the line is written is not a promise this file can keep, so every line is checked again here.
///
/// <para><b>A line is kept whole or withheld whole.</b> It must be a time stamp, a level, one of the two trail
/// sources and then a message in which every token is one of: a word of the app's own vocabulary
/// (<see cref="Vocabulary"/>, lower case, hyphenated parts each checked), a count (1 to 4 digits, beside a word that
/// counts things), a three-digit HTTP status on a bed line, or an exception type name. Any other character, digit,
/// capital, symbol, over-long run or unknown word withholds the line. There is no trimming: a half-cleaned line is
/// one somebody has to trust.</para>
///
/// <para><b>What this cannot tell apart:</b> a count of readings from a value written as one ("187 readings"). The
/// trails never write a value; this is the second line of defence, not the first (<c>TrailLinesTests</c> says so).
/// And a lower-case word that is in the vocabulary but means something else on the line. The vocabulary is a closed
/// list, so a new word in a trail phrase is a decision (<c>TrailVocabularyTests</c> fails until it is made).</para>
/// </summary>
public static class TrailLines
{
    /// <summary>How many of the newest lines of each trail the export carries.</summary>
    public const int MostRecent = 40;

    /// <summary>The longest message kept, in characters.</summary>
    public const int LongestMessage = 160;

    /// <summary>Stands in for a line that was withheld, so the reader knows something was there and where.</summary>
    public const string Withheld = "(line withheld)";

    private static readonly Regex Entry = new(
        @"^\[(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec) [0-9]{1,2}, [0-9]{1,2}:[0-9]{2} (?:am|pm)\] "
        + @"(?:INFO|WARN|ERROR|CRASH) (?:Body|Vault) — (?<message>[^\r\n]+)$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex AllowedCharacters = new(
        @"^[A-Za-z0-9 :,;()\-]+$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex Count = new(@"^[0-9]{1,4}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly Regex TypeName = new(
        @"^[A-Z][A-Za-z]{0,30}(?:Exception|Error)$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static readonly HashSet<string> OtherTypeNames = new(StringComparer.Ordinal) { "NoError", "NoResponse", "sleepData" };

    // A count stands beside a word that counts things (before it or after it).
    private static readonly HashSet<string> CountWords = new(StringComparer.Ordinal)
    {
        "reading", "readings", "record", "records", "usable", "field", "fields", "read", "not", "stored",
        "pages", "documents", "questions", "sessions", "events", "people",
    };

    private static readonly HashSet<int> HttpStatuses = [200, 201, 204, 400, 401, 403, 404, 408, 429, 500, 502, 503, 504];

    /// <summary>
    /// Every word the trails use, lower case. Gathered from the phrases the head, Infrastructure and Domain write
    /// (<c>TrailVocabularyTests</c> reads those sources and fails on a word missing here), plus the lower-cased
    /// names of the outcome enums they print.
    /// </summary>
    public static IReadOnlySet<string> Vocabulary { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "a",
        "account",
        "active",
        "again",
        "allow",
        "already",
        "an",
        "answer",
        "arrived",
        "as",
        "ask",
        "asked",
        "at",
        "authored",
        "back",
        "background",
        "backup",
        "be",
        "bed",
        "before",
        "begun",
        "blocked",
        "body",
        "box",
        "broadcast",
        "broke",
        "budget",
        "built",
        "calendar",
        "calendars",
        "called",
        "calories",
        "camera",
        "choices",
        "composition",
        "configured",
        "connect",
        "corrupt",
        "could",
        "data",
        "datasyncinprogress",
        "date",
        "day",
        "days",
        "deleted",
        "delivered",
        "denied",
        "did",
        "digest",
        "document",
        "documents",
        "download",
        "dropped",
        "earlier",
        "empty",
        "encouragement",
        "events",
        "extra",
        "failed",
        "failure",
        "field",
        "fields",
        "file",
        "first",
        "found",
        "foundnothingread",
        "from",
        "gap",
        "gave",
        "granted",
        "has",
        "health",
        "heart",
        "here",
        "history",
        "id",
        "in",
        "ink",
        "install",
        "intro",
        "invalidargument",
        "io",
        "is",
        "it",
        "json",
        "kept",
        "key",
        "known",
        "late",
        "launch",
        "left",
        "line",
        "list",
        "loaded",
        "login",
        "made",
        "meal",
        "minutes",
        "month",
        "monthly",
        "neverran",
        "new",
        "newer",
        "no",
        "nologin",
        "none",
        "nopassphrase",
        "nosuchside",
        "not",
        "nothing",
        "notsignedin",
        "object",
        "occasions",
        "off",
        "offline",
        "ok",
        "on",
        "one",
        "only",
        "open",
        "opened",
        "out",
        "owner",
        "page",
        "pages",
        "pantry",
        "parse",
        "partial",
        "passphrase",
        "people",
        "permission",
        "persist",
        "personal",
        "photo",
        "placement",
        "plan",
        "planned",
        "planner",
        "profile",
        "publish",
        "published",
        "questions",
        "queued",
        "ran",
        "rate",
        "ratelimitexceeded",
        "reached",
        "read",
        "reading",
        "readings",
        "recipe",
        "record",
        "recorded",
        "records",
        "refused",
        "rejected",
        "remote",
        "replaced",
        "reply",
        "resolve",
        "rest",
        "resting",
        "restore",
        "restored",
        "right",
        "roster",
        "rules",
        "save",
        "saved",
        "seat",
        "security",
        "sentence",
        "service",
        "session",
        "sessions",
        "set",
        "settings",
        "settled",
        "shape",
        "shared",
        "sheet",
        "side",
        "sign",
        "signal",
        "signed",
        "signinrejected",
        "skipped",
        "sleep",
        "sleeper",
        "sleepers",
        "sleepiq",
        "slot",
        "small",
        "spent",
        "steps",
        "stored",
        "strength",
        "succeeded",
        "sync",
        "tablet",
        "taste",
        "the",
        "this",
        "time",
        "timed",
        "to",
        "today",
        "too",
        "top",
        "trash",
        "trashed",
        "try",
        "unchanged",
        "unexpected",
        "unexpectedshape",
        "unknown",
        "unreachable",
        "unreadable",
        "unsupported",
        "unsupportedoperation",
        "unusable",
        "up",
        "usable",
        "variability",
        "vault",
        "version",
        "voice",
        "week",
        "weight",
        "what",
        "will",
        "withdrawn",
        "withheld",
        "without",
        "worker",
        "workout",
        "written",
        "wrong",
        "yet",
    };

    /// <summary>The line, kept exactly as it was, or null when any part of it is not allowed.</summary>
    public static string? Filter(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        Match entry;
        try
        {
            entry = Entry.Match(line);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        if (!entry.Success)
        {
            return null;
        }

        var message = entry.Groups["message"].Value;
        return MessageAllowed(message) ? line : null;
    }

    /// <summary>
    /// The newest <see cref="MostRecent"/> entries of a trail, oldest first, each filtered; one that is withheld is
    /// replaced by <see cref="Withheld"/>. Only an entry's first line is read: what follows it (an exception's
    /// detail) is not part of a trail line and is never carried.
    /// </summary>
    public static IReadOnlyList<TrailLine> Last(string trail)
    {
        ArgumentNullException.ThrowIfNull(trail);

        var entries = trail
            .Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(block => block.TrimStart('\r', '\n').Split('\n')[0].TrimEnd('\r'))
            .Where(first => first.Length > 0)
            .TakeLast(MostRecent);

        return entries.Select(first => TrailLine.From(first) ?? TrailLine.FromWithheld()).ToList();
    }

    private static bool MessageAllowed(string message)
    {
        if (message.Length > LongestMessage || !AllowedCharacters.IsMatch(message))
        {
            return false;
        }

        var tokens = message.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim('(', ')', ':', ',', ';'))
            .ToList();
        if (tokens.Count == 0 || tokens.Any(token => token.Length == 0))
        {
            return false;
        }

        var bedLine = tokens[0] == "sleepiq";
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (Count.IsMatch(token))
            {
                if (!CountStandsBesideACountWord(tokens, index) && !(bedLine && IsStatus(token)))
                {
                    return false;
                }

                continue;
            }

            if (!WordAllowed(token))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CountStandsBesideACountWord(List<string> tokens, int index) =>
        (index + 1 < tokens.Count && CountWords.Contains(tokens[index + 1]))
        || (index > 0 && CountWords.Contains(tokens[index - 1]));

    private static bool IsStatus(string token) =>
        token.Length == 3 && int.TryParse(token, out var status) && HttpStatuses.Contains(status);

    private static bool WordAllowed(string token)
    {
        if (OtherTypeNames.Contains(token) || TypeName.IsMatch(token))
        {
            return true;
        }

        // Lower case only, hyphenated parts each in the vocabulary: "resting-heart-rate", "open-time".
        return token.Split('-').All(part => part.Length > 0 && Vocabulary.Contains(part));
    }
}

/// <summary>
/// One trail line that has been through <see cref="TrailLines.Filter"/>. There is no other way to make one, so an
/// export that holds a <see cref="TrailLine"/> holds words and counts.
/// </summary>
public sealed record TrailLine
{
    private TrailLine(string value)
    {
        Value = value;
    }

    /// <summary>The line.</summary>
    public string Value { get; }

    /// <summary>The line for this raw text, or null when the filter withholds it.</summary>
    public static TrailLine? From(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        return TrailLines.Filter(raw) is { } kept ? new TrailLine(kept) : null;
    }

    internal static TrailLine FromWithheld() => new(TrailLines.Withheld);
}

/// <summary>Which trail a section of the export is: the Body trail (reads, the bed, the backup) or the vault's.</summary>
public enum TrailKind
{
    /// <summary>What the Body reads, the bed and the Body backup said (<c>body.log</c>).</summary>
    Body,

    /// <summary>What the vault restore and publish said (<c>vault.log</c>).</summary>
    Vault,
}

/// <summary>
/// The newest filtered lines of one trail. At most <see cref="TrailLines.MostRecent"/> of them, and each only a
/// <see cref="TrailLine"/>, which can be made nowhere but through the filter.
/// </summary>
public sealed class TrailSection
{
    public TrailSection(TrailKind kind, IEnumerable<TrailLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        Kind = ComponentOutcome.Defined(kind, nameof(kind));
        Lines = lines.ToList();
        if (Lines.Count > TrailLines.MostRecent)
        {
            throw new ArgumentException("A trail section carries at most the newest forty lines.", nameof(lines));
        }
    }

    public TrailKind Kind { get; }

    public IReadOnlyList<TrailLine> Lines { get; }
}
