using System.Text.Json;
using System.Text.Json.Serialization;

namespace Foreword.Domain.Models;

/// <summary>
/// One column of the daily calendar: a name the owner chose when they set up their columns in
/// Foreword.
///
/// <para>It was an enum of the household's four people plus Watch, which put real names in the source
/// and fixed the page to one family. The columns are now each account's own choices (see
/// <c>ColumnLayout</c>), and the code knows exactly one column by name: <see cref="Watch"/>, which is
/// a category — games, fixtures, anything followed rather than attended — not a person.</para>
///
/// <para><b>The wire did not change.</b> The enum was always stored as its word, never its number, so
/// a column the old app wrote reads back as the same column. Names compare without case, because the
/// old enum read them that way too.</para>
/// </summary>
[JsonConverter(typeof(PersonColumnJsonConverter))]
public readonly struct PersonColumn : IEquatable<PersonColumn>
{
    /// <summary>The fifth column: what the household follows rather than attends.</summary>
    public static readonly PersonColumn Watch = new("Watch");

    private readonly string? name;

    public PersonColumn(string name)
    {
        this.name = name?.Trim();
    }

    public string Name => name ?? string.Empty;

    public bool IsWatch => Equals(Watch);

    public bool Equals(PersonColumn other) =>
        string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is PersonColumn other && Equals(other);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Name);

    public override string ToString() => Name;

    public static bool operator ==(PersonColumn left, PersonColumn right) => left.Equals(right);

    public static bool operator !=(PersonColumn left, PersonColumn right) => !left.Equals(right);
}

/// <summary>
/// Writes a column as its name and reads one back from a name — as a value and as a dictionary key,
/// since the mapping keys addresses and words by column. A number is refused: the old enum was never
/// stored as one, so a number on the wire is a document nothing here wrote.
/// </summary>
public sealed class PersonColumnJsonConverter : JsonConverter<PersonColumn>
{
    public override PersonColumn Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? new PersonColumn(reader.GetString()!)
            : throw new JsonException($"A calendar column is stored as its name, not as {reader.TokenType}.");

    public override void Write(Utf8JsonWriter writer, PersonColumn value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Name);

    public override PersonColumn ReadAsPropertyName(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString()!);

    public override void WriteAsPropertyName(
        Utf8JsonWriter writer, PersonColumn value, JsonSerializerOptions options) =>
        writer.WritePropertyName(value.Name);
}
