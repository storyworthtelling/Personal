using Foreword.Domain.Models;

namespace Foreword.Domain.Brief;

/// <summary>
/// What the owner tends to read (D29). Stored as Drive JSON, following Score's taste profile; the
/// raw evidence stays on the device and only the summary travels.
///
/// <para><b>It ranks, never excludes</b>, and that is the whole decision. A profile that filtered
/// would quietly narrow the brief to what the owner already agrees with — and they asked for eight
/// stories "one of which you will disagree with". A paper that only ever confirmed them would be a
/// worse paper, and they would have no way to see it happening. So taste moves a story up or down
/// the order and nothing is ever dropped for it, however heavy the weight.</para>
/// </summary>
public sealed record ReadingTaste
{
    /// <summary>
    /// Per-section weight: positive rises, negative sinks, absent is neutral. Matched without
    /// regard to case, because the API's capitalisation is not something the owner sees or controls.
    /// </summary>
    public IReadOnlyDictionary<string, int> Sections { get; init; } = new Dictionary<string, int>();

    public static ReadingTaste Empty { get; } = new();

    private static bool IsOpinion(string section) =>
        section.Contains("Opinion", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The stories in reading order. Ranked <b>within</b> whatever the section order already decided
    /// rather than across it — a favoured column must not open the paper, because that is a
    /// different paper.
    /// </summary>
    public static IReadOnlyList<TopStory> Rank(IReadOnlyList<TopStory> stories, ReadingTaste taste)
    {
        if (taste.Sections.Count == 0)
        {
            // No evidence yet. A brief that reordered itself on none would be reordering at random.
            return stories;
        }

        var weights = taste.Sections.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        // News before opinion FIRST, taste second. Ranking across the whole list would let a
        // favoured column open the paper — and the news opening the paper is not a preference, it
        // is what makes it a paper. LINQ's ordering is stable, so equal taste inside each half
        // keeps the desk's order; re-sorting equals is how a stable list becomes a different list
        // every morning for no reason.
        return
        [
            .. stories
                .OrderBy(story => IsOpinion(story.Section) ? 1 : 0)
                .ThenByDescending(story =>
                    weights.TryGetValue(story.Section, out var weight) ? weight : 0),
        ];
    }
}
