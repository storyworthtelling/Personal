using System.Globalization;
using Storyworthtelling.Household;

namespace Constitution.Domain.Household.Records;

/// <summary>
/// Builds a record id from several parts. Each part is percent-escaped before joining, so a part holding
/// the separator cannot make two different tuples share one key — two values on one key would silently
/// overwrite each other in the merge.
/// </summary>
public static class RecordIds
{
    public const char Separator = ':';

    public static string Join(params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return string.Join(Separator, parts.Select(part => Uri.EscapeDataString(
            string.IsNullOrWhiteSpace(part) ? throw new ArgumentException("A record id part needs a value.", nameof(parts)) : part)));
    }
}

/// <summary>
/// The words a field is written in. Enum names are code and move with a rename; the file outlives
/// the build, so every enum a record stores goes through a table of its own words.
/// </summary>
public sealed class FieldWords<TEnum>
    where TEnum : struct, Enum
{
    private readonly Dictionary<TEnum, string> words;
    private readonly Dictionary<string, TEnum> values;

    public FieldWords(IReadOnlyDictionary<TEnum, string> words)
    {
        ArgumentNullException.ThrowIfNull(words);
        var missing = Enum.GetValues<TEnum>().Where(value => !words.ContainsKey(value)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException($"No stored word for {typeof(TEnum).Name}.{string.Join(", ", missing)}.", nameof(words));
        }

        this.words = words.ToDictionary(pair => pair.Key, pair => pair.Value);
        values = words.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
    }

    public string Word(TEnum value) => words[value];

    public bool TryValue(string word, out TEnum value) => values.TryGetValue(word, out value);
}

/// <summary>The one spelling of a calendar day in a record: <c>yyyy-MM-dd</c>, culture-free.</summary>
public static class RecordDates
{
    public const string Format = "yyyy-MM-dd";

    public static string Write(DateOnly date) => date.ToString(Format, CultureInfo.InvariantCulture);

    public static bool TryRead(string text, out DateOnly date) =>
        DateOnly.TryParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

/// <summary>Writes a record's fields as invariant-culture words.</summary>
public sealed class FieldWriter
{
    private readonly SortedDictionary<string, string> fields = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> Fields => fields;

    public FieldWriter Text(string name, string? value)
    {
        fields[name] = value ?? string.Empty;
        return this;
    }

    public FieldWriter Flag(string name, bool value) => Text(name, value ? "true" : "false");

    public FieldWriter Date(string name, DateOnly? value) => Text(name, value is { } date ? RecordDates.Write(date) : null);

    public FieldWriter Word<TEnum>(string name, FieldWords<TEnum> words, TEnum value)
        where TEnum : struct, Enum => Text(name, words.Word(value));
}

/// <summary>
/// Reads a record's fields back. An empty (or blank) field is an absent one. Anything a required field cannot
/// be read as throws <see cref="HouseholdRecordException"/> naming the field, which
/// <see cref="HouseholdRecords.Read"/> reports as unreadable. A date that cannot be read is named but
/// <b>never repeated</b>: the reason travels to logs and lists, and the text may be a birthdate.
/// </summary>
public sealed class RecordReader
{
    private readonly HouseholdRecord record;

    public RecordReader(HouseholdRecord record) => this.record = record ?? throw new ArgumentNullException(nameof(record));

    public RecordKey Key => record.Key;

    public string? OptionalText(string name) =>
        record.Fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public string Text(string name) => OptionalText(name) ?? throw Fail(name, "is missing");

    /// <summary>A flag a writer may not have known to write: absent reads as false.</summary>
    public bool OptionalFlag(string name) => OptionalText(name) switch
    {
        null => false,
        "true" => true,
        "false" => false,
        _ => throw Fail(name, "is not true or false"),
    };

    public DateOnly? OptionalDate(string name)
    {
        var text = OptionalText(name);
        if (text is null)
        {
            return null;
        }

        return RecordDates.TryRead(text, out var date) ? date : throw Fail(name, "is not a date");
    }

    public TEnum Word<TEnum>(string name, FieldWords<TEnum> words)
        where TEnum : struct, Enum
    {
        var text = Text(name);
        return words.TryValue(text, out var value)
            ? value
            : throw Fail(name, $"is '{text}', a word this build does not know");
    }

    public HouseholdRecordException Fail(string name, string problem) =>
        new(record.Key, $"field '{name}' {problem}.");
}
