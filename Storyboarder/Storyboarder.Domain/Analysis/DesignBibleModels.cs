using System.Text.Json.Serialization;

namespace Storyboarder.Domain.Analysis;

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
    IReadOnlyList<string> Aliases);

// The whole design bible: canonical characters and settings for the novel.
public sealed record DesignBible(
    IReadOnlyList<EntityProfile> Characters,
    IReadOnlyList<EntityProfile> Settings);
