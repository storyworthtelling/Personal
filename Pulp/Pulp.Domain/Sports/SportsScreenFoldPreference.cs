namespace Pulp.Domain.Sports;

/// <summary>
/// Where the Sports tab's fold choices are remembered (D241): on this phone only, never in the shared
/// household file, the way "Hide watched" is (D292). The file name and the key prefix are frozen
/// (<c>PulpInvariantsTests</c>): an upgraded phone finds what the viewer opened by them.
/// </summary>
public static class SportsFoldPreference
{
    /// <summary>The preferences file, named once (never derived from a member name).</summary>
    public const string FileName = "pulp-sports-fold";

    public const string KeyPrefix = "fold:";

    /// <summary>
    /// Every choice as a preference key and its open-or-folded value. The key folds case, since a league
    /// id compares without it: one league, one choice, however its id is spelled.
    /// </summary>
    public static IReadOnlyDictionary<string, bool> ToPreferences(FoldMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        return memory.Choices.ToDictionary(choice => KeyPrefix + choice.Key.ToLowerInvariant(), choice => choice.Value);
    }

    /// <summary>The memory those keys describe; a key that is not a fold choice is not ours and is left alone.</summary>
    public static FoldMemory FromPreferences(IReadOnlyDictionary<string, bool> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var choices = stored
            .Where(entry => entry.Key.StartsWith(KeyPrefix, StringComparison.Ordinal))
            .ToDictionary(entry => entry.Key[KeyPrefix.Length..], entry => entry.Value);
        return FoldMemory.From(choices);
    }
}
