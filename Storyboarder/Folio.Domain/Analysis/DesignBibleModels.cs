using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

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
    IReadOnlyList<EntityProfile> Settings)
{
    // How many of the most-mentioned entities stand in when a scene names nobody ("She crossed the room").
    private const int FallbackCharacters = 3;
    private const int FallbackSettings = 2;

    // The part of the bible a single scene needs: the characters and settings it names, by name or alias.
    // The whole bible runs to ~90,000 tokens on a long book, and a scene prompt that sends it all pays for
    // that every time to be told about people who are not in the scene. A scene that names no one still
    // needs someone to draw, so the book's most-mentioned entities stand in.
    public DesignBible ForScene(string sceneText)
    {
        var characters = Characters.Where(entity => Appears(sceneText, entity)).ToList();
        var settings = Settings.Where(entity => Appears(sceneText, entity)).ToList();

        if (characters.Count == 0 && settings.Count == 0)
        {
            characters = Characters.OrderByDescending(entity => entity.ChunkMentions).Take(FallbackCharacters).ToList();
            settings = Settings.OrderByDescending(entity => entity.ChunkMentions).Take(FallbackSettings).ToList();
        }

        return new DesignBible(characters, settings);
    }

    // Whole-word, case-insensitive: "Ann" is not named by "cannon" or "banner".
    public static bool Names(string text, string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name.Trim())}(?![\p{{L}}\p{{N}}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool Appears(string sceneText, EntityProfile entity) =>
        Names(sceneText, entity.Name) || entity.Aliases.Any(alias => Names(sceneText, alias));
}
