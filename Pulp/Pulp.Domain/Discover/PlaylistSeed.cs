using Pulp.Domain.Catalogue;
using Pulp.Domain.Core;
using Pulp.Domain.Library;

namespace Pulp.Domain.Discover;

/// <summary>Where a made playlist starts (D104): words, one title ("more like this"), or a playlist.</summary>
public enum SeedKind
{
    Prompt,
    Title,
    Playlist,
}

/// <summary>What the model is told about a seed title: what it is, never its id or who watched it.</summary>
public sealed record SeedTitle(string Name, int? Year, TitleKind Kind, IReadOnlyList<string> Genres)
{
    public static SeedTitle Of(CatalogueTitle title)
    {
        ArgumentNullException.ThrowIfNull(title);
        return new SeedTitle(title.Name.Trim(), title.Year, title.Kind, title.Genres.ToList());
    }
}

/// <summary>
/// The start of a model-made playlist (D104, D272). A seed carries only what the titles are — name,
/// year, kind, genres — so "more like this" can never carry anyone's viewing with it. The seed's own
/// titles are never handed back as results.
/// </summary>
public sealed record PlaylistSeed
{
    /// <summary>A prompt is a phrase; anything longer is cut rather than sent whole.</summary>
    public const int MaxPromptLength = 200;

    private PlaylistSeed(SeedKind kind, string prompt, string name, IReadOnlyList<SeedTitle> titles)
    {
        Kind = kind;
        Prompt = prompt;
        Name = name;
        Titles = titles;
    }

    public SeedKind Kind { get; }

    /// <summary>The words asked for; empty for a title or playlist seed.</summary>
    public string Prompt { get; }

    /// <summary>What the playlist is called when the model offers no name: the prompt, the title, or the playlist's name.</summary>
    public string Name { get; }

    public IReadOnlyList<SeedTitle> Titles { get; }

    public static PlaylistSeed FromPrompt(string prompt)
    {
        var phrase = DiscoverText.Phrase(prompt, MaxPromptLength, nameof(prompt));
        return new PlaylistSeed(SeedKind.Prompt, phrase, phrase, []);
    }

    public static PlaylistSeed FromTitle(CatalogueTitle title)
    {
        var seed = SeedTitle.Of(title);
        return new PlaylistSeed(SeedKind.Title, string.Empty, seed.Name, [seed]);
    }

    /// <summary>
    /// A playlist's films and shows, looked up through <paramref name="lookup"/>. Games and franchises
    /// are not titles the catalogue can name, and an entry it cannot find is skipped; a playlist with
    /// nothing left is refused, since "more like nothing" has no answer.
    /// </summary>
    public static PlaylistSeed FromPlaylist(Playlist playlist, Func<PlaylistEntry, CatalogueTitle?> lookup)
    {
        ArgumentNullException.ThrowIfNull(playlist);
        ArgumentNullException.ThrowIfNull(lookup);

        var titles = playlist.Entries
            .Where(entry => entry.Kind is PlaylistEntryKind.Film or PlaylistEntryKind.Show)
            .Select(lookup)
            .OfType<CatalogueTitle>()
            .DistinctBy(title => title.Id)
            .Select(SeedTitle.Of)
            .ToList();

        if (titles.Count == 0)
        {
            throw new ArgumentException(
                $"The playlist '{playlist.Name}' has no film or show the catalogue can name, so there is nothing to seed from.",
                nameof(playlist));
        }

        return new PlaylistSeed(SeedKind.Playlist, string.Empty, playlist.Name, titles);
    }
}

/// <summary>A made-playlist ask as it is sent: the seed, the household's context and the size wanted.</summary>
public sealed record PlaylistQuery
{
    /// <summary>A playlist is an evening or a weekend, not a catalogue.</summary>
    public const int DefaultMaxTitles = 12;

    private PlaylistQuery(PlaylistSeed seed, DiscoverContext context, int maxTitles)
    {
        Seed = seed;
        Context = context;
        MaxTitles = maxTitles;
    }

    public PlaylistSeed Seed { get; }

    public DiscoverContext Context { get; }

    public int MaxTitles { get; }

    public static PlaylistQuery For(PlaylistSeed seed, DiscoverContext context, int maxTitles = DefaultMaxTitles)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTitles);
        return new PlaylistQuery(seed, context, maxTitles);
    }
}
