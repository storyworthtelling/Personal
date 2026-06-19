using System.Text.Json.Serialization;

namespace Storyboarder.Domain.Analysis;

// A single character or setting as observed in one chunk of the novel (the "map" output).
public sealed record EntityMention
{
    public string Name { get; init; }
    public string Description { get; init; }
    public IReadOnlyList<string> Aliases { get; init; }

    [JsonConstructor]
    public EntityMention(string name, string description, IReadOnlyList<string> aliases)
    {
        Name = name;
        Description = description;
        Aliases = aliases;
    }

    public EntityMention(string name, string description)
        : this(name, description, Array.Empty<string>()) { }
}

// The per-chunk extraction result: the characters and settings found in one excerpt.
public sealed record DesignBibleFragment(
    IReadOnlyList<EntityMention> Characters,
    IReadOnlyList<EntityMention> Settings)
{
    public static DesignBibleFragment Empty { get; } =
        new(Array.Empty<EntityMention>(), Array.Empty<EntityMention>());
}

// A canonical, deduplicated entity with its merged visual description (the "reduce" output).
public sealed record EntityProfile(
    string Name,
    string CanonicalDescription,
    IReadOnlyList<string> Aliases);

// The whole design bible: canonical characters and settings for the novel.
public sealed record DesignBible(
    IReadOnlyList<EntityProfile> Characters,
    IReadOnlyList<EntityProfile> Settings);
