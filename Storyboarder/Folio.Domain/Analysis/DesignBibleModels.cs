using System.Text.Json.Serialization;

namespace Folio.Domain.Analysis;

// A single character or setting as observed in one chunk of the novel (the "map" output). Scale is an
// approximate physical size (a character's height/build, a setting's extent) so illustrations stay
// proportionate; it may be "" when the excerpt gives no size cue.
public sealed record EntityMention
{
    public string Name { get; init; }
    public string Description { get; init; }
    public string Scale { get; init; }
    public IReadOnlyList<string> Aliases { get; init; }

    [JsonConstructor]
    public EntityMention(string name, string description, string scale, IReadOnlyList<string> aliases)
    {
        Name = name;
        Description = description;
        Scale = scale ?? string.Empty;
        Aliases = aliases;
    }

    public EntityMention(string name, string description, IReadOnlyList<string> aliases)
        : this(name, description, string.Empty, aliases) { }

    public EntityMention(string name, string description)
        : this(name, description, string.Empty, Array.Empty<string>()) { }
}

// The per-chunk extraction result: the characters and settings found in one excerpt.
public sealed record DesignBibleFragment(
    IReadOnlyList<EntityMention> Characters,
    IReadOnlyList<EntityMention> Settings)
{
    public static DesignBibleFragment Empty { get; } =
        new(Array.Empty<EntityMention>(), Array.Empty<EntityMention>());
}

// A canonical, deduplicated entity with its merged visual description (the "reduce" output). Scale is
// the approximate physical size carried through from the mentions so panels keep entities in proportion.
public sealed record EntityProfile(
    string Name,
    string CanonicalDescription,
    string Scale,
    IReadOnlyList<string> Aliases,
    // How many distinct chunks mentioned this entity. A novel names a great many one-off entities (a
    // barman in one scene, a road crossed once), and drawing a reference sheet for each of them cost the
    // owner's last book 126 image calls for an ensemble of maybe a dozen. Only entities seen in more than
    // one chunk earn a sheet; the rest stay in the bible so their descriptions still reach the prompts.
    int ChunkMentions = 0);

// The whole design bible: canonical characters and settings for the novel.
public sealed record DesignBible(
    IReadOnlyList<EntityProfile> Characters,
    IReadOnlyList<EntityProfile> Settings);
