namespace Storyboarder.Domain.Analysis;

// Reduces the per-chunk extraction fragments into one deduplicated design bible. Entities are
// matched by normalized name or by any shared alias, so "Tom" and "Thomas Reed" collapse into a
// single profile whose description merges the complementary details seen across chunks.
public sealed class DesignBibleMerger
{
    public DesignBible Merge(IEnumerable<DesignBibleFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        var materialized = fragments.ToList();
        // One fragment per chunk, so the fragment's index is the chunk it came from. That index is
        // carried alongside each mention because how many DISTINCT chunks name an entity is what decides
        // whether it earns a reference sheet — a count of mentions alone would let one chatty chunk
        // promote a walk-on to recurring cast.
        var characters = MergeMentions(materialized.SelectMany((f, chunk) => f.Characters.Select(m => (Mention: m, Chunk: chunk))));
        var settings = MergeMentions(materialized.SelectMany((f, chunk) => f.Settings.Select(m => (Mention: m, Chunk: chunk))));
        return new DesignBible(characters, settings);
    }

    private static IReadOnlyList<EntityProfile> MergeMentions(IEnumerable<(EntityMention Mention, int Chunk)> mentions)
    {
        var profiles = new List<MutableProfile>();
        var byKey = new Dictionary<string, MutableProfile>(StringComparer.Ordinal);

        foreach (var (mention, chunk) in mentions)
        {
            if (string.IsNullOrWhiteSpace(mention.Name))
                continue;

            var keys = BuildKeys(mention);
            var profile = keys
                .Select(k => byKey.TryGetValue(k, out var found) ? found : null)
                .FirstOrDefault(found => found is not null);

            if (profile is null)
            {
                profile = new MutableProfile(mention.Name.Trim());
                profiles.Add(profile);
            }

            foreach (var key in keys)
                byKey[key] = profile;

            profile.Chunks.Add(chunk);
            MergeInto(profile, mention);
        }

        return profiles
            .Select(p => new EntityProfile(
                p.Name, string.Join(" ", p.Descriptions), p.Scale, p.Aliases.ToList(), p.Chunks.Count))
            .ToList();
    }

    private static List<string> BuildKeys(EntityMention mention)
    {
        var keys = new List<string> { Normalize(mention.Name) };
        foreach (var alias in mention.Aliases ?? [])
            if (!string.IsNullOrWhiteSpace(alias))
                keys.Add(Normalize(alias));
        return keys;
    }

    private static void MergeInto(MutableProfile profile, EntityMention mention)
    {
        if (!string.IsNullOrWhiteSpace(mention.Description))
        {
            var description = mention.Description.Trim();
            if (!profile.Descriptions.Contains(description))
                profile.Descriptions.Add(description);
        }

        // First non-empty scale seen for the entity wins: a later chunk usually just re-mentions the
        // character without re-stating their size, so we keep the first concrete measurement.
        if (string.IsNullOrWhiteSpace(profile.Scale) && !string.IsNullOrWhiteSpace(mention.Scale))
            profile.Scale = mention.Scale.Trim();

        AddAlias(profile, mention.Name);
        foreach (var alias in mention.Aliases ?? [])
            AddAlias(profile, alias);
    }

    private static void AddAlias(MutableProfile profile, string? alias)
    {
        var trimmed = alias?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return;
        // Don't record the canonical name as its own alias.
        if (string.Equals(Normalize(trimmed), Normalize(profile.Name), StringComparison.Ordinal))
            return;
        if (!profile.Aliases.Contains(trimmed))
            profile.Aliases.Add(trimmed);
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private sealed class MutableProfile(string name)
    {
        public string Name { get; } = name;
        public string Scale { get; set; } = string.Empty;
        public List<string> Descriptions { get; } = [];
        public List<string> Aliases { get; } = [];

        // Distinct chunk indices this entity was named in. A set, not a counter: an alias resolving to
        // the same profile within one chunk must not count as a second sighting.
        public HashSet<int> Chunks { get; } = [];
    }
}
