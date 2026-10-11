using System.Globalization;

namespace Pulp.Domain.Household.Records;

/// <summary>
/// Builds a record id from several parts. Each part is percent-escaped before joining, so a viewer
/// id holding the separator cannot make two different (viewer, title) pairs share one key — two
/// values on one key would silently overwrite each other in the merge.
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

    public FieldWriter Number(string name, long? value) =>
        Text(name, value?.ToString(CultureInfo.InvariantCulture));

    public FieldWriter Flag(string name, bool value) => Text(name, value ? "true" : "false");

    /// <summary>An instant with its offset, in the round-trip format.</summary>
    public FieldWriter Instant(string name, DateTimeOffset? value) =>
        Text(name, value?.ToString("O", CultureInfo.InvariantCulture));

    /// <summary>A length as whole milliseconds.</summary>
    public FieldWriter Length(string name, TimeSpan? value) =>
        Number(name, value is null ? null : (long)Math.Round(value.Value.TotalMilliseconds));

    public FieldWriter Word<TEnum>(string name, FieldWords<TEnum> words, TEnum value)
        where TEnum : struct, Enum => Text(name, words.Word(value));

    /// <summary>An ordered list: each item escaped, joined by commas. Empty items carry nothing and are left out.</summary>
    public FieldWriter List(string name, IEnumerable<string> items) =>
        Text(name, string.Join(',', items.Where(item => !string.IsNullOrEmpty(item)).Select(Uri.EscapeDataString)));
}

/// <summary>
/// Reads a record's fields back. An empty (or blank) field is an absent one. Anything a required field cannot
/// be read as throws <see cref="HouseholdRecordException"/> naming the field, which
/// <see cref="HouseholdRecords.Read"/> reports as unreadable.
/// </summary>
public sealed class RecordReader
{
    private readonly HouseholdRecord record;

    public RecordReader(HouseholdRecord record) => this.record = record ?? throw new ArgumentNullException(nameof(record));

    public RecordKey Key => record.Key;

    public string? OptionalText(string name) =>
        record.Fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public string Text(string name) => OptionalText(name) ?? throw Fail(name, "is missing");

    public long? OptionalNumber(string name)
    {
        var text = OptionalText(name);
        if (text is null)
        {
            return null;
        }

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw Fail(name, $"is '{text}', not a whole number");
    }

    public int? OptionalInt(string name)
    {
        var number = OptionalNumber(name);
        return number is null ? null
            : number is >= int.MinValue and <= int.MaxValue ? (int)number.Value
            : throw Fail(name, $"is {number}, out of range");
    }

    public int Int(string name) => OptionalInt(name) ?? throw Fail(name, "is missing");

    public bool Flag(string name) => Text(name) switch
    {
        "true" => true,
        "false" => false,
        var other => throw Fail(name, $"is '{other}', not true or false"),
    };

    public DateTimeOffset? OptionalInstant(string name)
    {
        var text = OptionalText(name);
        if (text is null)
        {
            return null;
        }

        return DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at
            : throw Fail(name, $"is '{text}', not a round-trip instant");
    }

    public DateTimeOffset Instant(string name) => OptionalInstant(name) ?? throw Fail(name, "is missing");

    public TimeSpan? OptionalLength(string name) =>
        OptionalNumber(name) is { } millis ? TimeSpan.FromMilliseconds(millis) : null;

    public TEnum Word<TEnum>(string name, FieldWords<TEnum> words)
        where TEnum : struct, Enum
    {
        var text = Text(name);
        return words.TryValue(text, out var value)
            ? value
            : throw Fail(name, $"is '{text}', a word this build does not know");
    }

    public IReadOnlyList<string> List(string name)
    {
        var text = OptionalText(name);
        return text is null ? [] : text.Split(',').Select(Uri.UnescapeDataString).ToList();
    }

    public HouseholdRecordException Fail(string name, string problem) =>
        new(record.Key, $"field '{name}' {problem}.");
}
