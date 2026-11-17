using System.Text.Json;
using System.Text.Json.Serialization;

namespace Folio.Domain.Series;

/// <summary>Where a book sits in its series' story time, against the other books (R21).</summary>
public enum PlacementKind
{
    After,
    Before,
    Between,
    Era,
}

/// <summary>
/// Where a book sits in its series' chronology (R21): after or before another book, between two, or in
/// an era of its own. Folio infers it and the owner confirms it; until then <see cref="Confirmed"/> is
/// false and anything that relies on it says what it assumed. Books are named by title, as the owner
/// knows them.
/// </summary>
public sealed record StoryPlacement(PlacementKind Kind, string Book, string OtherBook, string Era, bool Confirmed)
{
    public static StoryPlacement After(string book) => new(PlacementKind.After, book, string.Empty, string.Empty, false);

    public static StoryPlacement Before(string book) => new(PlacementKind.Before, book, string.Empty, string.Empty, false);

    public static StoryPlacement Between(string earlier, string later) => new(PlacementKind.Between, earlier, later, string.Empty, false);

    public static StoryPlacement InEra(string era) => new(PlacementKind.Era, string.Empty, string.Empty, era.Trim(), false);

    /// <summary>"after “The Salt Road”", "between “A” and “B”", "in the age before the flood".</summary>
    [JsonIgnore]
    public string Where => Kind switch
    {
        PlacementKind.After => $"after “{Book}”",
        PlacementKind.Before => $"before “{Book}”",
        PlacementKind.Between => $"between “{Book}” and “{OtherBook}”",
        _ => Era.StartsWith("in ", StringComparison.OrdinalIgnoreCase) ? Era : $"in {Era}",
    };

    /// <summary>The line the book page shows: "Takes place after “The Salt Road”", and whose word it is.</summary>
    [JsonIgnore]
    public string Line => $"Takes place {Where}" + (Confirmed ? string.Empty : " · Folio’s guess");

    /// <summary>
    /// What a continuity finding says while the placement is a guess, and nothing once it is confirmed.
    /// </summary>
    [JsonIgnore]
    public string Assumption => Confirmed
        ? string.Empty
        : $"This assumes the book takes place {Where}, which hasn’t been confirmed.";

    public StoryPlacement Confirm() => this with { Confirmed = true };

    /// <summary>
    /// The places the owner can choose from on the book page: after or before each other book, and
    /// between each two neighbouring books in the order they were written. The era is asked in words.
    /// </summary>
    public static IReadOnlyList<StoryPlacement> Choices(IReadOnlyList<string> otherBooks)
    {
        var books = (otherBooks ?? []).Where(title => !string.IsNullOrWhiteSpace(title)).ToList();
        var choices = new List<StoryPlacement>();
        choices.AddRange(books.Select(After));
        choices.AddRange(books.Select(Before));
        for (var i = 0; i + 1 < books.Count; i++)
            choices.Add(Between(books[i], books[i + 1]));
        return choices;
    }
}

/// <summary>How a placement is stored on a book: JSON, kind as a word; anything unreadable is no placement.</summary>
public static class StoryPlacementCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Write(StoryPlacement placement) => JsonSerializer.Serialize(placement, Options);

    public static StoryPlacement? Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<StoryPlacement>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>One line on the placement card: its words, and the placement it picks (null: ask for an era).</summary>
public sealed record PlacementOption(string Label, StoryPlacement? Placement);

/// <summary>
/// The paper card on which the owner confirms or changes where a book sits (R21). A guess leads with
/// "Yes, …"; then every other place it could sit; then an era of its own, asked for in words.
/// </summary>
public static class PlacementCard
{
    public const string Title = "Where does this book take place?";

    public const string EraOption = "In an era of its own…";

    public static string? Subtitle(StoryPlacement? current) =>
        current is { Confirmed: false } ? $"Folio guessed {current.Where}." : null;

    public static IReadOnlyList<PlacementOption> Options(StoryPlacement? current, IReadOnlyList<string> otherBooks)
    {
        var options = new List<PlacementOption>();
        if (current is { Confirmed: false })
            options.Add(new PlacementOption($"Yes, {current.Where}", current));
        options.AddRange(StoryPlacement.Choices(otherBooks)
            .Where(choice => current is null || choice.Where != current.Where)
            .Select(choice => new PlacementOption(Capital(choice.Where), choice)));
        options.Add(new PlacementOption(EraOption, null));
        return options;
    }

    private static string Capital(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
}
