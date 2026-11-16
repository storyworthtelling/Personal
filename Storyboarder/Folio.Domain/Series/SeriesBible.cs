using System.Text.Json;
using System.Text.Json.Serialization;
using Folio.Domain.Analysis;
using Folio.Domain.Markup;

namespace Folio.Domain.Series;

/// <summary>What a series bible entry is about (R20).</summary>
public enum BibleKind
{
    Character,
    Setting,
    Rule,
    Event,
    Thread,
}

/// <summary>
/// One book of a series, as the bible knows it. <paramref name="BookId"/> is the book's project id, so
/// every draft of a book is one book here; <paramref name="Written"/> is its number in the series, the
/// order the books were written in, which decides canon (R23).
/// </summary>
public sealed record SeriesBook(Guid BookId, string Title, int Written);

/// <summary>
/// One entry as one book has it (R20, R24): the story time it holds as of (<paramref name="Era"/>,
/// empty when the book does not say), what the book says, and the book's own words for it.
/// </summary>
public sealed record BibleState(Guid BookId, string Book, string Era, string Text, string Quote);

/// <summary>What one read of a book says about one entry, before it is merged into the bible.</summary>
public sealed record BibleFact(BibleKind Kind, string Name, IReadOnlyList<string> Aliases, string Era, string Text, string Quote);

/// <summary>
/// One character, setting, rule, event or open thread, mapped over the books that mention it (R20).
/// Its states run in the order the books were written, so the first is the one canon established (R23).
/// The owner's correction, when there is one, wins over every book (R26).
/// </summary>
public sealed record BibleEntry(BibleKind Kind, string Name)
{
    public IReadOnlyList<string> Aliases { get; init; } = [];

    public IReadOnlyList<BibleState> States { get; init; } = [];

    /// <summary>The owner's correction, or empty. Never written by a read of a book.</summary>
    public string Correction { get; init; } = string.Empty;

    /// <summary>The state the earliest-written book gave it: the canon.</summary>
    [JsonIgnore]
    public BibleState? Established => States.Count == 0 ? null : States[0];

    /// <summary>What the entry is now: the owner's correction, else the latest-written book's word.</summary>
    [JsonIgnore]
    public string Current =>
        Correction.Length > 0 ? Correction : States.Count == 0 ? string.Empty : States[^1].Text;

    internal bool Answers(BibleKind kind, string name) =>
        kind == Kind && (Same(Name, name) || Aliases.Any(alias => Same(alias, name)));

    internal static bool Same(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A series' bible (release G): its characters, settings, world rules, timeline events and open threads,
/// each mapped over story time as the books tell it, with the passage that established it. It is derived
/// from the manuscripts, so it lives on the phone and in the owner's Drive — never in the repository.
/// Immutable: every change returns a new bible.
/// </summary>
public sealed record SeriesBible(IReadOnlyList<SeriesBook> Books, IReadOnlyList<BibleEntry> Entries)
{
    public static SeriesBible Empty { get; } = new([], []);

    public bool Has(Guid bookId) => Books.Any(book => book.BookId == bookId);

    /// <summary>
    /// The bible with one book's read merged in. A book read again replaces what it said before, so a
    /// later draft's bible is that draft's, and an entry no book mentions any more is gone — unless the
    /// owner has corrected it, which keeps it.
    /// </summary>
    public SeriesBible WithBook(SeriesBook book, IReadOnlyList<BibleFact> facts)
    {
        ArgumentNullException.ThrowIfNull(book);
        var books = Books.Where(known => known.BookId != book.BookId).Append(book)
            .OrderBy(known => known.Written).ThenBy(known => known.Title, StringComparer.Ordinal).ToList();
        var written = books.ToDictionary(known => known.BookId, known => known.Written);

        var entries = Entries
            .Select(entry => entry with { States = [.. entry.States.Where(state => state.BookId != book.BookId)] })
            .Where(entry => entry.States.Count > 0 || entry.Correction.Length > 0)
            .ToList();

        foreach (var fact in facts ?? [])
        {
            if (string.IsNullOrWhiteSpace(fact.Name) || string.IsNullOrWhiteSpace(fact.Text))
                continue;

            var names = fact.Aliases.Prepend(fact.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
            var index = entries.FindIndex(entry => names.Any(name => entry.Answers(fact.Kind, name)));
            var entry = index < 0 ? new BibleEntry(fact.Kind, fact.Name.Trim()) : entries[index];

            var aliases = entry.Aliases
                .Concat(names.Select(name => name.Trim()))
                .Where(name => !BibleEntry.Same(name, entry.Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var state = new BibleState(book.BookId, book.Title, fact.Era.Trim(), fact.Text.Trim(), fact.Quote.Trim());
            var states = entry.States.Where(known => known.BookId != book.BookId).Append(state)
                .OrderBy(known => written.GetValueOrDefault(known.BookId, int.MaxValue)).ToList();

            entry = entry with { Aliases = aliases, States = states };
            if (index < 0)
                entries.Add(entry);
            else
                entries[index] = entry;
        }

        return new SeriesBible(books, entries);
    }

    /// <summary>
    /// The bible with the owner's correction to one entry (R26), which wins over every book. A blank
    /// correction clears it; a name no entry answers to changes nothing.
    /// </summary>
    public SeriesBible Correct(BibleKind kind, string name, string correction)
    {
        var entries = Entries.Select(entry => entry.Answers(kind, name)
            ? entry with { Correction = correction?.Trim() ?? string.Empty }
            : entry).ToList();
        return this with { Entries = entries };
    }

    /// <summary>The bible as the other books left it: what a book is checked against (R24).</summary>
    public SeriesBible Without(Guid bookId) => new(
        [.. Books.Where(book => book.BookId != bookId)],
        [.. Entries
            .Select(entry => entry with { States = [.. entry.States.Where(state => state.BookId != bookId)] })
            .Where(entry => entry.States.Count > 0 || entry.Correction.Length > 0)]);
}

/// <summary>
/// An earlier book's stored canon turned into bible facts (G2): existing series need no second read.
/// The canon holds no quotations, so these facts carry none.
/// </summary>
public static class BibleSeed
{
    public static IReadOnlyList<BibleFact> From(SeriesCanon canon)
    {
        ArgumentNullException.ThrowIfNull(canon);
        var facts = new List<BibleFact>();
        facts.AddRange(canon.Cast.Select(entity => Fact(BibleKind.Character, entity.Name, entity.Description)));
        facts.AddRange(canon.Settings.Select(entity => Fact(BibleKind.Setting, entity.Name, entity.Description)));
        if (!string.IsNullOrWhiteSpace(canon.PriorContinuitySummary))
            facts.Add(Fact(BibleKind.Event, $"Where \"{canon.PriorBookTitle}\" ends", canon.PriorContinuitySummary));
        facts.AddRange(canon.OpenThreads.Select(thread => Fact(BibleKind.Thread, thread.Detail, thread.Detail)));
        return facts;
    }

    private static BibleFact Fact(BibleKind kind, string name, string text) => new(kind, name, [], string.Empty, text, string.Empty);
}

/// <summary>
/// Keeps a fact's quotation only when the book holds it (R24): the bible quotes the earlier book to the
/// later one, and a quotation the book does not contain is an invented one. The fact itself stays.
/// </summary>
public static class BibleQuotes
{
    public static IReadOnlyList<BibleFact> Keep(string manuscript, IReadOnlyList<BibleFact> facts)
    {
        var index = ExcerptIndex.Of(manuscript ?? string.Empty);
        return [.. facts.Select(fact => fact.Quote.Length == 0 || index.Find(fact.Quote) is not null
            ? fact
            : fact with { Quote = string.Empty })];
    }
}

/// <summary>The bible as a prompt is given it: each entry, its books in order, their words, the owner's correction.</summary>
public static class BibleText
{
    private static readonly (BibleKind Kind, string Heading)[] Order =
    [
        (BibleKind.Character, "CHARACTERS"),
        (BibleKind.Setting, "SETTINGS"),
        (BibleKind.Rule, "WORLD RULES"),
        (BibleKind.Event, "TIMELINE EVENTS"),
        (BibleKind.Thread, "OPEN THREADS"),
    ];

    public static string For(SeriesBible bible)
    {
        ArgumentNullException.ThrowIfNull(bible);
        if (bible.Entries.Count == 0)
            return string.Empty;

        var lines = new List<string>
        {
            "SERIES BIBLE, from the books written before this one ("
            + string.Join(", ", bible.Books.Select(book => $"\"{book.Title}\"")) + "). The earliest-written book is canon.",
        };
        foreach (var (kind, heading) in Order)
        {
            var entries = bible.Entries.Where(entry => entry.Kind == kind).ToList();
            if (entries.Count == 0)
                continue;
            lines.Add(heading + ":\n" + string.Join("\n", entries.Select(Entry)));
        }
        return string.Join("\n\n", lines);
    }

    private static string Entry(BibleEntry entry)
    {
        var name = entry.Aliases.Count == 0 ? entry.Name : $"{entry.Name} (also {string.Join(", ", entry.Aliases)})";
        var states = entry.States.Select(state =>
            $"  - in \"{state.Book}\"{(state.Era.Length > 0 ? $", {state.Era}" : string.Empty)}: {state.Text}"
            + (state.Quote.Length > 0 ? $" Quoted: \"{state.Quote}\"" : string.Empty));
        var correction = entry.Correction.Length > 0
            ? [$"  - the author's correction, which wins over the books: {entry.Correction}"]
            : Array.Empty<string>();
        return string.Join("\n", states.Concat(correction).Prepend($"- {name}"));
    }
}

/// <summary>How a bible is stored: JSON, enum names as words, damage read as an empty bible.</summary>
public static class SeriesBibleCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write(SeriesBible bible) => JsonSerializer.Serialize(bible, Options);

    public static SeriesBible Read(string? json) => TryRead(json, out var bible) ? bible : SeriesBible.Empty;

    /// <summary>
    /// False for stored text that is not a bible. A caller about to write must stop there rather than
    /// write a fresh bible over one it could not read, which would lose every earlier book and correction.
    /// </summary>
    public static bool TryRead(string? json, out SeriesBible bible)
    {
        bible = SeriesBible.Empty;
        if (string.IsNullOrWhiteSpace(json))
            return true;
        try
        {
            var read = JsonSerializer.Deserialize<SeriesBible>(json, Options);
            if (read?.Books is null || read.Entries is null)
                return false;
            bible = read;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>
/// The series bible as the plain-text Google Doc kept in Drive (owner's request): one document per
/// series, rewritten in place each time the bible changes, so the doc's own version history shows what
/// each book and each correction changed. Everything is written in a stable order, kinds first, then
/// entries as the bible holds them, so a version differs from the last only where the bible did. The
/// second line says what prompted the version.
/// </summary>
public static class BibleDocumentText
{
    private static readonly (BibleKind Kind, string Heading)[] Order =
    [
        (BibleKind.Character, "CHARACTERS"),
        (BibleKind.Setting, "SETTINGS"),
        (BibleKind.Rule, "WORLD RULES"),
        (BibleKind.Event, "TIMELINE EVENTS"),
        (BibleKind.Thread, "OPEN THREADS"),
    ];

    public static string For(
        string series, SeriesBible bible, IReadOnlyDictionary<Guid, StoryPlacement> placements, string occasion)
    {
        ArgumentNullException.ThrowIfNull(bible);
        var lines = new List<string>
        {
            $"{series} — Series bible",
            $"This version: {occasion}",
            string.Empty,
            "The earliest-written book is canon; your corrections win over every book.",
            string.Empty,
            "BOOKS",
        };
        for (var i = 0; i < bible.Books.Count; i++)
        {
            var book = bible.Books[i];
            var where = placements.TryGetValue(book.BookId, out var placement) ? $" — {placement.Line}" : string.Empty;
            lines.Add($"{i + 1}. {book.Title}{where}");
        }

        foreach (var (kind, heading) in Order)
        {
            var entries = bible.Entries.Where(entry => entry.Kind == kind).ToList();
            if (entries.Count == 0)
                continue;

            lines.Add(string.Empty);
            lines.Add(heading);
            foreach (var entry in entries)
            {
                lines.Add(string.Empty);
                lines.Add(entry.Aliases.Count == 0 ? entry.Name : $"{entry.Name} (also {string.Join(", ", entry.Aliases)})");
                foreach (var state in entry.States)
                {
                    lines.Add($"  {state.Book}{(state.Era.Length > 0 ? $" ({state.Era})" : string.Empty)}: {state.Text}");
                    if (state.Quote.Length > 0)
                        lines.Add($"    “{state.Quote}”");
                }
                if (entry.Correction.Length > 0)
                    lines.Add($"  Your correction: {entry.Correction}");
            }
        }

        return string.Join("\n", lines) + "\n";
    }
}
