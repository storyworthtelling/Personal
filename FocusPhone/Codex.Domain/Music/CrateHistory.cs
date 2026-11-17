using System.Text.Json;

namespace Codex.Domain.Music;

/// <summary>One record a past crate offered, and the Friday it was offered on.</summary>
public sealed record OfferedPick(string Title, string Artist, DiscoveryKind Kind, DateOnly Week);

/// <summary>
/// What the crate has offered in recent weeks — its memory.
///
/// The crate came back near-identical every Friday because it had none: the same taste
/// profile and the same library went to the recommender each week, so the same records
/// came out. The history goes in with the request (so the recommender can look further
/// afield) and is enforced on the way out (<see cref="Fresh"/>), because asking a model
/// not to repeat itself is worth doing and not worth trusting.
///
/// It forgets after <see cref="Weeks"/> weeks, so a record passed over in spring can come
/// round again, and keeps at most <see cref="MaxPicks"/> so the prompt stays small.
/// </summary>
public sealed class CrateHistory
{
    /// <summary>How many weeks a pick is remembered.</summary>
    public const int Weeks = 12;

    /// <summary>The most picks remembered, newest kept.</summary>
    public const int MaxPicks = 240;

    private CrateHistory(IReadOnlyList<OfferedPick> offered) => Offered = offered;

    public static CrateHistory Empty { get; } = new([]);

    /// <summary>The remembered picks, oldest first.</summary>
    public IReadOnlyList<OfferedPick> Offered { get; }

    public static CrateHistory From(IEnumerable<OfferedPick> offered) => new([.. offered]);

    /// <summary>
    /// Adds a crate offered in <paramref name="week"/>, dropping anything older than the
    /// window and anything over the cap. A pick already remembered moves to the newer week
    /// rather than being held twice, so folding the same crate in again changes nothing.
    /// </summary>
    public CrateHistory Remember(IEnumerable<Recommendation> crate, DateOnly week)
    {
        var oldest = week.AddDays(-7 * Weeks);
        var incoming = crate
            .Where(pick => !string.IsNullOrWhiteSpace(pick.Title))
            .Select(pick => new OfferedPick(pick.Title, pick.Artist, pick.Kind, week))
            .ToList();
        var incomingKeys = incoming.Select(Key).ToHashSet(StringComparer.Ordinal);

        var kept = Offered
            .Where(old => old.Week > oldest && !incomingKeys.Contains(Key(old)))
            .Concat(incoming.DistinctBy(Key))
            .ToList();

        return new CrateHistory(kept.Count > MaxPicks ? kept[^MaxPicks..] : kept);
    }

    /// <summary>Whether a past crate already offered this record (spelling differences aside).</summary>
    public bool HasOffered(Recommendation pick)
    {
        var key = Discoveries.Key(pick);
        return Offered.Any(old => Key(old) == key);
    }

    /// <summary>The picks no recent crate has offered, in their order.</summary>
    public IReadOnlyList<Recommendation> Fresh(IEnumerable<Recommendation> picks) =>
        [.. picks.Where(pick => !HasOffered(pick))];

    private static string Key(OfferedPick pick) =>
        Discoveries.Key(new Recommendation(pick.Title, pick.Artist, Kind: pick.Kind));
}

/// <summary>The history's stored shape: a JSON list. Anything unreadable reads as no history.</summary>
public static class CrateHistoryCodec
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static string Write(CrateHistory history) => JsonSerializer.Serialize(history.Offered, Options);

    public static CrateHistory Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CrateHistory.Empty;
        }

        try
        {
            return CrateHistory.From(JsonSerializer.Deserialize<List<OfferedPick>>(json, Options) ?? []);
        }
        catch (JsonException)
        {
            return CrateHistory.Empty;
        }
    }
}
