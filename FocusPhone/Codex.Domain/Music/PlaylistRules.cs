using Codex.Domain.Design;

namespace Codex.Domain.Music;

/// <summary>Which tag a rule reads. Only fields the scanner actually fills.</summary>
public enum TrackField
{
    Artist,
    AlbumArtist,
    Album,
    Genre,
    Title,
}

public enum RuleOperator
{
    Is,
    Contains,
    IsNot,
}

/// <summary>Whether a track has to satisfy every rule or just one of them.</summary>
public enum RuleMatch
{
    All,
    Any,
}

/// <summary>One clause: a field, a test, and what to test it against.</summary>
public sealed record PlaylistRule(TrackField Field, RuleOperator Operator, string Value);

/// <summary>
/// A rule-based playlist's definition, and the evaluator that turns it into tracks.
///
/// The count this produces is the whole argument for the editor: you watch the set
/// shrink as you add clauses, and commit once it says what you meant. That means it
/// is evaluated on every keystroke, so it stays a pure pass over the catalog with no
/// caching to invalidate.
///
/// Matching is the same case- and accent-insensitive comparison the filter fields
/// use, for the same reason — nobody types the accent in Bartók to find a record
/// they own, and a rule that missed those would be quietly wrong rather than
/// visibly wrong.
/// </summary>
public sealed record PlaylistRules(RuleMatch Match, IReadOnlyList<PlaylistRule> Rules)
{
    public static PlaylistRules Empty { get; } = new(RuleMatch.All, []);

    /// <summary>
    /// The tracks these rules select, in catalog order.
    ///
    /// A rule set that says nothing selects nothing. Under All, an empty set is
    /// vacuously true and would select the entire library — a startling thing to
    /// read in a live count above an editor you have not typed in yet — so it is
    /// special-cased rather than left to logic. A clause whose value is still blank
    /// is the same situation mid-edit, and is skipped.
    /// </summary>
    public static IReadOnlyList<Track> Apply(PlaylistRules rules, IReadOnlyList<Track> catalog)
    {
        var live = rules.Rules.Where(rule => !string.IsNullOrWhiteSpace(rule.Value)).ToList();
        if (live.Count == 0)
        {
            return [];
        }

        return [.. catalog.Where(track => rules.Match == RuleMatch.All
            ? live.All(rule => Matches(rule, track))
            : live.Any(rule => Matches(rule, track)))];
    }

    private static bool Matches(PlaylistRule rule, Track track)
    {
        var field = Read(rule.Field, track);
        return rule.Operator switch
        {
            RuleOperator.Is => FilterMatch.Equals(field, rule.Value),
            RuleOperator.IsNot => !FilterMatch.Equals(field, rule.Value),
            _ => FilterMatch.Matches(field, rule.Value),
        };
    }

    private static string Read(TrackField field, Track track) => field switch
    {
        TrackField.Artist => track.DisplayArtist,
        TrackField.AlbumArtist => track.DisplayAlbumArtist,
        TrackField.Album => track.DisplayAlbum,
        TrackField.Genre => track.DisplayGenre,
        _ => track.DisplayTitle,
    };
}
